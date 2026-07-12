using System.Net.Http.Json;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.Extensions.Options;

namespace KinCare.API.Services.Dispatch;

public class BrokerDispatchService
{
    private readonly BrokerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrokerDispatchService> _logger;

    public BrokerDispatchService(
        IOptions<BrokerConfig> config,
        IHttpClientFactory httpClientFactory,
        ILogger<BrokerDispatchService> logger)
    {
        _config = config.Value;
        _httpClient = httpClientFactory.CreateClient("Broker");
        _logger = logger;
    }

    public async Task<string?> BookRideAsync(Ride ride, Resident resident)
    {
        try
        {
            var payload = new
            {
                pickup_address = ride.PickupAddress,
                dropoff_address = ride.DestinationAddress,
                pickup_time = ride.PickupTime.ToString("O"),
                patient_first_name = resident.FirstName,
                patient_last_name = resident.LastName,
                needs_wheelchair = resident.NeedsWheelchair,
                needs_oxygen = resident.NeedsOxygen,
                needs_stretcher = resident.NeedsStretcher
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.BaseUrl}/api/trips");
            request.Headers.Add("X-Api-Key", _config.ApiKey);
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Broker booking failed: {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<BrokerTripResponse>();
            return result?.TripId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Broker API call failed for ride {RideId}", ride.Id);
            return null;
        }
    }

    public async Task<string?> GetTripStatusAsync(string externalTripId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/api/trips/{externalTripId}");
            request.Headers.Add("X-Api-Key", _config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Broker status poll returned {Status} for trip {TripId}", response.StatusCode, externalTripId);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<BrokerTripStatusResponse>();
            return result?.Status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Broker status poll failed for trip {TripId}", externalTripId);
            return null;
        }
    }

    public static RideStatus? MapBrokerStatus(string brokerStatus)
    {
        return brokerStatus.ToLowerInvariant() switch
        {
            "assigned" => RideStatus.Confirmed,
            "en_route" => RideStatus.EnRoute,
            "at_pickup" => RideStatus.Arrived,
            "completed" => RideStatus.Dropped,
            "cancelled" => RideStatus.Cancelled,
            _ => null
        };
    }

    private record BrokerTripResponse(string TripId);
    private record BrokerTripStatusResponse(string Status);
}
