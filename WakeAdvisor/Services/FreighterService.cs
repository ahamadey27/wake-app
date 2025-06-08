using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

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
        public string MMSI { get; set; } // Maritime Mobile Service Identity
        public string Name { get; set; }
        public string VesselType { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SOG { get; set; } // Speed Over Ground (knots)
        public double COG { get; set; } // Course Over Ground (degrees)
        public DateTime Timestamp { get; set; }
    }

    // Represents a filtered southbound freighter and its ETA
    public class FreighterInfo
    {
        public string Name { get; set; }
        public string MMSI { get; set; }
        public double CurrentSOG { get; set; }
        public DateTime ETAAtKingston { get; set; }
        public double DistanceToKingstonNM { get; set; } // Nautical miles
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

            return new List<FreighterInfo>();
        }

        // Fetches AIS vessel data from the API
        private async Task<List<AISVesselData>> GetAISVesselsAsync(DateTime selectedDate)
        {
            var apiKey = _configuration["AISStreamApiKey"];
            // AISStream API endpoint for real-time data around a bounding box
            // Bounding box for Kingston, NY area (adjust as needed)
            // Format: BBOX = minLongitude,minLatitude,maxLongitude,maxLatitude
            var boundingBox = "-74.05,41.90,-73.85,42.00"; // Example, adjust as needed
            var requestUrl = $"https://stream.aisstream.io/v0/stream?bbox={boundingBox}";

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            
            try
            {
                var response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode(); // Throw on error code.

                var jsonResponse = await response.Content.ReadAsStringAsync();
                // Note: AISStream provides data as a stream of JSON objects, not a single array.
                // You'll need to handle this streaming nature. For simplicity, this example
                // assumes a way to get a list of vessels, which might require a different endpoint
                // or a library that handles the stream.
                // This is a placeholder for actual stream parsing logic.
                // You might need to use a library like System.Reactive or manually process the stream.

                // Placeholder: Deserialize the JSON. This will need to be adapted based on the actual
                // structure of the AISStream response and how you handle the stream.
                // If the API returns a stream of individual JSON objects, you'll need to parse them one by one.
                // For this example, let's assume a simplified scenario where you get a list.
                // You will likely need to adjust the AISVesselData class to match the API's fields.
                var vessels = JsonSerializer.Deserialize<List<AISVesselData>>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return vessels ?? new List<AISVesselData>();
            }
            catch (HttpRequestException e)
            {
                // Log error (e.g., using ILogger)
                Console.WriteLine($"Request error: {e.Message}");
                return new List<AISVesselData>(); // Return empty list on error
            }
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
