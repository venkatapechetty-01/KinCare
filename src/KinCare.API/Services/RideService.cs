using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Infrastructure;
using KinCare.API.Services.Dispatch;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KinCare.API.Services;

public class RideService
{
    private readonly AppDbContext _db;
    private readonly RideStateMachine _stateMachine;
    private readonly DispatchRouter _dispatchRouter;
    private readonly IHubContext<RideStatusHub> _hubContext;
    private readonly TwilioDispatchService _twilioDispatch;
    private readonly FcmService _fcm;
    private readonly AppConfig _appConfig;
    private readonly ILogger<RideService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RideService(
        AppDbContext db,
        RideStateMachine stateMachine,
        DispatchRouter dispatchRouter,
        IHubContext<RideStatusHub> hubContext,
        TwilioDispatchService twilioDispatch,
        FcmService fcm,
        IOptions<AppConfig> appConfig,
        ILogger<RideService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _stateMachine = stateMachine;
        _dispatchRouter = dispatchRouter;
        _hubContext = hubContext;
        _twilioDispatch = twilioDispatch;
        _fcm = fcm;
        _appConfig = appConfig.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<Ride> BookRideAsync(
        Guid facilityId,
        Guid organizationId,
        Guid? residentId,
        DateTime pickupTime,
        string pickupAddress,
        string destinationAddress,
        DispatchChannel? preferredChannel = null)
    {
        _logger.LogInformation(
            "BookRide started: FacilityId={FacilityId} OrgId={OrgId} ResidentId={ResidentId} PickupTime={PickupTime}",
            facilityId, organizationId, residentId, pickupTime);

        Resident? resident = null;
        if (residentId.HasValue)
        {
            resident = await _db.Residents.FirstOrDefaultAsync(r => r.Id == residentId.Value && r.IsActive);
            if (resident is null)
            {
                _logger.LogWarning("BookRide failed: resident {ResidentId} not found or inactive", residentId);
                throw new ArgumentException("Resident not found or inactive.");
            }
            var residentFacility = await _db.Facilities.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == resident.FacilityId);
            if (residentFacility is null || residentFacility.OrganizationId != organizationId)
            {
                _logger.LogWarning(
                    "BookRide failed: resident {ResidentId} belongs to org {ResidentOrgId}, caller org {OrgId}",
                    residentId, residentFacility?.OrganizationId, organizationId);
                throw new UnauthorizedAccessException("Resident does not belong to your organization.");
            }
            facilityId = resident.FacilityId;
        }

        var org = await _db.Organizations.FirstAsync(o => o.Id == organizationId);
        var facility = await _db.Facilities.FirstAsync(f => f.Id == facilityId);
        if (facility.OrganizationId != organizationId)
        {
            _logger.LogWarning(
                "BookRide failed: facility {FacilityId} belongs to org {FacilityOrgId}, caller org {OrgId}",
                facilityId, facility.OrganizationId, organizationId);
            throw new UnauthorizedAccessException("Facility does not belong to your organization.");
        }

        var (channel, vendors) = await _dispatchRouter.RouteAsync(resident, org, facility, preferredChannel);
        _logger.LogInformation(
            "BookRide routed: RideChannel={Channel} VendorCount={VendorCount} FacilityId={FacilityId}",
            channel, vendors.Count, facilityId);

        // Broadcast: VendorId stays null until a vendor accepts (Reply 1).
        // Single vendor list means no contest — assign immediately.
        Guid? assignedVendorId = null;
        string? trackingToken = null;

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = organizationId,
            ResidentId = residentId,
            VendorId = assignedVendorId,
            Status = RideStatus.Dispatched,
            DispatchChannel = channel,
            PickupTime = pickupTime,
            PickupAddress = pickupAddress,
            DestinationAddress = destinationAddress,
            TrackingToken = trackingToken
        };

