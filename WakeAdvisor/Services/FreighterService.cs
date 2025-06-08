using System;
using System.Collections.Generic;
using System.Net.Http; // Keep for now, might be used by other methods not shown
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging; // Added for logging
using System.IO;
using System.Text.Json.Serialization; // Ensure this is present for JsonIgnoreCondition
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks; // Added for Task
using System.Diagnostics; // For Stopwatch

namespace WakeAdvisor.Services
{
    // Represents a geographic point (latitude, longitude)
    public class GeoPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    // Represents raw vessel data from the AIS API
    public class AISVesselData
    {
        public string? MMSI { get; set; } // Maritime Mobile Service Identity
        public string? Name { get; set; }
        public string? VesselType { get; set; } // Textual, e.g., "Cargo", "Tanker"
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SOG { get; set; } // Speed Over Ground (knots)
        public double COG { get; set; } // Course Over Ground (degrees)
        public DateTime Timestamp { get; set; }
    }

    // Represents a filtered southbound freighter and its ETA
    public class FreighterInfo
    {
        public string? Name { get; set; }
        public string? MMSI { get; set; }
        public double CurrentSOG { get; set; }
        public DateTime ETAAtKingston { get; set; }
        public double DistanceToKingstonNM { get; set; } // Nautical miles
    }

    // DTO for the subscription message to AIS Stream
    public class AisSubscriptionMessageDto
    {
        [JsonPropertyName("APIKey")]
        public string? ApiKey { get; set; }

        [JsonPropertyName("BoundingBoxes")]
        public List<List<List<double>>>? BoundingBoxes { get; set; }

