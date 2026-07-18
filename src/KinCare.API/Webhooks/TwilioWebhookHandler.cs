using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;
using Twilio.Security;

namespace KinCare.API.Webhooks;

public static class TwilioWebhookHandler
{
    // Replies after Accept: vendor can advance their own ride status. Keyed on the
    // ride's CURRENT status first, since digits are reused across the outbound and
    // NEMT return legs (e.g. "3" means "on my way" on both legs) rather than having
    // a fixed global meaning — a phone keypad only has 9 usable digits.
    private static readonly Dictionary<RideStatus, Dictionary<int, RideStatus>> PostAcceptReplyMap = new()
    {
        { RideStatus.Confirmed,      new() { { 3, RideStatus.EnRoute } } },        // On my way to facility
        { RideStatus.EnRoute,        new() { { 4, RideStatus.Arrived } } },        // Reached facility
        { RideStatus.Arrived,        new() { { 5, RideStatus.PickedUp } } },       // Resident picked up
        { RideStatus.PickedUp,       new() { { 6, RideStatus.AtDestination } } },  // At destination
        { RideStatus.AtDestination,  new() { { 7, RideStatus.Dropped } } },        // Resident dropped off
        { RideStatus.Dropped,        new() { { 8, RideStatus.Completed } } },      // Trip complete (one-way)
        { RideStatus.AwaitingReturn, new() { { 3, RideStatus.ReturnEnRoute } } },  // On my way back
        { RideStatus.ReturnEnRoute,  new() { { 5, RideStatus.ReturnPickedUp } } }, // Resident picked up for return
        { RideStatus.ReturnPickedUp, new() { { 8, RideStatus.Completed } } },      // Trip complete (round trip)
    };

    public static void MapTwilioWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhook/twilio", HandleInbound)
            .WithTags("Webhooks")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleInbound(
        HttpContext httpContext,
        AppDbContext db,
        RideService rideService,
        FcmService fcm,
        IConfiguration config,
        ILogger<Program> logger)
    {
        var form = await httpContext.Request.ReadFormAsync();
        var authToken = config["Twilio:AuthToken"] ?? "";

        // Skip signature validation when token is empty (dev/test mode)
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            var requestValidator = new RequestValidator(authToken);
            var requestUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.Path}";
            var parameters = form.ToDictionary(x => x.Key, x => x.Value.ToString());
            var signature = httpContext.Request.Headers["X-Twilio-Signature"].FirstOrDefault() ?? "";

