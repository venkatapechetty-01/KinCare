using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Jobs;

public class EscalationJob
{
    private readonly AppDbContext _db;
    private readonly IHubContext<RideStatusHub> _hubContext;
    private readonly FcmService _fcm;
    private readonly ILogger<EscalationJob> _logger;

    public EscalationJob(AppDbContext db, IHubContext<RideStatusHub> hubContext, FcmService fcm, ILogger<EscalationJob> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;

        var smsRides = await _db.Rides
            .Where(r => (r.DispatchChannel == DispatchChannel.SmsNemt || r.DispatchChannel == DispatchChannel.SmsTaxi)
                && r.Status != RideStatus.Completed
                && r.Status != RideStatus.Cancelled)
            .Include(r => r.Events)
            .ToListAsync();

        foreach (var ride in smsRides)
        {
            var escalationType = GetEscalationType(ride, now);
            if (escalationType is null) continue;

            var alreadyEscalated = ride.Events.Any(e =>
                e.TriggeredBy == "escalation_job" && e.Notes == escalationType);
            if (alreadyEscalated) continue;

            var rideEvent = new RideEvent
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                FromStatus = ride.Status,
                ToStatus = ride.Status,
                TriggeredBy = "escalation_job",
                Notes = escalationType
            };
            _db.RideEvents.Add(rideEvent);

            await _hubContext.Clients.Group($"facility:{ride.FacilityId}")
                .SendAsync("EscalationAlert", new
                {
                    ride.Id,
                    ride.ResidentId,
                    ride.VendorId,
                    Message = escalationType
                });

            try { await _fcm.SendToFacilityUsersAsync(ride.FacilityId, "⚠️ Ride escalation", escalationType); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send FCM escalation push for ride {RideId}", ride.Id); }

            _logger.LogInformation("Escalation fired for ride {RideId}: {Type}", ride.Id, escalationType);
        }

        await _db.SaveChangesAsync();
    }

    private static string? GetEscalationType(Ride ride, DateTime now)
    {
        return ride.Status switch
        {
            RideStatus.Dispatched when ride.PickupTime.AddMinutes(30) < now
                => "No confirmation — 30min past pickup time",

            RideStatus.Confirmed when ride.PickupTime.AddMinutes(-15) < now
                => "Hasn't departed — 15min before pickup",

            RideStatus.EnRoute when ride.PickupTime.AddMinutes(45) < now
                => "May be delayed — 45min past pickup time",

            RideStatus.Arrived when ride.Events
                .Where(e => e.ToStatus == RideStatus.Arrived)
                .Select(e => e.OccurredAt)
                .DefaultIfEmpty(now)
                .Max()
                .AddMinutes(20) < now
                => "May need boarding help — arrived 20min ago",

            _ => null
        };
    }
}
