using System;
using System.Collections.Generic;
using System.Net.Http; // Keep for now, might be used by other methods not shown
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json.Serialization; // Ensure this is present for JsonIgnoreCondition
using System.Threading; // Added for CancellationToken
using System.Threading.Tasks; // Added for Task
using System.Diagnostics; // For Stopwatch
using System.Globalization; // Added for CultureInfo and DateTimeStyles
using Microsoft.Extensions.Logging; // Required for ILogger in CustomDateTimeConverter

namespace WakeAdvisor.Services
{
    // Custom DateTime converter for AIS Stream API's specific format
    public class CustomDateTimeConverter : JsonConverter<DateTime>
    {
        // Example format from logs: "2025-06-08 19:41:54.183554458 +0000 UTC"
        // Constants for formats are illustrative; the array below is used.
        // private const string DateTimeFormatSevenFractional = "yyyy-MM-dd HH:mm:ss.fffffff K";
        // private const string DateTimeFormatSixFractional = "yyyy-MM-dd HH:mm:ss.ffffff K";
        // private const string DateTimeFormatFiveFractional = "yyyy-MM-dd HH:mm:ss.fffff K";
        // private const string DateTimeFormatFourFractional = "yyyy-MM-dd HH:mm:ss.ffff K";
        // private const string DateTimeFormatThreeFractional = "yyyy-MM-dd HH:mm:ss.fff K";
        // private const string DateTimeFormatNoFractional = "yyyy-MM-dd HH:mm:ss K";

        private static ILogger? _logger;

        public static void InitializeLogger(ILogger logger)
        {
            _logger = logger;
        }

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? dateString = reader.GetString();
                if (string.IsNullOrEmpty(dateString))
                {
                    _logger?.LogWarning("DateTime string is null or empty.");
                    return default;
                }

                string stringToParse = dateString.Trim();
                // Remove " UTC" suffix if present
                if (stringToParse.EndsWith(" UTC"))
                {
                    stringToParse = stringToParse.Substring(0, stringToParse.Length - 4).TrimEnd();
                }

                // Truncate fractional seconds to 7 digits if longer
                int dotIndex = stringToParse.IndexOf('.');
                if (dotIndex > 0 && dotIndex < stringToParse.Length - 1) // Ensure there's something after '.'
                {
                    int firstCharAfterFractional = stringToParse.Length;
                    // Find the first non-digit character after the dot, or end of string
                    for (int i = dotIndex + 1; i < stringToParse.Length; i++)
                    {
                        if (!char.IsDigit(stringToParse[i]))
                        {
                            firstCharAfterFractional = i;
                            break;
                        }
                    }

                    if (firstCharAfterFractional > dotIndex + 1) // We have some fractional digits
                    {
                        string fractionalDigits = stringToParse.Substring(dotIndex + 1, firstCharAfterFractional - (dotIndex + 1));
                        if (fractionalDigits.Length > 7)
                        {
                            string truncatedFractionalDigits = fractionalDigits.Substring(0, 7);
                            stringToParse = stringToParse.Substring(0, dotIndex + 1) + 
                                              truncatedFractionalDigits + 
                                              stringToParse.Substring(firstCharAfterFractional);
                        }
                    }
                }
                
                _logger?.LogDebug("Original DateTime string: '{OriginalDateString}', Processed for parsing: '{ProcessedString}'", dateString, stringToParse);
                
                string[] formats = { 
                    "yyyy-MM-dd HH:mm:ss.fffffff K", 
                    "yyyy-MM-dd HH:mm:ss.ffffff K",
                    "yyyy-MM-dd HH:mm:ss.fffff K",
                    "yyyy-MM-dd HH:mm:ss.ffff K",
                    "yyyy-MM-dd HH:mm:ss.fff K",
                    "yyyy-MM-dd HH:mm:ss.ff K",
                    "yyyy-MM-dd HH:mm:ss.f K",
                    "yyyy-MM-dd HH:mm:ss K",
                    // Fallbacks with 'Z' instead of 'K' if the offset part is tricky
                    "yyyy-MM-dd HH:mm:ss.fffffff'Z'",
                    "yyyy-MM-dd HH:mm:ss.ffffff'Z'",
                    "yyyy-MM-dd HH:mm:ss.fffff'Z'",
                    "yyyy-MM-dd HH:mm:ss.ffff'Z'",
                    "yyyy-MM-dd HH:mm:ss.fff'Z'",
                    "yyyy-MM-dd HH:mm:ss.ff'Z'",
                    "yyyy-MM-dd HH:mm:ss.f'Z'",
                    "yyyy-MM-dd HH:mm:ss'Z'"
                };

