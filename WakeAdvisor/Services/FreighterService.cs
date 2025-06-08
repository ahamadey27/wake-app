using System;
using System.Collections.Generic;
using System.Net.Http; // Keep for now, might be used by other methods not shown
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging; // Added for logging
using System.IO;
using System.Text.Json.Serialization;
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
        public List<List<List<double>>>? BoundingBoxes { get; set; } // e.g. [[[lat_south, lon_west], [lat_north, lon_east]]]
    }

    // Outer container for any AIS Stream message
    public class AisMessageContainerDto
    {
        [JsonPropertyName("MessageType")]
        public string? MessageType { get; set; }

        [JsonPropertyName("Message")]
        public JsonElement Message { get; set; } // Use JsonElement to deserialize specific message type later
    }

    // Specific DTO for PositionReport messages (nested within AisMessageContainerDto.Message)
    public class PositionReportDto
    {
        [JsonPropertyName("UserID")] // This is the MMSI
        public int Mmsi { get; set; }

        // Name is often part of ShipStaticData message, not always in PositionReport.
        // For simplicity, we'll try to get it if available, but it might be null.
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("Latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("Longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("Sog")] // Speed over ground in 0.1 knots. API: 1023=N/A
        public double? Sog { get; set; }

        [JsonPropertyName("Cog")] // Course over ground in 0.1 degrees. API: 3600=N/A
        public double? Cog { get; set; }

        [JsonPropertyName("TrueHeading")] // 0-359 degrees, 511 = not available
        public int? TrueHeading { get; set; }

        [JsonPropertyName("ShipType")] // AIS numeric ship type
        public int? ShipType { get; set; }

        [JsonPropertyName("Timestamp")] // UTC timestamp as string e.g. "2021-09-01T12:34:56.789Z"
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
                BoundingBoxes = boundingBoxes
                // Consider adding: FilterMessageTypes = new List<string> { "PositionReport" }
            };
            var subscriptionJson = JsonSerializer.Serialize(subscriptionMessage, jsonOptions);
            _logger.LogInformation("AIS Stream Subscription JSON: {SubscriptionJson}", subscriptionJson);

            // Increased overall timeout and data collection window
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Overall timeout for the operation
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
                // Increased data collection window after subscription
                var dataCollectionEndTime = DateTime.UtcNow.AddSeconds(45); 
                listenStopwatch.Start();
                int receiveLoopIterations = 0;

                while (client.State == WebSocketState.Open && DateTime.UtcNow < dataCollectionEndTime && !cts.Token.IsCancellationRequested)
                {
                    receiveLoopIterations++;
                    _logger.LogDebug("Receive loop iteration {Iteration}, elapsed listen time: {ElapsedMs}ms", receiveLoopIterations, listenStopwatch.ElapsedMilliseconds);

                    using var messageCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // Increased timeout for individual message
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, messageCts.Token);
                    
                    WebSocketReceiveResult result;
                    using var ms = new MemoryStream();
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

                    if (linkedCts.Token.IsCancellationRequested && !cts.Token.IsCancellationRequested && !messageCts.Token.IsCancellationRequested)
                    {
                        // This case should ideally not happen if linkedCts is only cancelled by cts or messageCts
                         _logger.LogWarning("LinkedCts cancelled without main CTS or message CTS being the primary cause.");
                    }
                    else if (messageCts.Token.IsCancellationRequested)
                    {
                        _logger.LogWarning("Timeout receiving a single WebSocket message chunk after {ElapsedMs}ms in this attempt.", listenStopwatch.ElapsedMilliseconds);
                        // Continue to next iteration of the outer while loop to try receiving again if overall time permits
                        continue;
                    }
                     else if (cts.Token.IsCancellationRequested)
                    {
                        _logger.LogInformation("Overall cancellation token triggered during receive.");
                        break; // Exit while loop
                    }
                    
                    ms.Seek(0, SeekOrigin.Begin);
                    if (ms.Length > 0)
                    {
                        var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                        _logger.LogDebug("Received AIS message (Length: {Length}): {MessageJson}", ms.Length, messageJson);

                        try
                        {
                            var container = JsonSerializer.Deserialize<AisMessageContainerDto>(messageJson, jsonOptions);
                            if (container?.MessageType == "PositionReport")
                            {
                                var positionReport = JsonSerializer.Deserialize<PositionReportDto>(container.Message.GetRawText(), jsonOptions);
                                if (positionReport != null)
                                {
                                    double sogKnots = (positionReport.Sog.HasValue && positionReport.Sog.Value < 1023) ? positionReport.Sog.Value / 10.0 : 0.0;
                                    double cogDegrees = (positionReport.Cog.HasValue && positionReport.Cog.Value < 3600) ? positionReport.Cog.Value / 10.0 : 0.0;

                                    vessels.Add(new AISVesselData
                                    {
                                        MMSI = positionReport.Mmsi.ToString(),
                                        Name = positionReport.Name,
                                        Latitude = positionReport.Latitude,
                                        Longitude = positionReport.Longitude,
                                        SOG = sogKnots,
                                        COG = cogDegrees,
                                        Timestamp = positionReport.Timestamp,
                                        VesselType = MapShipTypeNumericToText(positionReport.ShipType)
                                    });
                                     _logger.LogInformation("Processed PositionReport for MMSI: {MMSI}", positionReport.Mmsi);
                                }
                            }
                            // else: Log other message types if interested
                            // else if (container != null) { _logger.LogDebug("Received other message type: {MessageType}", container.MessageType); }

                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError(jsonEx, "Error deserializing AIS message: {MessageJson}", messageJson);
                        }
                    }
                    else if (result.MessageType != WebSocketMessageType.Close) // No data received, but not a close message
                    {
                        _logger.LogDebug("Received empty message or EndOfMessage without data. MessageType: {MessageType}", result.MessageType);
                    }
                }
                 listenStopwatch.Stop();
                _logger.LogInformation("Finished listening for AIS data after {ElapsedMs}ms. Loop iterations: {Iterations}", listenStopwatch.ElapsedMilliseconds, receiveLoopIterations);

            }
            catch (WebSocketException wsEx)
            {
                _logger.LogError(wsEx, "WebSocket error while communicating with AIS Stream API.");
            }
            catch (OperationCanceledException) 
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.LogInformation("AIS data collection task was cancelled due to overall timeout ({TotalSeconds}s).", cts.Token.CanBeCanceled ? TimeSpan.FromSeconds(60).TotalSeconds : 0);
                }
                else
                {
                     _logger.LogInformation("AIS data collection task was cancelled (not by overall timeout).");
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