        [JsonPropertyName("FilterMessageTypes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FilterMessageTypes { get; set; }
    }

    // Outer envelope for any AIS Stream message
    public class AisStreamEnvelopeDto
    {
        [JsonPropertyName("MessageType")]
        public string? MessageType { get; set; }

        [JsonPropertyName("MetaData")]
        public AisStreamMetaDataDto? MetaData { get; set; }

        [JsonPropertyName("Message")]
        public JsonElement Message { get; set; } // Raw JSON of the inner message part
    }

    public class AisStreamMetaDataDto
    {
        [JsonPropertyName("ShipName")]
        public string? ShipName { get; set; }

        [JsonPropertyName("MMSI")] // MMSI in MetaData is a string
        public string? Mmsi { get; set; }

        [JsonPropertyName("time_utc")]
        public DateTime TimeUtc {get; set;} // Timestamp of when the message was received by the AISStream server
    }

    // Specific DTO for the content of Message.PositionReport
    public class AisPositionReportDto
    {
        [JsonPropertyName("UserID")] // This is the MMSI as an integer
        public int UserId { get; set; }

        [JsonPropertyName("Latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("Longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("Sog")] // Speed over ground in 0.1 knot steps (0-102.2 knots). 1023 = not available.
        public double? Sog { get; set; }

        [JsonPropertyName("Cog")] // Course over ground in 0.1 degree steps (0-359.9 degrees). 3600 = not available.
        public double? Cog { get; set; }

        [JsonPropertyName("TrueHeading")] // 0-359 degrees, 511 = not available
        public int? TrueHeading { get; set; }

        [JsonPropertyName("ShipType")] // AIS numeric ship type
        public int? ShipType { get; set; }

        [JsonPropertyName("Timestamp")] // UTC timestamp of the AIS message generation
        public DateTime Timestamp { get; set; }
    }


    public class FreighterService
    {
        // HttpClient might not be strictly needed if all AIS comms are via WebSocket
        // private readonly HttpClient _httpClient; 
        private readonly IConfiguration _configuration;
        private readonly ILogger<FreighterService> _logger;

        // Kingston Point reference coordinates
        private static readonly GeoPoint KingstonPoint = new GeoPoint
        {
            Latitude = 41.9275,
            Longitude = -73.9639
        };

        // Southbound bearing range (degrees True)
        private const double SouthboundMin = 160.0;
        private const double SouthboundMax = 220.0;

        public FreighterService(IConfiguration configuration, ILogger<FreighterService> logger)
        {
            // _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        // Main entry point: gets southbound freighters and their ETAs
        public async Task<List<FreighterInfo>> GetSouthboundFreighterInfoAsync(DateTime selectedDate)
        {
            // Only allow live AIS tracking for today or tomorrow
            var today = DateTime.Now.Date;
            var tomorrow = today.AddDays(1);
            if (selectedDate.Date != today && selectedDate.Date != tomorrow)
            {
                // For future dates, return an empty list (or handle as needed)
                return new List<FreighterInfo>();
            }

            // TODO: Call AIS API and retrieve vessel data for the Kingston area
            List<AISVesselData> vessels = await GetAISVesselsAsync(selectedDate);
            // Filter vessels by type (freighter/cargo), position, direction, and ETA window
            var filtered = new List<FreighterInfo>();
            foreach (var vessel in vessels)
            {
                // Only consider freighter/cargo types (adjust as needed for your AIS data)
                if (string.IsNullOrEmpty(vessel.VesselType) ||
                    !(vessel.VesselType.Equals("Cargo", StringComparison.OrdinalIgnoreCase) ||
                      vessel.VesselType.Equals("Tanker", StringComparison.OrdinalIgnoreCase) ||
                      vessel.VesselType.ToLower().Contains("freighter"))) // Keep freighter for broader custom types
                    continue;

                // Vessel must be north of Kingston Point
                if (vessel.Latitude <= KingstonPoint.Latitude)
                    continue;

                // Vessel must be heading southbound
                if (vessel.COG < SouthboundMin || vessel.COG > SouthboundMax)
                    continue;

                // Calculate distance to Kingston (nautical miles)
                var vesselPoint = new GeoPoint { Latitude = vessel.Latitude, Longitude = vessel.Longitude };
                double distanceNM = CalculateDistanceNM(vesselPoint, KingstonPoint);

                // Avoid divide by zero and filter out stationary vessels
                if (vessel.SOG <= 0.1)
                    continue;

                // Calculate ETA in minutes
                double etaMinutes = (distanceNM / vessel.SOG) * 60.0;

                // Only include vessels with ETA between 15 and 50 minutes
                if (etaMinutes < 15 || etaMinutes > 50)
                    continue;
                
                // Add to results
                filtered.Add(new FreighterInfo
                {
                    Name = vessel.Name,
                    MMSI = vessel.MMSI,
                    CurrentSOG = vessel.SOG,
                    DistanceToKingstonNM = distanceNM,
                    ETAAtKingston = vessel.Timestamp.AddMinutes(etaMinutes) // Assuming vessel.Timestamp is UTC
                });
            }
            return filtered;
        }

        // Helper method to map AIS numeric ship type to a textual description
        private string MapShipTypeNumericToText(int? numericType)
        {
            if (!numericType.HasValue) return "Unknown";
            int type = numericType.Value;

            // Based on common AIS ship type categorizations
            // See https://www.navcen.uscg.gov/ais-messages (e.g., Type 5, field 10)
            // AIS Standard Ship Types:
            // 70-79: Cargo ships
            // 80-89: Tankers
            // Consider others if necessary (e.g., 60-69 Passenger, 30 Fishing)
            if (type >= 70 && type <= 79) return "Cargo";
            if (type >= 80 && type <= 89) return "Tanker";
            // Add more specific mappings if needed
            // if (type == 0) return "Not available or no ship"; // Default value
            // if (type == 30) return "Fishing";
            // if (type >= 60 && type <= 69) return "Passenger";
            return $"Type {type}"; // Default for unmapped/other types
        }

        // Fetches AIS vessel data from the AIS Stream API using WebSockets
        private async Task<List<AISVesselData>> GetAISVesselsAsync(DateTime selectedDate)
        {
            var apiKey = _configuration["AISStreamApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("AIS Stream API Key is not configured.");
                return new List<AISVesselData>();
            }

            var vessels = new List<AISVesselData>();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            // Kingston Bounding Box: [[LAT_SOUTH, LON_WEST], [LAT_NORTH, LON_EAST]]
            // Approx. +/- 0.5 degrees from Kingston Point (41.9275 N, -73.9639 W)
            // Lat: 41.4275 to 42.4275, Lon: -74.4639 to -73.4639
            var boundingBoxes = new List<List<List<double>>>
            {
                new List<List<double>>
                {
                    new List<double> { 41.4275, -74.4639 }, // South-West corner
                    new List<double> { 42.4275, -73.4639 }  // North-East corner
                }
            };

            var subscriptionMessage = new AisSubscriptionMessageDto
            {
                ApiKey = apiKey,
                BoundingBoxes = boundingBoxes,
                FilterMessageTypes = new List<string> { "PositionReport" } // Explicitly request PositionReport
            };
            var subscriptionJson = JsonSerializer.Serialize(subscriptionMessage, jsonOptions);
            _logger.LogInformation("AIS Stream Subscription JSON: {SubscriptionJson}", subscriptionJson);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var client = new ClientWebSocket();
            Stopwatch listenStopwatch = new Stopwatch();

            try
            {
                _logger.LogInformation("Connecting to AIS Stream API (wss://stream.aisstream.io/v0/stream)...");
                await client.ConnectAsync(new Uri("wss://stream.aisstream.io/v0/stream"), cts.Token);
                _logger.LogInformation("Connected. Sending subscription message.");

                var sendBuffer = Encoding.UTF8.GetBytes(subscriptionJson);
                await client.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, cts.Token);
                _logger.LogInformation("Subscription message sent. Listening for data...");

                var receiveBuffer = new byte[8192];
                var dataCollectionEndTime = DateTime.UtcNow.AddSeconds(45);
                listenStopwatch.Start();
                int receiveLoopIterations = 0;

                while (client.State == WebSocketState.Open && DateTime.UtcNow < dataCollectionEndTime && !cts.Token.IsCancellationRequested)
                {
                    receiveLoopIterations++;
                    // Changed to LogInformation to ensure it appears on the console
                    _logger.LogInformation("Receive loop iteration {Iteration}, elapsed listen time: {ElapsedMs}ms. Attempting to receive...", receiveLoopIterations, listenStopwatch.ElapsedMilliseconds);
                    
                    // Increased timeout for individual message to 30 seconds
                    using var messageCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); 
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, messageCts.Token);
                    
                    WebSocketReceiveResult result;
                    using var ms = new MemoryStream();
                    try
                    {
                        do
                        {
                            var segment = new ArraySegment<byte>(receiveBuffer);
                            result = await client.ReceiveAsync(segment, linkedCts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                _logger.LogWarning("AIS Stream API closed the connection. Status: {CloseStatus}, Description: {StatusDescription}", result.CloseStatus, result.CloseStatusDescription);
                                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client acknowledging close", CancellationToken.None);
                                listenStopwatch.Stop();
                                return vessels;
                            }
                            ms.Write(segment.Array!, segment.Offset, result.Count);
                        } while (!result.EndOfMessage && !linkedCts.Token.IsCancellationRequested);
                    }
                    catch (OperationCanceledException oce) when (messageCts.Token.IsCancellationRequested)
                    {
                        _logger.LogWarning(oce, "Timeout (30s) receiving a single WebSocket message/chunk. Iteration: {Iteration}. Elapsed: {ElapsedMs}ms. Continuing to listen if time permits.", receiveLoopIterations, listenStopwatch.ElapsedMilliseconds);
                        continue; // Continue to the next iteration of the while loop
                    }
                    // If linkedCts was cancelled by cts (overall timeout), it will be caught by the outer catch block.

                    ms.Seek(0, SeekOrigin.Begin);
                    if (ms.Length > 0)
                    {
                        var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                        _logger.LogInformation("Received raw data (Length: {Length}): {MessageJson}", ms.Length, messageJson); // Changed to LogInformation

                        try
                        {
                            var envelope = JsonSerializer.Deserialize<AisStreamEnvelopeDto>(messageJson, jsonOptions);
                            if (envelope == null)
                            {
                                _logger.LogWarning("Failed to deserialize AIS message envelope. Raw JSON: {MessageJson}", messageJson);
                                continue;
                            }

                            if (envelope.MessageType == "PositionReport")
                            {
                                if (envelope.Message.TryGetProperty("PositionReport", out JsonElement positionReportElement))
                                {
                                    var positionReport = JsonSerializer.Deserialize<AisPositionReportDto>(positionReportElement.GetRawText(), jsonOptions);
                                    if (positionReport != null)
                                    {
                                        double sogKnots = (positionReport.Sog.HasValue && positionReport.Sog.Value < 1023) ? positionReport.Sog.Value / 10.0 : 0.0;
                                        double cogDegrees = (positionReport.Cog.HasValue && positionReport.Cog.Value < 3600) ? positionReport.Cog.Value / 10.0 : 0.0;

                                        vessels.Add(new AISVesselData
                                        {
                                            MMSI = positionReport.UserId.ToString(),
                                            Name = envelope.MetaData?.ShipName,
                                            Latitude = positionReport.Latitude,
                                            Longitude = positionReport.Longitude,
                                            SOG = sogKnots,
                                            COG = cogDegrees,
                                            Timestamp = positionReport.Timestamp, 
                                            VesselType = MapShipTypeNumericToText(positionReport.ShipType)
                                        });
                                        _logger.LogInformation("Processed PositionReport for MMSI: {MMSI}, Name: {Name}, Lat: {Lat}, Lon: {Lon}, SOG: {Sog}kn, COG: {Cog}deg, Type: {ShipTypeNum}, Timestamp: {Ts}", 
                                            positionReport.UserId, envelope.MetaData?.ShipName ?? "N/A", positionReport.Latitude, positionReport.Longitude, sogKnots, cogDegrees, positionReport.ShipType, positionReport.Timestamp);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("'PositionReport' property missing in Message object for MessageType 'PositionReport'. Raw Message part: {MessageJson}", envelope.Message.GetRawText());
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Received AIS message of type: {MessageType}. MetaData MMSI: {MetaMmsi}, MetaData ShipName: {MetaShipName}. Raw content: {MessageJson}", 
                                    envelope.MessageType, envelope.MetaData?.Mmsi ?? "N/A", envelope.MetaData?.ShipName ?? "N/A", messageJson);
                            }
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError(jsonEx, "Error deserializing AIS message (JSON likely incomplete or not an envelope): {MessageJson}", messageJson);
                        }
                    }
                    else if (result.MessageType != WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Received empty message or EndOfMessage without data. MessageType: {MessageType}. Iteration: {Iteration}", result.MessageType, receiveLoopIterations);
                    }
                }
                listenStopwatch.Stop();
                _logger.LogInformation("Finished listening for AIS data after {ElapsedMs}ms. Loop iterations: {Iterations}", listenStopwatch.ElapsedMilliseconds, receiveLoopIterations);
            }
            catch (OperationCanceledException ex) 
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.LogInformation(ex, "AIS data collection task was cancelled due to overall timeout ({TotalSeconds}s).", TimeSpan.FromSeconds(60).TotalSeconds);
                }
                else
                {
                     _logger.LogWarning(ex, "AIS data collection task was cancelled (not by overall timeout, possibly by individual message timeout or other cancellation).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AIS vessel data.");
            }
            finally
            {
                if (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived)
                {
                    _logger.LogInformation("Closing AIS Stream connection.");
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", CancellationToken.None);
                }
                _logger.LogInformation("AIS Stream connection process finished. Collected {Count} unique vessels.", vessels.Count);
            }
            
            return vessels
                .GroupBy(v => v.MMSI)
                .Select(g => g.OrderByDescending(v => v.Timestamp).First())
                .ToList();
        }
        
        // Utility: Calculate distance (nautical miles) between two points (Haversine formula)
        public static double CalculateDistanceNM(GeoPoint a, GeoPoint b)
        {
            // Radius of the Earth in nautical miles
            const double R = 3440.065;

            // Convert degrees to radians
            double lat1 = a.Latitude * Math.PI / 180.0;
            double lon1 = a.Longitude * Math.PI / 180.0;
            double lat2 = b.Latitude * Math.PI / 180.0;
            double lon2 = b.Longitude * Math.PI / 180.0;

            // Haversine formula
            double dLat = lat2 - lat1;
            double dLon = lon2 - lon1;
            double h = Math.Pow(Math.Sin(dLat / 2), 2) +
                       Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
            double c = 2 * Math.Asin(Math.Sqrt(h));
            double distance = R * c;

            return distance;
        }
    }
}