                foreach (var format in formats)
                {
                    if (DateTime.TryParseExact(stringToParse, format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTime result))
                    {
                        _logger?.LogDebug("Successfully parsed DateTime string '{OriginalDateString}' (processed to '{ProcessedString}') using format '{Format}'", dateString, stringToParse, format);
                        return result;
                    }
                }
                
                // Try with DateTimeOffset as a fallback, it's more flexible
                if (DateTimeOffset.TryParse(stringToParse, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset dtoResult))
                {
                    _logger?.LogDebug("Successfully parsed DateTime string '{OriginalDateString}' (processed to '{ProcessedString}') using DateTimeOffset.TryParse, then converted to UTC DateTime.", dateString, stringToParse);
                    return dtoResult.UtcDateTime;
                }

                _logger?.LogError("Unable to convert \"{OriginalDateString}\" (processed to \"{ProcessedString}\") to DateTime. None of the expected formats or DateTimeOffset.TryParse worked.", dateString, stringToParse);
                throw new JsonException($"Unable to convert \"{dateString}\" (processed to \"{stringToParse}\") to DateTime. None of the expected formats matched.");
            }
            _logger?.LogWarning("Encountered a non-string token for DateTime deserialization: {TokenType}", reader.TokenType);
            // Fallback for non-string tokens, though not expected for this API's DateTime fields
            // For safety, returning default or rethrowing might be better than trying to deserialize.
            // However, if JsonSerializer.Deserialize<DateTime> is called, it might use built-in logic.
            // Given the context, this path is unlikely for the 'time_utc' field.
            return default; // Or throw new JsonException($"Expected string for DateTime, got {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Use a standard UTC format, 'O' (round-trip) is good.
            writer.WriteStringValue(value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        }
    }

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

        [JsonPropertyName("MMSI")]
        public long? Mmsi { get; set; }

        [JsonPropertyName("time_utc")]
        [JsonConverter(typeof(CustomDateTimeConverter))] // Apply the custom converter
        public DateTime TimeUtc { get; set; } // Timestamp of when the message was received by the AISStream server

        // Adding other fields observed in logs to prevent deserialization issues if they appear
        [JsonPropertyName("MMSI_String")]
        public long? MmsiString { get; set; } // Changed from string? to long?

        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }
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

        [JsonPropertyName("Timestamp")] // UTC second of the AIS message generation (0-59 typically)
        public int? Timestamp { get; set; } // Changed from DateTime to int?
    }

    // DTO for AIS Ship Static Data (e.g., from AIS Message Type 5 or 24)
    // Assuming MessageType "ShipStaticData" and fields "UserID", "ShipType", "Name"
    public class AisShipStaticDataDto
    {
        [JsonPropertyName("UserID")] // MMSI
        public int UserId { get; set; }

        [JsonPropertyName("Type")] // Corrected from "ShipType" to "Type"
        public int? ShipType { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        // Other potential static fields if needed later:
        // [JsonPropertyName("CallSign")]
        // public string? CallSign { get; set; }
        // [JsonPropertyName("Imo")]
        // public int? Imo { get; set; }
        // Dimensions, etc.
    }


    public class FreighterService
    {
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
            _configuration = configuration;
            _logger = logger;
            CustomDateTimeConverter.InitializeLogger(logger); // Initialize logger for the custom converter
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

            // Based on https://www.navcen.uscg.gov/ais-ship-types
            // and common AIS categorizations.
            if (type >= 70 && type <= 79) return "Cargo";
            if (type >= 80 && type <= 89) return "Tanker";
            
            if (type == 0) return "Not available or no ship";
            if (type >= 20 && type <= 29) return "Wing in ground (WIG)";
            if (type == 30) return "Fishing";
            if (type == 31) return "Towing";
            if (type == 32) return "Towing: length > 200m or breadth > 25m";
            if (type == 33) return "Dredging or underwater ops";
            if (type == 34) return "Diving ops";
            if (type == 35) return "Military ops";
            if (type == 36) return "Sailing";
            if (type == 37) return "Pleasure Craft";
            // 38-39 Reserved
            if (type >= 40 && type <= 49) return "High speed craft (HSC)";
            if (type == 50) return "Pilot Vessel";
            if (type == 51) return "Search and Rescue vessel";
            if (type == 52) return "Tug";
            if (type == 53) return "Port Tender";
            if (type == 54) return "Anti-pollution equipment";
            if (type == 55) return "Law Enforcement";
            // 56-57 Spare
            if (type == 58) return "Medical Transport";
            if (type == 59) return "Noncombatant ship (Resolution No. 18)";
            if (type >= 60 && type <= 69) return "Passenger";
            // 70-79 Cargo (handled above)
            // 80-89 Tanker (handled above)
            if (type >= 90 && type <= 99) return "Other Type";

            return $"Type {type}"; // Fallback for unmapped or reserved types
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
            var staticDataCache = new Dictionary<int, (int? shipType, string? shipName)>(); // Cache for static data
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            var subscriptionMessage = new AisSubscriptionMessageDto
            {
                ApiKey = _configuration["AISStreamApiKey"],
                // Wider Kingston Area bounding box
                BoundingBoxes = [[[ -75.0, 41.0], [-73.0, 43.0]]],
                FilterMessageTypes = ["PositionReport", "ShipStaticData"]
            };
            var subscriptionJson = JsonSerializer.Serialize(subscriptionMessage, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            _logger.LogInformation("AIS Stream Subscription JSON (Wider Kingston Box, PositionReport & ShipStaticData Filter): {SubscriptionJson}", subscriptionJson);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Overall timeout
            using var client = new ClientWebSocket();
            Stopwatch listenStopwatch = new Stopwatch();

            try
            {
                _logger.LogInformation("Connecting to AIS Stream API (wss://stream.aisstream.io/v0/stream)...");
                await client.ConnectAsync(new Uri("wss://stream.aisstream.io/v0/stream"), cts.Token);
                _logger.LogInformation("Connected. Waiting a moment before sending subscription...");
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token); // Small delay after connect

                _logger.LogInformation("Sending subscription message.");
                var sendBuffer = Encoding.UTF8.GetBytes(subscriptionJson);
                await client.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, cts.Token);
                _logger.LogInformation("Subscription message sent. Waiting a moment before listening...");
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token); // Slightly longer delay after subscription

                _logger.LogInformation("Starting to listen for data...");
                var receiveBuffer = new byte[8192];
                var dataCollectionEndTime = DateTime.UtcNow.AddSeconds(45); // Listen for up to 45s after delays
                listenStopwatch.Start();
                int receiveLoopIterations = 0;

                while (client.State == WebSocketState.Open && DateTime.UtcNow < dataCollectionEndTime && !cts.Token.IsCancellationRequested)
                {
                    receiveLoopIterations++;
                    _logger.LogInformation("Receive loop iteration {Iteration}, client state: {ClientState}, elapsed listen time: {ElapsedMs}ms. Attempting to receive...", receiveLoopIterations, client.State, listenStopwatch.ElapsedMilliseconds);
                    
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
                        _logger.LogWarning(oce, "Timeout (30s) receiving a single WebSocket message/chunk. Iteration: {Iteration}. Client State: {ClientState}. Elapsed: {ElapsedMs}ms. Continuing to listen if time permits.", receiveLoopIterations, client.State, listenStopwatch.ElapsedMilliseconds);
                        // Check client state here before continuing
                        if(client.State != WebSocketState.Open)
                        {
                            _logger.LogWarning("Client state is {ClientState} after receive timeout, breaking receive loop.", client.State);
                            break; // Exit while loop if state is no longer open
                        }
                        continue; 
                    }
                    
                    ms.Seek(0, SeekOrigin.Begin);
                    if (ms.Length > 0)
                    {
                        var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                        _logger.LogInformation("Received raw data (Length: {Length}): {MessageJson}", ms.Length, messageJson); 

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

                                        DateTime finalTimestamp = envelope.MetaData?.TimeUtc ?? DateTime.UtcNow; 
                                        if (positionReport.Timestamp.HasValue && positionReport.Timestamp.Value >= 0 && positionReport.Timestamp.Value <= 59)
                                        {
                                            finalTimestamp = new DateTime(finalTimestamp.Year, finalTimestamp.Month, finalTimestamp.Day, 
                                                                      finalTimestamp.Hour, finalTimestamp.Minute, positionReport.Timestamp.Value, 
                                                                      finalTimestamp.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : finalTimestamp.Kind);
                                            finalTimestamp = finalTimestamp.AddMilliseconds(envelope.MetaData?.TimeUtc.Millisecond ?? 0);
                                        }
                                        else
                                        {
                                            _logger.LogWarning("PositionReport.Timestamp ({PositionReportTimestamp}) is not a valid second (0-59). Using MetaData.TimeUtc ({MetaDataTimeUtc}) as is for MMSI: {MMSI}.", 
                                                positionReport.Timestamp, envelope.MetaData?.TimeUtc, positionReport.UserId);
                                        }

                                        // Attempt to get ShipType and Name from cache
                                        int? finalShipType = null; // Was positionReport.ShipType, which is always null
                                        string? finalShipName = envelope.MetaData?.ShipName; // Default to MetaData ShipName
                                        bool dataFromCache = false;

                                        if (staticDataCache.TryGetValue(positionReport.UserId, out var cachedData))
                                        {
                                            if (cachedData.shipType.HasValue)
                                            {
                                                finalShipType = cachedData.shipType;
                                            }
                                            if (!string.IsNullOrEmpty(cachedData.shipName))
                                            {
                                                finalShipName = cachedData.shipName; // Prefer name from static data cache
                                            }
                                            dataFromCache = true;
                                        }

                                        vessels.Add(new AISVesselData
                                        {
                                            MMSI = positionReport.UserId.ToString(),
                                            Name = finalShipName,
                                            Latitude = positionReport.Latitude,
                                            Longitude = positionReport.Longitude,
                                            SOG = sogKnots,
                                            COG = cogDegrees,
                                            Timestamp = finalTimestamp, 
                                            VesselType = MapShipTypeNumericToText(finalShipType)
                                        });
                                        _logger.LogInformation("Processed PositionReport for MMSI: {MMSI}, Name: {Name} (MetaName: {MetaName}), Lat: {Lat}, Lon: {Lon}, SOG: {Sog}kn, COG: {Cog}deg, NumericType: {ShipTypeNum} (Cached: {IsCached}), MappedType: {MappedType}, TS: {TsValue} (sec), Final TS: {FinalTs}", 
                                            positionReport.UserId, 
                                            finalShipName ?? "N/A",
                                            envelope.MetaData?.ShipName ?? "N/A",
                                            positionReport.Latitude, positionReport.Longitude, sogKnots, cogDegrees, 
                                            finalShipType.HasValue ? finalShipType.Value.ToString() : "(null)", 
                                            dataFromCache,
                                            MapShipTypeNumericToText(finalShipType),
                                            positionReport.Timestamp, finalTimestamp);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("'PositionReport' property missing in Message object for MessageType 'PositionReport'. Raw Message part: {MessageJson}", envelope.Message.GetRawText());
                                }
                            }
                            else if (envelope.MessageType == "ShipStaticData") // Handle static data messages
                            {
                                if (envelope.Message.TryGetProperty("ShipStaticData", out JsonElement staticDataElement))
                                {
                                    var staticData = JsonSerializer.Deserialize<AisShipStaticDataDto>(staticDataElement.GetRawText(), jsonOptions);
                                    if (staticData != null)
                                    {
                                        // Update cache, preferring new non-null values if an entry already exists
                                        staticDataCache.TryGetValue(staticData.UserId, out var existingData);
                                        var newShipType = staticData.ShipType ?? existingData.shipType;
                                        var newShipName = !string.IsNullOrEmpty(staticData.Name) ? staticData.Name : existingData.shipName;
                                        
                                        staticDataCache[staticData.UserId] = (newShipType, newShipName);
                                        _logger.LogInformation("Cached/Updated static data for MMSI: {MMSI}, ShipType: {ShipType}, Name: {Name}", staticData.UserId, newShipType, newShipName);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to deserialize ShipStaticData content. Raw Element: {StaticDataElement}", staticDataElement.GetRawText());
                                    }
                                }
                                else
                                {
                                     _logger.LogWarning("'ShipStaticData' property missing in Message object for MessageType 'ShipStaticData'. Raw Message part: {MessageJson}", envelope.Message.GetRawText());
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Received AIS message of type: {MessageType}. MetaData MMSI: {MetaMmsi}, MetaData MMSI_String: {MetaMmsiString}, MetaData ShipName: {MetaShipName}. Raw content: {MessageJson}", 
                                    envelope.MessageType, 
                                    envelope.MetaData?.Mmsi.ToString() ?? "N/A", 
                                    envelope.MetaData?.MmsiString.ToString() ?? "N/A", // Adjusted for long?
                                    envelope.MetaData?.ShipName ?? "N/A", 
                                    messageJson);
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
                _logger.LogInformation("Finished listening for AIS data after {ElapsedMs}ms. Loop iterations: {Iterations}. Final Client State: {ClientState}", listenStopwatch.ElapsedMilliseconds, receiveLoopIterations, client.State);
            }
            catch (OperationCanceledException ex) 
            {
                if (cts.Token.IsCancellationRequested)
                {
                    _logger.LogInformation(ex, "AIS data collection task was cancelled due to overall timeout ({TotalSeconds}s). ClientState: {ClientState}", TimeSpan.FromSeconds(60).TotalSeconds, client.State);
                }
                else
                {
                     _logger.LogWarning(ex, "AIS data collection task was cancelled (not by overall timeout, possibly by individual message timeout or other cancellation). ClientState: {ClientState}", client.State);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AIS vessel data. ClientState: {ClientState}", client.State);
            }
            finally
            {
                if (client.State == WebSocketState.Open || client.State == WebSocketState.CloseReceived)
                {
                    _logger.LogInformation("Closing AIS Stream connection. Current State: {ClientState}", client.State);
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", CancellationToken.None);
                }
                _logger.LogInformation("AIS Stream connection process finished. Collected {Count} unique vessels. Final Client State: {ClientState}", vessels.Count, client.State);
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
