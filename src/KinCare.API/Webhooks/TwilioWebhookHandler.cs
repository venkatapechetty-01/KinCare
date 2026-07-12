using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;
using Twilio.Security;

namespace KinCare.API.Webhooks;

public static class TwilioWebhookHandler
{
    // Replies after Accept: vendor can advance their own ride status
    private static readonly Dictionary<int, RideStatus> PostAcceptReplyMap = new()
    {
        { 3, RideStatus.EnRoute       },  // On my way to facility
        { 4, RideStatus.Arrived       },  // Reached facility
        { 5, RideStatus.PickedUp      },  // Resident picked up
        { 6, RideStatus.AtDestination },  // At destination
        { 7, RideStatus.Dropped       },  // Resident dropped off
        { 8, RideStatus.Completed     },  // Trip complete
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
                    && o.Status.StartsWith("Pending")
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
                .Include(o => o.Ride)
                .Where(o => o.VendorId == vendor.Id
                    && o.Status.StartsWith("Pending")
                    && o.Ride.Status == RideStatus.Dispatched)
                .OrderByDescending(o => o.SentAt)
                .FirstOrDefaultAsync();

            if (offer is not null)
            {
                offer.Status = "Declined";
                offer.RespondedAt = DateTime.UtcNow;

                db.RideEvents.Add(new RideEvent
                {
                    Id = Guid.NewGuid(),
                    RideId = offer.RideId,
                    FromStatus = offer.Ride.Status,
                    ToStatus = offer.Ride.Status,
                    TriggeredBy = "vendor_sms",
                    Notes = $"Vendor {vendor.Name} declined. twilio_sid:{messageSid}"
                });
                await db.SaveChangesAsync();

                // Check if ALL vendors declined — if so mark Cancelled
                var anyPending = await db.RideDispatchOffers
                    .AnyAsync(o => o.RideId == offer.RideId && o.Status.StartsWith("Pending"));
                if (!anyPending)
                {
                    await rideService.AdvanceStatusAsync(
                        offer.RideId, RideStatus.Cancelled, "vendor_sms",
                        $"All vendors declined. twilio_sid:{messageSid}");
                }
            }
            return Results.Ok();
        }

        // ── Replies 3-5: Post-accept status updates ───────────────────────────
        if (PostAcceptReplyMap.TryGetValue(replyDigit, out var newStatus))
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

            var stateMachine = new RideStateMachine();
            if (!stateMachine.CanTransition(ride.Status, newStatus))
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
            }
        }

        return Results.Ok();
    }
}
