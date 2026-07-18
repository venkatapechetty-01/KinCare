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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KinCare.Tests;

public class RideServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RideService _sut;
    private readonly Mock<IHubContext<RideStatusHub>> _mockHub;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockGroup;
    private readonly Mock<FcmService> _mockFcm;
    private readonly RideStateMachine _stateMachine;

    public RideServiceTests()
    {
        var dbName = "RideServiceTests_" + Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new AppDbContext(options);

        _mockHub = new Mock<IHubContext<RideStatusHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockGroup = new Mock<IClientProxy>();

        _mockHub.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockGroup.Object);
        _mockGroup
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _stateMachine = new RideStateMachine();

        var twilioConfig = Options.Create(new TwilioConfig());
        var twilioAppConfig = Options.Create(new AppConfig());
        var twilioLogger = NullLogger<TwilioDispatchService>.Instance;
        var twilioDispatch = new TwilioDispatchService(twilioConfig, twilioAppConfig, twilioLogger);

        var fcmConfig = Options.Create(new FcmConfig());
        var fcmLogger = NullLogger<FcmService>.Instance;
        _mockFcm = new Mock<FcmService>(_db, fcmConfig, fcmLogger);
        _mockFcm.Setup(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Fire-and-forget background work in RideService (SMS dispatch, FCM push) resolves
        // a fresh AppDbContext AND a fresh FcmService from a new DI scope rather than using
        // the instances above directly — both need to be registered here too, or the
        // scope's GetRequiredService<FcmService>() throws (service not found) inside the
        // unawaited Task.Run and the push silently never happens.
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<FcmService>(_mockFcm.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var planGate = new PlanGate();
        var dispatchRouter = new DispatchRouter(_db, planGate);

        var appConfig = Options.Create(new AppConfig { BaseUrl = "https://test.kincare.io" });
        var logger = NullLogger<RideService>.Instance;

        _sut = new RideService(
            _db,
            _stateMachine,
            dispatchRouter,
            _mockHub.Object,
            twilioDispatch,
            _mockFcm.Object,
            appConfig,
            logger,
            scopeFactory);
    }

    // ── AdvanceStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceStatusAsync_HappyPath_DispatchedToConfirmed_SavesEventAndReturnsRide()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        var result = await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Confirmed, "test");

        result.Status.Should().Be(RideStatus.Confirmed);

        var evt = await _db.RideEvents.FirstAsync(e => e.RideId == ride.Id && e.ToStatus == RideStatus.Confirmed);
        evt.FromStatus.Should().Be(RideStatus.Dispatched);
        evt.TriggeredBy.Should().Be("test");
    }

    [Fact]
    public async Task AdvanceStatusAsync_TerminalTransitionToCompleted_NullsTrackingToken()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dropped);
        ride.TrackingToken = "sometoken";
        await _db.SaveChangesAsync();

        var result = await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Completed, "coordinator");

        result.TrackingToken.Should().BeNull();
        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.TrackingToken.Should().BeNull();
    }

    [Fact]
    public async Task AdvanceStatusAsync_TerminalTransitionToCancelled_NullsTrackingToken()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);
        ride.TrackingToken = "activetoken";
        await _db.SaveChangesAsync();

        var result = await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Cancelled, "coordinator");

        result.TrackingToken.Should().BeNull();
    }

    [Fact]
    public async Task AdvanceStatusAsync_InvalidTransition_ThrowsInvalidOperationException()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        var act = () => _sut.AdvanceStatusAsync(ride.Id, RideStatus.Completed, "test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid ride status transition*");
    }

    [Fact]
    public async Task AdvanceStatusAsync_WrongOrg_ThrowsUnauthorizedAccessException()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        var differentOrgId = Guid.NewGuid();

        var act = () => _sut.AdvanceStatusAsync(ride.Id, RideStatus.Confirmed, "test", requiredOrgId: differentOrgId);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AdvanceStatusAsync_RideNotFound_ThrowsKeyNotFoundException()
    {
        var act = () => _sut.AdvanceStatusAsync(Guid.NewGuid(), RideStatus.Confirmed, "test");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AdvanceStatusAsync_Success_BroadcastsSignalRRideStatusChanged()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Confirmed, "test");

        _mockGroup.Verify(
            p => p.SendCoreAsync(
                "RideStatusChanged",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceStatusAsync_ToArrived_SendsFcmPush()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.EnRoute);

        await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Arrived, "test");
        await Task.Delay(300); // let the fire-and-forget FCM push (Task.Run) complete

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(
            facility.Id,
            It.Is<string>(t => t.Contains("arrived", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceStatusAsync_ToDropped_SendsFcmPush()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.AtDestination);

        await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Dropped, "test");
        await Task.Delay(300);

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(
            facility.Id,
            It.Is<string>(t => t.Contains("complete", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceStatusAsync_ToConfirmed_DoesNotSendFcmPush()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        await _sut.AdvanceStatusAsync(ride.Id, RideStatus.Confirmed, "test");
        await Task.Delay(300);

        _mockFcm.Verify(f => f.SendToFacilityUsersAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AdvanceStatusAsync_WithOptionalOrgId_MatchingOrg_Succeeds()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        var result = await _sut.AdvanceStatusAsync(
            ride.Id, RideStatus.Confirmed, "coordinator", requiredOrgId: org.Id);

        result.Status.Should().Be(RideStatus.Confirmed);
    }

    // ── ClaimRideAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimRideAsync_HappyPath_AssignsVendorAndSetsConfirmed()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending");

        var result = await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMS123");

        result.Should().BeTrue();
        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Confirmed);
        dbRide.VendorId.Should().Be(vendor.Id);
    }

    [Fact]
    public async Task ClaimRideAsync_HappyPath_SupersedesOtherPendingOffers()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor1 = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var vendor2 = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var vendor3 = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor1.Id, "Pending");
        SeedOffer(ride.Id, vendor2.Id, "Pending");
        SeedOffer(ride.Id, vendor3.Id, "Pending");

        await _sut.ClaimRideAsync(ride.Id, vendor1.Id, "SMS_ACCEPT");

        var offer2 = await _db.RideDispatchOffers.FirstAsync(o => o.RideId == ride.Id && o.VendorId == vendor2.Id);
        var offer3 = await _db.RideDispatchOffers.FirstAsync(o => o.RideId == ride.Id && o.VendorId == vendor3.Id);
        offer2.Status.Should().Be("Superseded");
        offer3.Status.Should().Be("Superseded");
    }

    [Fact]
    public async Task ClaimRideAsync_RideNotInDispatchedState_ReturnsFalse()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);
        SeedOffer(ride.Id, vendor.Id, "Pending");

        var result = await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMS999");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClaimRideAsync_NoPendingOfferForVendor_ReturnsFalse()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var otherVendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        // Only seed an offer for otherVendor, not for vendor
        SeedOffer(ride.Id, otherVendor.Id, "Pending");

        var result = await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMS456");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClaimRideAsync_SmartVendorOffer_ActivatesTrackingToken()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt, VendorCapabilityTier.Smart);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending", trackingToken: "abc123");

        await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMS789");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.TrackingToken.Should().Be("abc123");
    }

    [Fact]
    public async Task ClaimRideAsync_RecordsEventWithMessageSidInNotes()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending");

        await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMSMSG001");

        var evt = await _db.RideEvents
            .FirstOrDefaultAsync(e => e.RideId == ride.Id && e.Notes != null && e.Notes.Contains("twilio_sid:SMSMSG001"));
        evt.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimRideAsync_AcceptedOfferStatus_ReturnsFalse()
    {
        // Offer already accepted — no longer starts with "Pending"
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Accepted");

        var result = await _sut.ClaimRideAsync(ride.Id, vendor.Id, "SMS_DUP");

        result.Should().BeFalse();
    }

    // ── DeclineOfferAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeclineOfferAsync_MarksOfferDeclined()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending");

        var result = await _sut.DeclineOfferAsync(ride.Id, vendor.Id, "vendor_sms", "twilio_sid:SMS1");

        result.Should().BeTrue();
        var offer = await _db.RideDispatchOffers.FirstAsync(o => o.RideId == ride.Id && o.VendorId == vendor.Id);
        offer.Status.Should().Be("Declined");
        offer.RespondedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeclineOfferAsync_LastPendingOffer_CancelsRide()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor.Id, "Pending");

        await _sut.DeclineOfferAsync(ride.Id, vendor.Id, "vendor_sms", "twilio_sid:SMS1");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Cancelled);
    }

    [Fact]
    public async Task DeclineOfferAsync_OtherOffersStillPending_RideNotCancelled()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor1 = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var vendor2 = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, vendor1.Id, "Pending");
        SeedOffer(ride.Id, vendor2.Id, "Pending");

        await _sut.DeclineOfferAsync(ride.Id, vendor1.Id, "vendor_sms", "twilio_sid:SMS1");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.Dispatched);
        var otherOffer = await _db.RideDispatchOffers.FirstAsync(o => o.RideId == ride.Id && o.VendorId == vendor2.Id);
        otherOffer.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task DeclineOfferAsync_NoPendingOfferForVendor_ReturnsFalse()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var otherVendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);
        SeedOffer(ride.Id, otherVendor.Id, "Pending");

        var result = await _sut.DeclineOfferAsync(ride.Id, vendor.Id, "vendor_sms", "twilio_sid:SMS2");

        result.Should().BeFalse();
    }

    // ── GetRideDetailAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRideDetailAsync_WrongOrg_ReturnsNull()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        var result = await _sut.GetRideDetailAsync(ride.Id, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRideDetailAsync_RideNotFound_ReturnsNull()
    {
        var org = SeedOrg();

        var result = await _sut.GetRideDetailAsync(Guid.NewGuid(), org.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRideDetailAsync_ValidRide_ReturnsDtoWithEventsOrderedByOccurredAt()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Confirmed);

        // Add events out of order to verify sorting
        var laterTime = DateTime.UtcNow.AddMinutes(5);
        var earlierTime = DateTime.UtcNow.AddMinutes(-5);

        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Confirmed,
            ToStatus = RideStatus.Confirmed,
            TriggeredBy = "later_event",
            OccurredAt = laterTime
        });
        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Confirmed,
            TriggeredBy = "earlier_event",
            OccurredAt = earlierTime
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetRideDetailAsync(ride.Id, org.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(ride.Id);
        result.Events.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Events.Should().BeInAscendingOrder(e => e.OccurredAt);
        result.Events.First().TriggeredBy.Should().Be("earlier_event");
    }

    [Fact]
    public async Task GetRideDetailAsync_ValidRide_ReturnsCorrectStatus()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.EnRoute);

        var result = await _sut.GetRideDetailAsync(ride.Id, org.Id);

        result.Should().NotBeNull();
        result!.Status.Should().Be("EnRoute");
    }

    // ── RedispatchAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RedispatchAsync_RideNotCancelled_ThrowsInvalidOperationException()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Dispatched);

        var act = () => _sut.RedispatchAsync(ride.Id, org.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only cancelled rides can be redispatched*");
    }

    [Fact]
    public async Task RedispatchAsync_WrongOrg_ThrowsUnauthorizedAccessException()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Cancelled);

        var act = () => _sut.RedispatchAsync(ride.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RedispatchAsync_RideNotFound_ThrowsKeyNotFoundException()
    {
        var org = SeedOrg();

        var act = () => _sut.RedispatchAsync(Guid.NewGuid(), org.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RedispatchAsync_ValidCancelledRide_CreatesNewRideWithSameAddressesAndDispatchedStatus()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Cancelled);
        ride.PickupAddress = "123 Main St";
        ride.DestinationAddress = "456 Elm Ave";
        await _db.SaveChangesAsync();

        var newRide = await _sut.RedispatchAsync(ride.Id, org.Id);

        newRide.Should().NotBeNull();
        newRide.Id.Should().NotBe(ride.Id);
        newRide.Status.Should().Be(RideStatus.Dispatched);
        newRide.PickupAddress.Should().Be("123 Main St");
        newRide.DestinationAddress.Should().Be("456 Elm Ave");
        newRide.FacilityId.Should().Be(facility.Id);
        newRide.OrganizationId.Should().Be(org.Id);
    }

    [Fact]
    public async Task RedispatchAsync_ValidCancelledRide_CreatesRedispatchEvent()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Cancelled);

        var newRide = await _sut.RedispatchAsync(ride.Id, org.Id);

        var evt = await _db.RideEvents
            .FirstOrDefaultAsync(e => e.RideId == newRide.Id && e.TriggeredBy == "redispatch");
        evt.Should().NotBeNull();
    }

    [Fact]
    public async Task RedispatchAsync_ValidCancelledRide_NewRideHasNullVendorId()
    {
        var org = SeedOrg();
        var facility = SeedFacility(org.Id);
        var vendor = SeedVendor(facility.Id, DispatchMethod.SmsNemt);
        var ride = SeedRide(facility.Id, org.Id, RideStatus.Cancelled);
        ride.VendorId = vendor.Id;
        await _db.SaveChangesAsync();

        var newRide = await _sut.RedispatchAsync(ride.Id, org.Id);

        newRide.VendorId.Should().BeNull();
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private Organization SeedOrg()
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
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
            Name = "Test Facility",
            Address = "1 Facility Rd",
            Timezone = "America/New_York"
        };
        _db.Facilities.Add(facility);
        _db.SaveChanges();
        return facility;
    }

    private Vendor SeedVendor(Guid facilityId, DispatchMethod method,
        VendorCapabilityTier tier = VendorCapabilityTier.Basic)
    {
        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            Name = $"Vendor_{method}_{Guid.NewGuid():N}",
            PhoneNumber = $"+1555{Guid.NewGuid().ToString()[..7].Replace("-", "")}",
            DispatchMethod = method,
            CapabilityTier = tier,
            IsActive = true
        };
        _db.Vendors.Add(vendor);
        _db.SaveChanges();
        return vendor;
    }

    private Resident SeedResident(Guid facilityId)
    {
        var resident = new Resident
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            FirstName = "Jane",
            LastName = "Doe",
            IsActive = true
        };
        _db.Residents.Add(resident);
        _db.SaveChanges();
        return resident;
    }

    private Ride SeedRide(Guid facilityId, Guid orgId, RideStatus status = RideStatus.Dispatched)
    {
        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            Status = status,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(2),
            PickupAddress = "100 Pickup Lane",
            DestinationAddress = "200 Destination Dr"
        };
        _db.Rides.Add(ride);
        _db.SaveChanges();
        return ride;
    }

    private RideDispatchOffer SeedOffer(Guid rideId, Guid vendorId, string status, string? trackingToken = null)
    {
        var offer = new RideDispatchOffer
        {
            Id = Guid.NewGuid(),
            RideId = rideId,
            VendorId = vendorId,
            Status = status,
            TrackingToken = trackingToken
        };
        _db.RideDispatchOffers.Add(offer);
        _db.SaveChanges();
        return offer;
    }

    public void Dispose() => _db.Dispose();
}
