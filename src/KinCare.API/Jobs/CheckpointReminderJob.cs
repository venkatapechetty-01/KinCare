using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Jobs;

public class CheckpointReminderJob
{
    private readonly AppDbContext _db;
    private readonly TwilioDispatchService _twilioService;
    private readonly ILogger<CheckpointReminderJob> _logger;

    public CheckpointReminderJob(
        AppDbContext db,
        TwilioDispatchService twilioService,
        ILogger<CheckpointReminderJob> logger)
    {
        _db = db;
        _twilioService = twilioService;
        _logger = logger;
    }

    public async Task SendReminderAsync(Guid rideId, string checkpoint)
    {
        var ride = await _db.Rides
            .Include(r => r.Vendor)
            .FirstOrDefaultAsync(r => r.Id == rideId);

        if (ride?.Vendor is null) return;

        if (ride.Status == RideStatus.Completed || ride.Status == RideStatus.Cancelled)
            return;

        await _twilioService.SendCheckpointSmsAsync(ride, ride.Vendor, checkpoint);
        _logger.LogInformation("Checkpoint reminder sent for ride {RideId}: {Checkpoint}", rideId, checkpoint);
    }
}
