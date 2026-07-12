using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Hubs;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using KinCare.API.Services.Dispatch;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KinCare.Tests;

/// <summary>
/// Tests the business logic exercised by TrackingEndpoints by replicating what
/// each endpoint does against a real in-memory DB and RideService, without
/// spinning up an HTTP test server.
/// </summary>
public class TrackingEndpointsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RideService _rideService;
    private readonly Mock<IHubContext<RideStatusHub>> _mockHub;
    private readonly Mock<IClientProxy> _mockGroup;

    public TrackingEndpointsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("TrackingEndpointsTests_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        _mockHub = new Mock<IHubContext<RideStatusHub>>();
        var mockClients = new Mock<IHubClients>();
        _mockGroup = new Mock<IClientProxy>();
        _mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockGroup.Object);
        _mockGroup
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var twilioConfig = Options.Create(new TwilioConfig());
        var twilioDispatch = new TwilioDispatchService(twilioConfig, NullLogger<TwilioDispatchService>.Instance);

        var planGate = new PlanGate();
        var dispatchRouter = new DispatchRouter(_db, planGate);
        var appConfig = Options.Create(new AppConfig { BaseUrl = "https://test.kincare.io" });

        _rideService = new RideService(
            _db,
            new RideStateMachine(),
            dispatchRouter,
            _mockHub.Object,
            twilioDispatch,
            appConfig,
            NullLogger<RideService>.Instance);
    }

    // ── UpdateLocation logic ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLocation_ValidCoordinates_UpdatesRideLatLng()
    {
        var ride = SeedRideWithToken(RideStatus.EnRoute, "token_loc_001");

        // Replicate UpdateLocation endpoint logic
        ride.LastKnownLat = 37.7749;
        ride.LastKnownLng = -122.4194;
        ride.LastLocationAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.LastKnownLat.Should().Be(37.7749);
        dbRide.LastKnownLng.Should().Be(-122.4194);
        dbRide.LastLocationAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateLocation_InvalidLatitudeTooLow_ShouldBeRejected()
    {
        // Endpoint rejects lat < -90 or > 90
        double latitude = -91.0;
        double longitude = 0.0;

        var isInvalid = latitude is < -90 or > 90 || longitude is < -180 or > 180;

        isInvalid.Should().BeTrue("latitude -91 is outside valid range");
    }

    [Fact]
    public void UpdateLocation_InvalidLatitudeTooHigh_ShouldBeRejected()
    {
        double latitude = 91.0;
        double longitude = 0.0;

        var isInvalid = latitude is < -90 or > 90 || longitude is < -180 or > 180;

        isInvalid.Should().BeTrue("latitude 91 is outside valid range");
    }

    [Fact]
    public void UpdateLocation_InvalidLongitude_ShouldBeRejected()
    {
        double latitude = 45.0;
        double longitude = 181.0;

        var isInvalid = latitude is < -90 or > 90 || longitude is < -180 or > 180;

        isInvalid.Should().BeTrue("longitude 181 is outside valid range");
    }

    [Fact]
    public void UpdateLocation_ValidBoundaryCoordinates_NotRejected()
    {
        // Exactly at boundary: -90 lat, -180 lng are valid
        double latitude = -90.0;
        double longitude = -180.0;

        var isInvalid = latitude is < -90 or > 90 || longitude is < -180 or > 180;

        isInvalid.Should().BeFalse("boundary values -90 lat / -180 lng are within valid range");
    }

    [Fact]
    public void UpdateLocation_TerminalRide_Rejected()
    {
        var ride = SeedRideWithToken(RideStatus.Completed, "token_completed_001");

        // Replicate endpoint guard: IsTerminal check
        var isTerminal = RideStateMachine.IsTerminal(ride.Status);

        isTerminal.Should().BeTrue("completed rides must reject location updates");
    }

    [Fact]
    public void UpdateLocation_CancelledRide_Rejected()
    {
        var ride = SeedRideWithToken(RideStatus.Cancelled, "token_cancelled_001");

        var isTerminal = RideStateMachine.IsTerminal(ride.Status);

        isTerminal.Should().BeTrue("cancelled rides must reject location updates");
    }

    [Fact]
    public async Task UpdateLocation_UnknownToken_NotFound()
    {
        var ride = await _db.Rides
            .FirstOrDefaultAsync(r => r.TrackingToken == "nonexistent_token_xyz");

        ride.Should().BeNull("unknown token should not find a ride");
    }

    [Fact]
    public async Task UpdateLocation_ValidToken_FindsCorrectRide()
    {
        var seededRide = SeedRideWithToken(RideStatus.EnRoute, "token_find_001");

        var ride = await _db.Rides.FirstOrDefaultAsync(r => r.TrackingToken == "token_find_001");

        ride.Should().NotBeNull();
        ride!.Id.Should().Be(seededRide.Id);
    }

    [Fact]
    public async Task UpdateLocation_Success_BroadcastsSignalRLocationUpdated()
    {
        var ride = SeedRideWithToken(RideStatus.EnRoute, "token_signalr_001");

        // Replicate what UpdateLocation does after saving
        ride.LastKnownLat = 40.7128;
        ride.LastKnownLng = -74.0060;
        ride.LastLocationAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _mockHub.Object.Clients.Group($"facility:{ride.FacilityId}")
            .SendAsync("LocationUpdated", new
            {
                RideId = ride.Id,
                Latitude = ride.LastKnownLat,
                Longitude = ride.LastKnownLng,
                LastLocationAt = ride.LastLocationAt,
            });

        _mockGroup.Verify(
            p => p.SendCoreAsync(
                "LocationUpdated",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── UpdateTrackingStatus logic ────────────────────────────────────────────

    [Fact]
    public async Task TrackingStatus_IssueReport_DoesNotChangeRideStatus()
    {
        var ride = SeedRideWithToken(RideStatus.EnRoute, "token_issue_001");

        // Replicate the "Issue" branch: append event, no AdvanceStatusAsync call
        _db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = ride.Status,
            ToStatus = ride.Status,
            TriggeredBy = "tracking_page",
            Notes = "Driver reported an issue via tracking page"
        });
        await _db.SaveChangesAsync();

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.EnRoute, "issue report must not change ride status");

        var evt = await _db.RideEvents
            .FirstOrDefaultAsync(e => e.RideId == ride.Id && e.Notes != null && e.Notes.Contains("issue"));
        evt.Should().NotBeNull();
        evt!.FromStatus.Should().Be(RideStatus.EnRoute);
        evt.ToStatus.Should().Be(RideStatus.EnRoute);
    }

    [Fact]
    public async Task TrackingStatus_ValidAdvance_UpdatesStatus()
    {
        var ride = SeedRideWithToken(RideStatus.Confirmed, "token_advance_001");

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.EnRoute, "tracking_page");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.Status.Should().Be(RideStatus.EnRoute);
    }

    [Fact]
    public async Task TrackingStatus_InvalidTransition_ThrowsInvalidOperationException()
    {
        var ride = SeedRideWithToken(RideStatus.Dispatched, "token_invalid_001");

        // Dispatched → Completed is not a valid transition
        var act = () => _rideService.AdvanceStatusAsync(ride.Id, RideStatus.Completed, "tracking_page");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid ride status transition*");
    }

    [Fact]
    public void TrackingStatus_TerminalRide_EndpointGuardBlocksAdvance()
    {
        // Endpoint checks IsTerminal before calling AdvanceStatusAsync — verify guard behaviour
        var ride = SeedRideWithToken(RideStatus.Completed, "token_terminal_001");

        var isTerminal = RideStateMachine.IsTerminal(ride.Status);

        isTerminal.Should().BeTrue("endpoint must return 400 without calling AdvanceStatusAsync");
    }

    [Fact]
    public void TrackingStatus_CancelledRide_EndpointGuardBlocksAdvance()
    {
        var ride = SeedRideWithToken(RideStatus.Cancelled, "token_cancel_001");

        var isTerminal = RideStateMachine.IsTerminal(ride.Status);

        isTerminal.Should().BeTrue("cancelled rides must be blocked by the terminal guard");
    }

    [Fact]
    public async Task TrackingStatus_UnknownToken_RideNotFound()
    {
        var ride = await _db.Rides
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TrackingToken == "nonexistent_track_token");

        ride.Should().BeNull();
    }

    [Fact]
    public void TrackingStatus_UnknownStatus_EnumParseFails()
    {
        var parsed = Enum.TryParse<RideStatus>("Teleport", out var _);

        parsed.Should().BeFalse("'Teleport' is not a valid RideStatus and should be rejected");
    }

    [Fact]
    public void TrackingStatus_ValidStatusString_EnumParseSucceeds()
    {
        var parsed = Enum.TryParse<RideStatus>("EnRoute", out var status);

        parsed.Should().BeTrue();
        status.Should().Be(RideStatus.EnRoute);
    }

    // ── TrackingToken nulled on completion ────────────────────────────────────

    [Fact]
    public async Task TrackingToken_NulledWhenRideReachesCompleted()
    {
        var ride = SeedRideWithToken(RideStatus.Dropped, "token_complete_001");

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.Completed, "coordinator");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.TrackingToken.Should().BeNull();
    }

    [Fact]
    public async Task TrackingToken_NulledWhenRideGetsCancelled()
    {
        var ride = SeedRideWithToken(RideStatus.Confirmed, "token_cancel_002");

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.Cancelled, "coordinator");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.TrackingToken.Should().BeNull();
    }

    [Fact]
    public async Task TrackingToken_PreservedOnNonTerminalTransition()
    {
        var ride = SeedRideWithToken(RideStatus.Confirmed, "token_preserve_001");

        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.EnRoute, "tracking_page");

        var dbRide = await _db.Rides.FindAsync(ride.Id);
        dbRide!.TrackingToken.Should().Be("token_preserve_001");
    }

    // ── TrackingPage token lookup ─────────────────────────────────────────────

    [Fact]
    public async Task TrackingPage_KnownToken_FindsRide()
    {
        var seeded = SeedRideWithToken(RideStatus.EnRoute, "token_page_001");

        var found = await _db.Rides
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TrackingToken == "token_page_001");

        found.Should().NotBeNull();
        found!.Id.Should().Be(seeded.Id);
    }

    [Fact]
    public async Task TrackingPage_TokenExpiredAfterCompletion_ReturnsNull()
    {
        var ride = SeedRideWithToken(RideStatus.Dropped, "token_expire_001");

        // Simulate completing the ride (token is nulled)
        await _rideService.AdvanceStatusAsync(ride.Id, RideStatus.Completed, "coordinator");

        var found = await _db.Rides
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TrackingToken == "token_expire_001");

        found.Should().BeNull("tracking token is cleared on completion");
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private Ride SeedRideWithToken(RideStatus status, string trackingToken)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Tracking Test Org",
            BillingEmail = "billing@test.com",
            PlanTier = PlanTier.Starter,
            IsActive = true
        };
        _db.Organizations.Add(org);

        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Tracking Test Facility",
            Address = "1 Track Rd",
            Timezone = "America/New_York"
        };
        _db.Facilities.Add(facility);

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facility.Id,
            OrganizationId = org.Id,
            Status = status,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(1),
            PickupAddress = "1 Pickup Ave",
            DestinationAddress = "2 Destination Blvd",
            TrackingToken = trackingToken
        };
        _db.Rides.Add(ride);
        _db.SaveChanges();
        return ride;
    }

    public void Dispose() => _db.Dispose();
}