        _db.Rides.Add(ride);

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Dispatched,
            TriggeredBy = "system",
            Notes = vendors.Count > 0
                ? $"Ride created — broadcast to {vendors.Count} vendor(s)"
                : "Ride created — no vendors available"
        });

        // Create offer records for every vendor being contacted
        foreach (var v in vendors)
        {
            _db.RideDispatchOffers.Add(new RideDispatchOffer
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                VendorId = v.Id,
                Status = "Pending"
            });
        }

        await _db.SaveChangesAsync();

        // Broadcast SMS to all vendors (fire-and-forget, one SMS per vendor)
        if (vendors.Count > 0 && resident is not null
            && (channel == DispatchChannel.SmsNemt || channel == DispatchChannel.SmsTaxi))
        {
            var rideId = ride.Id;
            _ = Task.Run(async () =>
            {
                // This runs after the HTTP response completes, so the request-scoped
                // AppDbContext (_db) is already disposed — resolve a fresh one from a
                // new DI scope for any database access in here.
                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                foreach (var v in vendors)
                {
                    // Every vendor — Basic or Smart tier — gets their own unique tracking
                    // token and link, usable both to accept the ride and (once accepted) to
                    // check in through trip completion, in addition to numbered SMS replies.
                    var token = Guid.NewGuid().ToString("N");
                    var vendorTrackingUrl = $"{_appConfig.BaseUrl}/track/{token}";
                    try
                    {
                        var offer = await scopedDb.RideDispatchOffers
                            .FirstOrDefaultAsync(o => o.RideId == rideId && o.VendorId == v.Id);
                        if (offer is not null)
                        {
                            offer.TrackingToken = token;
                            await scopedDb.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to store tracking token for offer"); }

                    try { await _twilioDispatch.SendBookingSmsAsync(ride, v, resident, vendorTrackingUrl); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send booking SMS to vendor {VendorId}", v.Id); }
                }
            });
        }

        await _hubContext.Clients.Group($"facility:{facilityId}")
            .SendAsync("RideCreated", new
            {
                ride.Id,
                ride.Status,
                ride.DispatchChannel,
                VendorCount = vendors.Count
            });

        _logger.LogInformation(
            "BookRide complete: RideId={RideId} Channel={Channel} Status={Status} VendorCount={VendorCount}",
            ride.Id, ride.DispatchChannel, ride.Status, vendors.Count);

        return ride;
    }

    // Called by Twilio webhook when a vendor replies "1" (Accept).
    // Claims the ride for that vendor, marks all other offers as Superseded, SMSes them.
    public async Task<bool> ClaimRideAsync(Guid rideId, Guid vendorId, string messageSid)
    {
        _logger.LogInformation(
            "ClaimRide attempt: RideId={RideId} VendorId={VendorId} MessageSid={MessageSid}",
            rideId, vendorId, messageSid);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == rideId);
            if (ride is null || ride.Status != RideStatus.Dispatched)
            {
                _logger.LogWarning(
                    "ClaimRide rejected: RideId={RideId} Status={Status} (already claimed or not found)",
                    rideId, ride?.Status.ToString() ?? "not found");
                await tx.RollbackAsync();
                return false;
            }

            var offer = await _db.RideDispatchOffers
                .FirstOrDefaultAsync(o => o.RideId == rideId && o.VendorId == vendorId && o.Status == "Pending");
            if (offer is null)
            {
                _logger.LogWarning(
                    "ClaimRide rejected: no pending offer for RideId={RideId} VendorId={VendorId}",
                    rideId, vendorId);
                await tx.RollbackAsync();
                return false;
            }

            // Assign vendor to ride and advance status
            ride.VendorId = vendorId;

            // Activate the offer's tracking token onto the ride — the same URL the vendor
            // already has keeps working, now resolving to the status tracker instead of the
            // pre-acceptance Accept/Decline page.
            if (offer.TrackingToken is not null)
                ride.TrackingToken = offer.TrackingToken;

            offer.Status = "Accepted";
            offer.RespondedAt = DateTime.UtcNow;

            _db.RideEvents.Add(new RideEvent
            {
                Id = Guid.NewGuid(),
                RideId = rideId,
                FromStatus = RideStatus.Dispatched,
                ToStatus = RideStatus.Confirmed,
                TriggeredBy = "vendor_sms",
                Notes = $"Vendor accepted. twilio_sid:{messageSid}"
            });
            ride.Status = RideStatus.Confirmed;

            // Supersede all other pending offers
            var otherOffers = await _db.RideDispatchOffers
                .Where(o => o.RideId == rideId && o.VendorId != vendorId && o.Status == "Pending")
                .Include(o => o.Vendor)
                .ToListAsync();

            foreach (var other in otherOffers)
            {
                other.Status = "Superseded";
                other.RespondedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            // Notify superseded vendors asynchronously
            if (otherOffers.Count > 0)
            {
                var vendor = await _db.Vendors.FindAsync(vendorId);
                _ = Task.Run(async () =>
                {
                    foreach (var other in otherOffers)
                    {
                        try { await _twilioDispatch.SendRideClaimedSmsAsync(other.Vendor, ride); }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to send 'ride claimed' SMS to vendor {VendorId}", other.VendorId); }
                    }
                });
            }

            await _hubContext.Clients.Group($"facility:{ride.FacilityId}")
                .SendAsync("RideStatusChanged", new
                {
                    ride.Id,
                    FromStatus = "Dispatched",
                    ToStatus = "Confirmed",
                    ride.VendorId
                });

            _logger.LogInformation(
                "ClaimRide success: RideId={RideId} VendorId={VendorId} SupersededOffers={SupersededCount}",
                rideId, vendorId, otherOffers.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClaimRide transaction failed: RideId={RideId} VendorId={VendorId}", rideId, vendorId);
            await tx.RollbackAsync();
            throw;
        }
    }

    // Called by the Twilio webhook (reply "2") and the vendor tracking-page Decline
    // button. Marks this vendor's offer Declined; if it was the last Pending offer on
    // the ride, cancels the ride automatically since no one is left to service it.
    public async Task<bool> DeclineOfferAsync(Guid rideId, Guid vendorId, string triggeredBy, string notes)
    {
        var offer = await _db.RideDispatchOffers
            .Include(o => o.Ride)
            .Include(o => o.Vendor)
            .FirstOrDefaultAsync(o => o.RideId == rideId && o.VendorId == vendorId && o.Status == "Pending");

        if (offer is null)
        {
            _logger.LogWarning(
                "DeclineOffer rejected: no pending offer for RideId={RideId} VendorId={VendorId}",
                rideId, vendorId);
            return false;
        }

        offer.Status = "Declined";
        offer.RespondedAt = DateTime.UtcNow;

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = offer.RideId,
            FromStatus = offer.Ride.Status,
            ToStatus = offer.Ride.Status,
            TriggeredBy = triggeredBy,
            Notes = $"Vendor {offer.Vendor.Name} declined. {notes}"
        });
        await _db.SaveChangesAsync();

        // Check if ALL vendors declined — if so mark Cancelled
        var anyPending = await _db.RideDispatchOffers
            .AnyAsync(o => o.RideId == offer.RideId && o.Status == "Pending");
        if (!anyPending)
        {
            await AdvanceStatusAsync(offer.RideId, RideStatus.Cancelled, triggeredBy, $"All vendors declined. {notes}");
        }

        _logger.LogInformation(
            "DeclineOffer success: RideId={RideId} VendorId={VendorId} AllDeclined={AllDeclined}",
            rideId, vendorId, !anyPending);

        return true;
    }

    public async Task<Ride> AdvanceStatusAsync(
        Guid rideId,
        RideStatus newStatus,
        string triggeredBy,
        string? notes = null,
        Guid? requiredOrgId = null)
    {
        _logger.LogInformation(
            "AdvanceStatus: RideId={RideId} NewStatus={NewStatus} TriggeredBy={TriggeredBy}",
            rideId, newStatus, triggeredBy);

        var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == rideId);
        if (ride is null)
        {
            _logger.LogWarning("AdvanceStatus failed: ride {RideId} not found", rideId);
            throw new KeyNotFoundException($"Ride {rideId} not found.");
        }
        if (requiredOrgId.HasValue && ride.OrganizationId != requiredOrgId.Value)
        {
            _logger.LogWarning(
                "AdvanceStatus rejected: RideId={RideId} belongs to org {RideOrgId}, caller org {OrgId}",
                rideId, ride.OrganizationId, requiredOrgId.Value);
            throw new UnauthorizedAccessException("Ride does not belong to your organization.");
        }
        _stateMachine.Validate(ride.Status, newStatus, ride.DispatchChannel);

        var fromStatus = ride.Status;
        ride.Status = newStatus;

        if (RideStateMachine.IsTerminal(newStatus))
            ride.TrackingToken = null;

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = rideId,
            FromStatus = fromStatus,
            ToStatus = newStatus,
            TriggeredBy = triggeredBy,
            Notes = notes
        });

        await _db.SaveChangesAsync();

        await _hubContext.Clients.Group($"facility:{ride.FacilityId}")
            .SendAsync("RideStatusChanged", new
            {
                ride.Id,
                FromStatus = fromStatus.ToString(),
                ToStatus = newStatus.ToString(),
                ride.ResidentId,
                ride.VendorId
            });

        _logger.LogInformation(
            "AdvanceStatus complete: RideId={RideId} {FromStatus} → {ToStatus} TriggeredBy={TriggeredBy}",
            rideId, fromStatus, newStatus, triggeredBy);

        // Push a notification to every coordinator device at this facility for the two
        // moments they most need to know about without having the dashboard open: the
        // vehicle showing up, and the resident safely at their destination.
        if (newStatus == RideStatus.Arrived || newStatus == RideStatus.Dropped)
        {
            var facilityId = ride.FacilityId;
            var residentId = ride.ResidentId;
            var vendorId = ride.VendorId;
            var destinationAddress = ride.DestinationAddress;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedFcm = scope.ServiceProvider.GetRequiredService<FcmService>();

                var residentName = residentId.HasValue
                    ? await scopedDb.Residents.Where(r => r.Id == residentId.Value)
                        .Select(r => r.FirstName + " " + r.LastName).FirstOrDefaultAsync()
                    : null;
                var vendorName = vendorId.HasValue
                    ? await scopedDb.Vendors.Where(v => v.Id == vendorId.Value)
                        .Select(v => v.Name).FirstOrDefaultAsync()
                    : null;

                var (title, body) = newStatus == RideStatus.Arrived
                    ? ("🚐 Driver arrived", $"{vendorName ?? "Driver"} is outside for {residentName ?? "resident"}")
                    : ("✅ Trip complete", $"{residentName ?? "Resident"} safely at {destinationAddress}");

                try { await scopedFcm.SendToFacilityUsersAsync(facilityId, title, body); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send FCM push for ride {RideId} status {Status}", rideId, newStatus); }
            });
        }

        // Coordinator just requested the return leg — let the vendor know via SMS.
        if (newStatus == RideStatus.AwaitingReturn && ride.VendorId.HasValue)
        {
            var vendorId = ride.VendorId.Value;
            var destinationAddress = ride.DestinationAddress;
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var vendor = await scopedDb.Vendors.FindAsync(vendorId);
                if (vendor is null) return;

                try
                {
                    await _twilioDispatch.SendCheckpointSmsAsync(
                        ride, vendor, $"Resident ready for return pickup from {destinationAddress}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send return-pickup SMS for ride {RideId}", ride.Id);
                }
            });
        }

        return ride;
    }

    public async Task<List<RideSummaryDto>> GetTodaysRidesAsync(Guid facilityId, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayEnd = todayStart.AddDays(1);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(todayStart, tz);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(todayEnd, tz);

        return await _db.Rides
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.PickupTime >= utcStart && r.PickupTime < utcEnd)
            .OrderBy(r => r.PickupTime)
            .Select(r => new RideSummaryDto(
                r.Id,
                r.Resident != null ? r.Resident.FirstName + " " + r.Resident.LastName : "Unknown",
                r.Vendor != null ? r.Vendor.Name : null,
                r.Status.ToString(),
                r.DispatchChannel.ToString(),
                r.PickupTime,
                r.PickupAddress,
                r.DestinationAddress,
                r.LastKnownLat,
                r.LastKnownLng,
                r.LastLocationAt))
            .ToListAsync();
    }

    public async Task<List<RideSummaryDto>> GetUpcomingRidesAsync(Guid facilityId, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var todayEnd = todayStart.AddDays(1);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(todayEnd, tz);

        return await _db.Rides
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId && r.PickupTime >= utcEnd)
            .OrderBy(r => r.PickupTime)
            .Select(r => new RideSummaryDto(
                r.Id,
                r.Resident != null ? r.Resident.FirstName + " " + r.Resident.LastName : "Unknown",
                r.Vendor != null ? r.Vendor.Name : null,
                r.Status.ToString(),
                r.DispatchChannel.ToString(),
                r.PickupTime,
                r.PickupAddress,
                r.DestinationAddress,
                r.LastKnownLat,
                r.LastKnownLng,
                r.LastLocationAt))
            .ToListAsync();
    }

    public async Task<RideDetailDto?> GetRideDetailAsync(Guid rideId, Guid organizationId)
    {
        return await _db.Rides
            .AsNoTracking()
            .Where(r => r.Id == rideId && r.OrganizationId == organizationId)
            .Select(r => new RideDetailDto(
                r.Id,
                r.FacilityId,
                r.ResidentId,
                r.Resident != null ? r.Resident.FirstName + " " + r.Resident.LastName : "Unknown",
                r.VendorId,
                r.Vendor != null ? r.Vendor.Name : null,
                r.Vendor != null ? r.Vendor.PhoneNumber : null,
                r.Status.ToString(),
                r.DispatchChannel.ToString(),
                r.ExternalTripId,
                r.PickupTime,
                r.PickupAddress,
                r.DestinationAddress,
                r.TrackingToken,
                r.LastKnownLat,
                r.LastKnownLng,
                r.LastLocationAt,
                r.Events.OrderBy(e => e.OccurredAt).Select(e => new RideEventDto(
                    e.FromStatus.ToString(),
                    e.ToStatus.ToString(),
                    e.TriggeredBy,
                    e.Notes,
                    e.OccurredAt)).ToList()))
            .FirstOrDefaultAsync();
    }

    public async Task<List<DispatchOfferDto>> GetDispatchOffersAsync(Guid rideId, Guid organizationId)
    {
        var offers = await _db.RideDispatchOffers
            .AsNoTracking()
            .Where(o => o.RideId == rideId && o.Ride.OrganizationId == organizationId)
            .Select(o => new
            {
                o.VendorId,
                VendorName = o.Vendor.Name,
                VendorPhone = o.Vendor.PhoneNumber,
                o.Status,
                o.SentAt,
                o.RespondedAt,
                o.TrackingToken,
            })
            .ToListAsync();

        return offers
            .Select(o => new DispatchOfferDto(
                o.VendorId,
                o.VendorName,
                o.VendorPhone,
                o.Status,
                o.SentAt,
                o.RespondedAt,
                o.TrackingToken is not null ? $"{_appConfig.BaseUrl}/track/{o.TrackingToken}" : null))
            .ToList();
    }

    public async Task<Ride> RedispatchAsync(Guid rideId, Guid organizationId)
    {
        _logger.LogInformation("Redispatch started: OriginalRideId={RideId} OrgId={OrgId}", rideId, organizationId);

        var ride = await _db.Rides
            .Include(r => r.Resident)
            .FirstOrDefaultAsync(r => r.Id == rideId);

        if (ride is null)
        {
            _logger.LogWarning("Redispatch failed: ride {RideId} not found", rideId);
            throw new KeyNotFoundException($"Ride {rideId} not found.");
        }
        if (ride.OrganizationId != organizationId)
        {
            _logger.LogWarning(
                "Redispatch rejected: RideId={RideId} org {RideOrgId} != caller org {OrgId}",
                rideId, ride.OrganizationId, organizationId);
            throw new UnauthorizedAccessException();
        }
        if (ride.Status != RideStatus.Cancelled)
        {
            _logger.LogWarning(
                "Redispatch rejected: RideId={RideId} has status {Status}, must be Cancelled",
                rideId, ride.Status);
            throw new InvalidOperationException("Only cancelled rides can be redispatched.");
        }

        var org = await _db.Organizations.FirstAsync(o => o.Id == organizationId);
        var facility = await _db.Facilities.FirstAsync(f => f.Id == ride.FacilityId);
        var (channel, vendors) = await _dispatchRouter.RouteAsync(ride.Resident, org, facility, ride.DispatchChannel);

        var newRide = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = ride.FacilityId,
            OrganizationId = ride.OrganizationId,
            ResidentId = ride.ResidentId,
            VendorId = null,
            Status = RideStatus.Dispatched,
            DispatchChannel = channel,
            PickupTime = ride.PickupTime,
            PickupAddress = ride.PickupAddress,
            DestinationAddress = ride.DestinationAddress,
        };
        _db.Rides.Add(newRide);

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = newRide.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Dispatched,
            TriggeredBy = "redispatch",
            Notes = $"Redispatched from cancelled ride {rideId} — broadcast to {vendors.Count} vendor(s)"
        });

        foreach (var v in vendors)
        {
            _db.RideDispatchOffers.Add(new RideDispatchOffer
            {
                Id = Guid.NewGuid(),
                RideId = newRide.Id,
                VendorId = v.Id,
                Status = "Pending"
            });
        }

        await _db.SaveChangesAsync();

        if (vendors.Count > 0 && ride.Resident is not null
            && (channel == DispatchChannel.SmsNemt || channel == DispatchChannel.SmsTaxi))
        {
            _ = Task.Run(async () =>
            {
                foreach (var v in vendors)
                {
                    try { await _twilioDispatch.SendBookingSmsAsync(newRide, v, ride.Resident, null); }
                    catch (Exception ex) { _logger.LogError(ex, "Redispatch SMS failed for vendor {VendorId}", v.Id); }
                }
            });
        }

        await _hubContext.Clients.Group($"facility:{newRide.FacilityId}")
            .SendAsync("RideCreated", new { newRide.Id, newRide.Status, newRide.DispatchChannel });

        _logger.LogInformation(
            "Redispatch complete: NewRideId={NewRideId} OriginalRideId={OriginalRideId} Channel={Channel} VendorCount={VendorCount}",
            newRide.Id, rideId, newRide.DispatchChannel, vendors.Count);

        return newRide;
    }
}

public record RideSummaryDto(
    Guid Id, string ResidentName, string? VendorName,
    string Status, string DispatchChannel,
    DateTime PickupTime, string PickupAddress, string DestinationAddress,
    double? LastKnownLat, double? LastKnownLng, DateTime? LastLocationAt);

public record RideDetailDto(
    Guid Id, Guid FacilityId, Guid? ResidentId, string ResidentName,
    Guid? VendorId, string? VendorName, string? VendorPhone,
    string Status, string DispatchChannel, string? ExternalTripId,
    DateTime PickupTime, string PickupAddress, string DestinationAddress,
    string? TrackingToken,
    double? LastKnownLat, double? LastKnownLng,
    DateTime? LastLocationAt, List<RideEventDto> Events);

public record RideEventDto(
    string FromStatus, string ToStatus, string TriggeredBy,
    string? Notes, DateTime OccurredAt);

public record DispatchOfferDto(
    Guid VendorId, string VendorName, string VendorPhone,
    string Status, DateTime SentAt, DateTime? RespondedAt, string? TrackingUrl);
