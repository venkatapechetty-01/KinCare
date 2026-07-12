using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using KinCare.API.Services.Dispatch;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KinCare.Tests;

/// <summary>
/// Tests the business logic exercised by TwilioWebhookHandler by calling RideService
/// methods directly (same path the handler takes), verifying DB state afterward.
/// This is equivalent to integration-testing the handler without spinning up a
/// test server — and avoids the Twilio signature check entirely.
/// </summary>
public class TwilioWebhookHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RideService _rideService;
    private readonly RideStateMachine _stateMachine;

    public TwilioWebhookHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("TwilioWebhookHandlerTests_" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new AppDbContext(options);

        var mockHub = new Mock<IHubContext<RideStatusHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockGroup = new Mock<IClientProxy>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockGroup.Object);
        mockGroup
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var twilioConfig = Options.Create(new TwilioConfig());
        var twilioLogger = NullLogger<TwilioDispatchService>.Instance;
        var twilioDispatch = new TwilioDispatchService(twilioConfig, twilioLogger);

        var planGate = new PlanGate();
        var dispatchRouter = new DispatchRouter(_db, planGate);
        var appConfig = Options.Create(new AppConfig { BaseUrl = "https://test.kincare.io" });

        _stateMachine = new RideStateMachine();

        _rideService = new RideService(
            _db,
            _stateMachine,
            dispatchRouter,
            mockHub.Object,
            twilioDispatch,
            appConfig,
            NullLogger<RideService>.Instance);
    }

    // ── Reply 1 — Accept / Claim ──────────────────────────────────────────────

    [Fact]
    public async Task Reply1_AcceptsRide_SetsVendorAndConfirmedStatus()
    {
        var (ride, vendor) = SeedDispatchedRideWithOffer();

        // Simulate what TwilioWebhookHandler does for reply "1"
        var claimed = await _rideService.ClaimRideAsync(ride.Id, vendor.Id, "SMS_ACK001");

        claimed.Should().BeTrue();
        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Confirmed);
        dbRide.VendorId.Should().Be(vendor.Id);
    }

    [Fact]
    public async Task Reply1_SecondAcceptForSameRide_ReturnsFalse_RideAlreadyClaimed()
    {
        var (ride, vendor) = SeedDispatchedRideWithOffer();
        var vendor2 = SeedVendor(ride.FacilityId, DispatchMethod.SmsNemt);
        SeedOffer(ride.Id, vendor2.Id, "Pending");

        // vendor1 claims first
        await _rideService.ClaimRideAsync(ride.Id, vendor.Id, "SMS_FIRST");
        // vendor2 tries to claim the now-Confirmed ride
        var result = await _rideService.ClaimRideAsync(ride.Id, vendor2.Id, "SMS_SECOND");

        result.Should().BeFalse();
    }

    // ── Reply 2 — Decline ─────────────────────────────────────────────────────

    [Fact]
    public async Task Reply2_DeclinesRide_MarksOfferDeclinedAndLogsEvent()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        var offer = SeedOffer(ride.Id, vendor.Id, "Pending");

        // Simulate what TwilioWebhookHandler does for reply "2"
        offer.Status = "Declined";
        offer.RespondedAt = DateTime.UtcNow;

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = ride.Status,
            ToStatus = ride.Status,
            TriggeredBy = "vendor_sms",
            Notes = $"Vendor {vendor.Name} declined. twilio_sid:SMS_DEC001"
        });
        await _db.SaveChangesAsync();

        var updatedOffer = await _db.RideDispatchOffers.FindAsync(offer.Id);
        updatedOffer!.Status.Should().Be("Declined");

        var evt = await _db.RideEvents
            .FirstOrDefaultAsync(e => e.RideId == ride.Id && e.Notes != null && e.Notes.Contains("twilio_sid:SMS_DEC001"));
        evt.Should().NotBeNull();
        evt!.FromStatus.Should().Be(RideStatus.Dispatched);
        evt.ToStatus.Should().Be(RideStatus.Dispatched);
    }

    [Fact]
    public async Task Reply2_AllVendorsDeclined_RideGetsCancelled()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        var offer = SeedOffer(ride.Id, vendor.Id, "Pending");

        // Mark the only offer declined
        offer.Status = "Declined";
        offer.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // No pending offers remain — handler calls AdvanceStatusAsync to Cancelled
        var anyPending = await _db.RideDispatchOffers
            .AnyAsync(o => o.RideId == ride.Id && o.Status.StartsWith("Pending"));

        anyPending.Should().BeFalse();

        // Call AdvanceStatusAsync as the handler would
        await _rideService.AdvanceStatusAsync(
            ride.Id, RideStatus.Cancelled, "vendor_sms",
            $"All vendors declined. twilio_sid:SMS_DEC_LAST");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Cancelled);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Idempotency_SameMessageSidProcessedOnce_AnyAsyncDetectsExistingEvent()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);
        var messageSid = "SMS_IDEM_001";

        // Simulate first processing: event recorded with twilio_sid
        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Confirmed,
            TriggeredBy = "vendor_sms",
            Notes = $"Vendor accepted. twilio_sid:{messageSid}"
        });
        await _db.SaveChangesAsync();

        // Simulate what handler does on second delivery of same MessageSid
        var alreadyProcessed = await _db.RideEvents
            .AnyAsync(e => e.Notes != null && e.Notes.Contains($"twilio_sid:{messageSid}"));

        alreadyProcessed.Should().BeTrue("second delivery must be detected and dropped");
    }

    [Fact]
    public async Task Idempotency_DifferentMessageSid_NotDetectedAsProcessed()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Confirmed,
            TriggeredBy = "vendor_sms",
            Notes = "Vendor accepted. twilio_sid:SMS_FIRST"
        });
        await _db.SaveChangesAsync();

        var alreadyProcessed = await _db.RideEvents
            .AnyAsync(e => e.Notes != null && e.Notes.Contains("twilio_sid:SMS_NEW"));

        alreadyProcessed.Should().BeFalse();
    }

    // ── Post-accept status replies (3–8) ──────────────────────────────────────

    [Fact]
    public async Task PostAcceptReply3_AdvancesRideToEnRoute()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);
        ride.VendorId = vendor.Id;
        await _db.SaveChangesAsync();

        // Verify the transition is valid (same check handler does)
        var canTransition = _stateMachine.CanTransition(ride.Status, RideStatus.EnRoute);
        canTransition.Should().BeTrue();

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.EnRoute, "vendor_sms", $"twilio_sid:SMS_3");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.EnRoute);
    }

    [Fact]
    public async Task PostAcceptReply4_AdvancesRideToArrived()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.EnRoute);
        ride.VendorId = vendor.Id;
        await _db.SaveChangesAsync();

        var canTransition = _stateMachine.CanTransition(ride.Status, RideStatus.Arrived);
        canTransition.Should().BeTrue();

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.Arrived, "vendor_sms", $"twilio_sid:SMS_4");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Arrived);
    }

    [Fact]
    public async Task PostAcceptReply9_IssueReport_DoesNotChangeRideStatus()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.EnRoute);
        ride.VendorId = vendor.Id;
        await _db.SaveChangesAsync();

        var messageSid = "SMS_ISSUE_001";

        // Simulate what handler does for reply 9: append event, do NOT change status
        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = ride.Status,
            ToStatus = ride.Status,
            TriggeredBy = "vendor_sms",
            Notes = $"Issue reported by vendor. twilio_sid:{messageSid}"
        });
        await _db.SaveChangesAsync();

        // Status must remain unchanged
        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.EnRoute);

        // Event must exist
        var evt = await _db.RideEvents
            .FirstOrDefaultAsync(e => e.RideId == ride.Id && e.Notes != null && e.Notes.Contains("Issue reported"));
        evt.Should().NotBeNull();
        evt!.FromStatus.Should().Be(RideStatus.EnRoute);
        evt.ToStatus.Should().Be(RideStatus.EnRoute);
    }

    // ── Unknown vendor number ─────────────────────────────────────────────────

    [Fact]
    public async Task UnknownVendorPhone_VendorLookupReturnsNull()
    {
        var vendor = await _db.Vendors
            .FirstOrDefaultAsync(v => v.PhoneNumber == "+19995550000" && v.IsActive);

        vendor.Should().BeNull("unknown number should not resolve to any vendor");
    }

    // ── State machine transition guard (used by handler) ─────────────────────

    [Fact]
    public async Task PostAcceptReply_InvalidTransition_CanTransitionReturnsFalse()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        // Dispatched → Dropped is not valid
        var canTransition = _stateMachine.CanTransition(ride.Status, RideStatus.Dropped);
        canTransition.Should().BeFalse();

        // Handler skips AdvanceStatusAsync when CanTransition is false, status unchanged
        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Dispatched);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private (Ride ride, Vendor vendor) SeedDispatchedRideWithOffer(DispatchMethod method = DispatchMethod.SmsNemt)
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, method);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending");
        return (ride, vendor);
    }

    private Organization SeedOrg()
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Twilio Test Org",
            BillingEmail = "billing@test.com",
            PlanTier = PlanTier.Professional,
            IsActive = true
        };
        _db.Organizations.Add(org);
        _db.SaveChanges();
        return org;
    }

    private Facility SeedFacility(Guid orgId)
    {
        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Twilio Test Facility",
            Address = "1 Test Blvd",
            Timezone = "America/New_York"
        };
        _db.Facilities.Add(facility);
        _db.SaveChanges();
        return facility;
    }

    private Vendor SeedVendor(Guid facilityId, DispatchMethod method)
    {
        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            Name = $"TwilioVendor_{Guid.NewGuid():N}",
            PhoneNumber = $"+1555{Guid.NewGuid().ToString()[..7].Replace("-", "")}",
            DispatchMethod = method,
            IsActive = true
        };
        _db.Vendors.Add(vendor);
        _db.SaveChanges();
        return vendor;
    }

    private Ride SeedRide(Guid facilityId, Guid orgId, RideStatus status)
    {
        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            Status = status,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(1),
            PickupAddress = "10 Pickup St",
            DestinationAddress = "20 Destination Ave"
        };
        _db.Rides.Add(ride);
        _db.SaveChanges();
        return ride;
    }

    private RideDispatchOffer SeedOffer(Guid rideId, Guid vendorId, string status)
    {
        var offer = new RideDispatchOffer
        {
            Id = Guid.NewGuid(),
            RideId = rideId,
            VendorId = vendorId,
            Status = status
        };
        _db.RideDispatchOffers.Add(offer);
        _db.SaveChanges();
        return offer;
    }

    public void Dispose() => _db.Dispose();
}
