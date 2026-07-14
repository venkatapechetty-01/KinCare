using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Services;
using KinCare.API.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Jobs;

public class ExternalTripSyncJob
{
    private readonly AppDbContext _db;
    private readonly RideService _rideService;
    private readonly BrokerDispatchService _broker;
    private readonly RideStateMachine _stateMachine;
    private readonly ILogger<ExternalTripSyncJob> _logger;

    public ExternalTripSyncJob(
        AppDbContext db,
        RideService rideService,
        BrokerDispatchService broker,
        RideStateMachine stateMachine,
        ILogger<ExternalTripSyncJob> logger)
    {
        _db = db;
        _rideService = rideService;
        _broker = broker;
        _stateMachine = stateMachine;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        // Only Broker rides use external trip IDs. Webhooks are the primary mechanism;
        // this job is a fallback for missed webhook events.
        var activeExternalRides = await _db.Rides
            .Where(r => r.ExternalTripId != null
                && r.Status != RideStatus.Completed
                && r.Status != RideStatus.Cancelled
                && r.DispatchChannel == DispatchChannel.Broker)
            .ToListAsync();

        foreach (var ride in activeExternalRides)
        {
            try
            {
                var brokerStatus = await _broker.GetTripStatusAsync(ride.ExternalTripId!);
                if (brokerStatus is null)
                {
                    _logger.LogDebug("Broker returned no status for ride {RideId}, trip {TripId}",
                        ride.Id, ride.ExternalTripId);
                    continue;
                }

                var newStatus = BrokerDispatchService.MapBrokerStatus(brokerStatus);
                if (newStatus is null || newStatus == ride.Status)
                    continue;

                if (!_stateMachine.CanTransition(ride.Status, newStatus.Value, ride.DispatchChannel))
                {
                    _logger.LogWarning("Broker status {BrokerStatus} maps to {NewStatus} but transition from {Current} is invalid for ride {RideId}",
                        brokerStatus, newStatus, ride.Status, ride.Id);
                    continue;
                }

                await _rideService.AdvanceStatusAsync(ride.Id, newStatus.Value, "broker_sync_job");
                _logger.LogInformation("Synced ride {RideId} to {Status} via Broker poll", ride.Id, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Broker status for ride {RideId}", ride.Id);
            }
        }
    }
}
