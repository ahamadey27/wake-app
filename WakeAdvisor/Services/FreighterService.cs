using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.IO; // Added for StreamReader
using System.Text.Json.Serialization; // Added for JsonPropertyName

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
        public string? VesselType { get; set; }
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

    // DTO for parsing messages from AIS Stream
    public class AisStreamMessageDto
    {
        [JsonPropertyName("mmsi")]
        public int Mmsi { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("sog")] // Speed over ground in knots
        public double Sog { get; set; }

        [JsonPropertyName("cog")] // Course over ground in degrees
        public double Cog { get; set; }

        [JsonPropertyName("true_heading")] // AIS HEADING: 0-359 degrees, 511 = not available
        public int? TrueHeading { get; set; }

        [JsonPropertyName("ship_type")] // AIS numeric ship type
        public int? ShipTypeNumeric { get; set; }
        
        // In a real scenario, AIS Stream might provide a textual ship type directly,
        // or you might subscribe to a stream that enriches data this way.
        // Example: [JsonPropertyName("vessel_type_text")]
        // public string VesselTypeText { get; set; }

        [JsonPropertyName("timestamp")] // Or "time_utc", "ts", etc. - adjust to actual field name
        public DateTime Timestamp { get; set; }
    }

    public class FreighterService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        // Kingston Point reference coordinates
        private static readonly GeoPoint KingstonPoint = new GeoPoint
        {
            Latitude = 41.9275,
            Longitude = -73.9639
        };

        // Southbound bearing range (degrees True)
        private const double SouthboundMin = 160.0;
        private const double SouthboundMax = 220.0;

        public FreighterService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
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
                    !(vessel.VesselType.ToLower().Contains("freighter") || vessel.VesselType.ToLower().Contains("cargo")))
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
                    ETAAtKingston = vessel.Timestamp.AddMinutes(etaMinutes)
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
            if (type >= 70 && type <= 79) return "Cargo";       // Cargo ships
            if (type >= 80 && type <= 89) return "Tanker";      // Tankers (often broadly considered freighters)
            if (type == 30) return "Fishing";
            if (type >= 60 && type <= 69) return "Passenger";
            // Add more specific mappings as needed for your application's logic
            // For example, you might want to differentiate various cargo types if the numeric code allows.
            return $"Type {type}"; // Default for unmapped types
        }

        // Fetches AIS vessel data from the AIS Stream API
        private async Task<List<AISVesselData>> GetAISVesselsAsync(DateTime selectedDate)
        {
            var apiKey = _configuration["AISStreamApiKey"];
            // Bounding box for Kingston, NY area (adjust as needed)
            // Format: BBOX = minLongitude,minLatitude,maxLongitude,maxLatitude
            var boundingBox = "-74.05,41.90,-73.85,42.00"; // Example, adjust as needed
            var requestUrl = $"https://stream.aisstream.io/v0/stream?bbox={boundingBox}";

            // AISStream might require API key in URL or as a Bearer token.
            // This implementation uses Bearer token as per previous setup.
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            // If API key is needed in query: requestUrl += $"&apikey={apiKey}";

            var vessels = new List<AISVesselData>();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            try
            {
                // HttpCompletionOption.ResponseHeadersRead is important for processing streams
                // as it allows you to start processing the content as it arrives.
                using var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode(); // Throw on error code.

                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(responseStream);

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        // Assuming each line is a self-contained JSON object from AIS Stream.
                        // The AisStreamMessageDto needs to accurately reflect the structure of these objects.
                        var aisMessage = JsonSerializer.Deserialize<AisStreamMessageDto>(line, jsonOptions);

                        if (aisMessage != null)
                        {
                            // Map DTO to AISVesselData
                            vessels.Add(new AISVesselData
                            {
                                MMSI = aisMessage.Mmsi.ToString(),
                                Name = aisMessage.Name, // Can be null if not provided in the message (CS8601 addressed by making AISVesselData.Name nullable)
                                // Use mapped ship type. The filter in GetSouthboundFreighterInfoAsync expects "freighter" or "cargo".
                                VesselType = MapShipTypeNumericToText(aisMessage.ShipTypeNumeric),
                                Latitude = aisMessage.Latitude,
                                Longitude = aisMessage.Longitude,
                                SOG = aisMessage.Sog,
                                // Prefer TrueHeading if available and valid, otherwise use COG.
                                // AIS TrueHeading 511 means "not available".
                                COG = (aisMessage.TrueHeading.HasValue && aisMessage.TrueHeading.Value != 511)
                                      ? aisMessage.TrueHeading.Value
                                      : aisMessage.Cog,
                                Timestamp = aisMessage.Timestamp // Ensure this is correctly parsed (e.g., as UTC)
                            });
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        // Log error for a single malformed line, but attempt to continue with the stream.
                        // In a production app, use ILogger.
                        Console.WriteLine($"Error parsing AIS data line: {jsonEx.Message}. Line: '{line}'");
                    }
                }
            }
            catch (HttpRequestException e)
            {
                // Log request error (e.g., using ILogger)
                Console.WriteLine($"AIS API Request error: {e.Message}");
                // Depending on requirements, you might rethrow, or return empty/partial list.
            }
            // Note: A true real-time stream from AISStream might be continuous.
            // This implementation assumes the stream will eventually end (e.g., server closes it after sending a burst of data,
            // or connection is dropped). For a web request context, this is a common pattern.
            // If it's a long-lived persistent stream, a different architecture (e.g., background service) would be needed.
            return vessels;
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
