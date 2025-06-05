using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        // Kingston Point reference coordinates
        private static readonly GeoPoint KingstonPoint = new GeoPoint
        {
            Latitude = 41.9275,
            Longitude = -73.9639
        };

        // Southbound bearing range (degrees True)
        private const double SouthboundMin = 160.0;
        private const double SouthboundMax = 220.0;

        // Main entry point: gets southbound freighters and their ETAs
        public async Task<List<FreighterInfo>> GetSouthboundFreighterInfoAsync(DateTime selectedDate)
        {
            // TODO: Call AIS API and retrieve vessel data for the Kingston area
            List<AISVesselData> vessels = await GetAISVesselsAsync(selectedDate);

            // TODO: Filter vessels by type (freighter/cargo)
            // TODO: Filter vessels north of Kingston and approaching
            // TODO: Filter by COG within southbound range
            // TODO: Filter out vessels already past Kingston

            // TODO: For each qualifying vessel, calculate distance to Kingston and ETA
            // Use Haversine formula for distance, SOG for ETA

            // TODO: Return list of FreighterInfo objects
            return new List<FreighterInfo>();
        }

        // Stub for AIS API call (to be implemented)
        private Task<List<AISVesselData>> GetAISVesselsAsync(DateTime date)
        {
            // TODO: Implement actual API call and parsing
            return Task.FromResult(new List<AISVesselData>());
        }

        // Utility: Calculate distance (nautical miles) between two points (Haversine formula)
        public static double CalculateDistanceNM(GeoPoint a, GeoPoint b)
        {
            // TODO: Implement Haversine formula
            return 0.0;
        }
    }
}
