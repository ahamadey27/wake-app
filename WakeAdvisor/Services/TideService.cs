using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WakeAdvisor.Services
{
    // Service responsible for retrieving and processing tide data from NOAA
    public class TideService
    {
        private readonly HttpClient _httpClient;
        private const string StationId = "8519482"; // Kingston Point, Hudson River, NY

        public TideService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Retrieves low tide windows for the specified date
        public async Task<List<LowTideWindow>> GetLowTideWindowsAsync(DateTime date)
        {
            // Build the NOAA API request URL for the selected date and station
            string apiUrl = $"https://api.tidesandcurrents.noaa.gov/api/prod/datagetter?product=predictions&application=WakeAdvisor&begin_date={date:yyyyMMdd}&end_date={date:yyyyMMdd}&datum=MLLW&station={StationId}&time_zone=lst_ldt&units=english&interval=h&format=json";

            try
            {
                // Make the HTTP GET request to the NOAA API
                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                // Parse the JSON response into C# objects
                var json = await response.Content.ReadAsStringAsync();
                var tideData = JsonSerializer.Deserialize<TidePredictionData>(json);

                // Filter for times when tide height is ≤ 2 feet
                var lowTideWindows = new List<LowTideWindow>();
                if (tideData?.Predictions == null)
                {
                    // Return an empty list or handle the error as appropriate
                    return new List<LowTideWindow>();
                }
                foreach (var prediction in tideData.Predictions)
                {
                    if (!string.IsNullOrEmpty(prediction?.Value) && double.TryParse(prediction.Value, out double height) && height <= 2.0)
                    {
                        // For today's date, filter out times that have already passed
                        if (date.Date == DateTime.Now.Date)
                        {
                            if (DateTime.TryParse(prediction.Time, out DateTime predictionTime) && predictionTime > DateTime.Now)
                            {
                                lowTideWindows.Add(new LowTideWindow
                                {
                                    Time = prediction.Time,
                                    Height = height
                                });
                            }
                        }
                        else
                        {
                            // For future dates, add all low tide windows
                            lowTideWindows.Add(new LowTideWindow
                            {
                                Time = prediction.Time,
                                Height = height
                            });
                        }
                    }
                }
                return lowTideWindows;
            }
            catch (HttpRequestException ex)
            {
                // If the API returns a 400 Bad Request or other error, handle it gracefully
                // Log the error if needed and return an empty list
                return new List<LowTideWindow>();
            }
        }
    }

    // Model for the NOAA API response
    public class TidePredictionData
    {
        [JsonPropertyName("predictions")]
        public List<TidePrediction>? Predictions { get; set; }
    }

    // Model for each tide prediction entry
    public class TidePrediction
    {
        public string? Time { get; set; }
        public string? Value { get; set; }
    }

    // Model for a filtered low tide window
    public class LowTideWindow
    {
        public string? Time { get; set; }
        public double Height { get; set; }
    }
}