            if (!requestValidator.Validate(requestUrl, parameters, signature))
            {
                logger.LogWarning("Invalid Twilio signature from {IP}", httpContext.Connection.RemoteIpAddress);
                return Results.StatusCode(403);
            }
        }

        var messageSid = form["MessageSid"].ToString();
        var fromNumber = form["From"].ToString();
        var body = form["Body"].ToString().Trim();

        // Idempotency
        var alreadyProcessed = await db.RideEvents
            .AnyAsync(e => e.Notes != null && e.Notes.Contains($"twilio_sid:{messageSid}"));
        if (alreadyProcessed)
            return Results.Ok();

        var vendor = await db.Vendors
            .FirstOrDefaultAsync(v => v.PhoneNumber == fromNumber && v.IsActive);
        if (vendor is null)
        {
            logger.LogWarning("Twilio webhook from unknown vendor phone: {Phone}", fromNumber);
            return Results.Ok();
        }

        if (!int.TryParse(body.Length > 0 ? body[..1] : "", out var replyDigit))
            return Results.Ok();

        // ── Reply 1: Accept / Claim ride ─────────────────────────────────────
        if (replyDigit == 1)
        {
            // Find the pending offer for this vendor
            var offer = await db.RideDispatchOffers
                .Include(o => o.Ride)
                .Where(o => o.VendorId == vendor.Id
                    && o.Status == "Pending"
                    && o.Ride.Status == RideStatus.Dispatched)
                .OrderByDescending(o => o.SentAt)
                .FirstOrDefaultAsync();

            if (offer is null)
            {
                logger.LogWarning("No pending offer for vendor {VendorId} to claim", vendor.Id);
                return Results.Ok();
            }

            var claimed = await rideService.ClaimRideAsync(offer.RideId, vendor.Id, messageSid);
            if (!claimed)
                logger.LogWarning("ClaimRide failed for ride {RideId} vendor {VendorId} — already claimed", offer.RideId, vendor.Id);

            return Results.Ok();
        }

        // ── Reply 2: Decline ──────────────────────────────────────────────────
        if (replyDigit == 2)
        {
            var offer = await db.RideDispatchOffers
                .Where(o => o.VendorId == vendor.Id
                    && o.Status == "Pending"
                    && o.Ride.Status == RideStatus.Dispatched)
                .OrderByDescending(o => o.SentAt)
                .FirstOrDefaultAsync();

            if (offer is not null)
                await rideService.DeclineOfferAsync(offer.RideId, vendor.Id, "vendor_sms", $"twilio_sid:{messageSid}");

            return Results.Ok();
        }

        // ── Replies 3-8: Post-accept status updates ───────────────────────────
        // The ride must be loaded first since the digit's meaning now depends on
        // the ride's current status (see PostAcceptReplyMap).
        if (replyDigit is >= 3 and <= 8)
        {
            var ride = await db.Rides
                .Where(r => r.VendorId == vendor.Id
                    && r.Status != RideStatus.Completed
                    && r.Status != RideStatus.Cancelled
                    && (r.DispatchChannel == DispatchChannel.SmsNemt || r.DispatchChannel == DispatchChannel.SmsTaxi))
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (ride is null)
            {
                logger.LogWarning("No active assigned ride for vendor {VendorId}", vendor.Id);
                return Results.Ok();
            }

            if (!PostAcceptReplyMap.TryGetValue(ride.Status, out var digitMap) ||
                !digitMap.TryGetValue(replyDigit, out var newStatus))
            {
                logger.LogWarning("Reply digit {Digit} has no meaning for ride {RideId} in status {Status}",
                    replyDigit, ride.Id, ride.Status);
                return Results.Ok();
            }

            var stateMachine = new RideStateMachine();
            if (!stateMachine.CanTransition(ride.Status, newStatus, ride.DispatchChannel))
            {
                logger.LogWarning("Invalid transition {From} → {To} for ride {RideId}", ride.Status, newStatus, ride.Id);
                return Results.Ok();
            }

            await rideService.AdvanceStatusAsync(ride.Id, newStatus, "vendor_sms", $"twilio_sid:{messageSid}");
            return Results.Ok();
        }

        // ── Reply 9: Issue reported ───────────────────────────────────────────
        if (replyDigit == 9)
        {
            var ride = await db.Rides
                .Where(r => r.VendorId == vendor.Id
                    && r.Status != RideStatus.Completed
                    && r.Status != RideStatus.Cancelled)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (ride is not null)
            {
                db.RideEvents.Add(new RideEvent
                {
                    Id = Guid.NewGuid(),
                    RideId = ride.Id,
                    FromStatus = ride.Status,
                    ToStatus = ride.Status,
                    TriggeredBy = "vendor_sms",
                    Notes = $"Issue reported by vendor. twilio_sid:{messageSid}"
                });
                await db.SaveChangesAsync();

                var residentName = ride.ResidentId.HasValue
                    ? await db.Residents.Where(r => r.Id == ride.ResidentId.Value)
                        .Select(r => r.FirstName + " " + r.LastName).FirstOrDefaultAsync()
                    : null;
                try
                {
                    await fcm.SendToFacilityUsersAsync(ride.FacilityId, "🚨 Issue reported",
                        $"{vendor.Name} reported an issue for {residentName ?? "resident"}");
                }
                catch (Exception ex) { logger.LogError(ex, "Failed to send FCM issue-report push for ride {RideId}", ride.Id); }
            }
        }

        return Results.Ok();
    }
}
