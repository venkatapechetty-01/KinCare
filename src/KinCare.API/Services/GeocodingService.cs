using System.Text.Json.Serialization;
using KinCare.API.Infrastructure;
using Microsoft.Extensions.Options;

namespace KinCare.API.Services;

public record AddressSuggestion(string DisplayName, double Lat, double Lon);

public class GeocodingService
{
    private readonly LocationIqConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeocodingService> _logger;

    public GeocodingService(
        IOptions<LocationIqConfig> config,
        IHttpClientFactory httpClientFactory,
        ILogger<GeocodingService> logger)
    {
        _config = config.Value;
        _httpClient = httpClientFactory.CreateClient("LocationIq");
        _logger = logger;
    }

    public async Task<List<AddressSuggestion>> AutocompleteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _logger.LogWarning("LocationIq ApiKey not configured — skipping address autocomplete");
            return [];
        }

        try
        {
            var url = "https://api.locationiq.com/v1/autocomplete" +
                      $"?key={Uri.EscapeDataString(_config.ApiKey)}" +
                      $"&q={Uri.EscapeDataString(query)}" +
                      "&limit=5&dedupe=1";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LocationIq autocomplete failed: {Status}", response.StatusCode);
                return [];
            }

            var results = await response.Content.ReadFromJsonAsync<List<LocationIqResult>>();
            return results?
                .Select(r => new AddressSuggestion(r.DisplayName, double.Parse(r.Lat), double.Parse(r.Lon)))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocationIq autocomplete request failed");
            return [];
        }
    }

    private class LocationIqResult
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("lat")]
        public string Lat { get; set; } = "0";

        [JsonPropertyName("lon")]
        public string Lon { get; set; } = "0";
    }
}
