using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace KinCare.API.IntegrationTests;

public class TrackingEndpointIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TrackingEndpointIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── DB helpers ────────────────────────────────────────────────────────────

    private async Task<(Guid OrgId, Guid FacilityId)> SeedOrgAndFacilityAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = $"Tracking Test Org {Guid.NewGuid():N}",
            BillingEmail = $"billing-{Guid.NewGuid():N}@test.com",
            PlanTier = PlanTier.Starter,
            IsActive = true
        };
        db.Organizations.Add(org);

        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Tracking Test Facility",
            Address = "123 Test St, Detroit, MI",
            Timezone = "America/New_York",
            IsActive = true
        };
        db.Facilities.Add(facility);

        await db.SaveChangesAsync();
        return (org.Id, facility.Id);
    }

    private async Task<Guid> SeedRideWithTokenAsync(
        Guid orgId,
        Guid facilityId,
        string trackingToken,
        RideStatus status = RideStatus.Confirmed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            Status = status,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(2),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI",
            TrackingToken = status is RideStatus.Completed or RideStatus.Cancelled
                ? null          // Terminal rides have no tracking token (nulled by state machine)
                : trackingToken
        };

        // For a Completed ride we want the token to be findable via the seeded token
        // but the domain rule says terminal = null. The test for terminal passes a
        // non-null token only for the seed lookup — the page checks status, not token.
        // Override: the completed-page test seeds with null token and looks up by
        // a separate completed ride seeded with TrackingToken set (pre-terminal state).
        if (status is RideStatus.Completed)
            ride.TrackingToken = trackingToken; // keep token so we can look it up in the test

        db.Rides.Add(ride);

        db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = status,
            ToStatus = status,
            TriggeredBy = "system",
            Notes = "Seeded for tracking test"
        });

        await db.SaveChangesAsync();
        return ride.Id;
    }

    private async Task<Guid> SeedVendorAsync(Guid facilityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            Name = "Tracking Test Vendor",
            PhoneNumber = $"+1555{Guid.NewGuid().ToString("N")[..7]}",
            VendorType = VendorType.Wheelchair,
            DispatchMethod = DispatchMethod.SmsNemt,
            CapabilityTier = VendorCapabilityTier.Basic,
            IsActive = true
        };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    // Mirrors what RideService.BookRideAsync does for every vendor at broadcast time:
    // a Dispatched ride with no TrackingToken of its own yet, and a Pending offer per
    // vendor carrying its own tracking token (only activated onto the ride upon claim).
    private async Task<(Guid RideId, Guid OfferId)> SeedDispatchedRideWithOfferAsync(
        Guid orgId, Guid facilityId, Guid vendorId, string offerToken, string offerStatus = "Pending")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            OrganizationId = orgId,
            Status = RideStatus.Dispatched,
            DispatchChannel = DispatchChannel.SmsNemt,
            PickupTime = DateTime.UtcNow.AddHours(2),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI",
        };
        db.Rides.Add(ride);

        db.RideEvents.Add(new RideEvent
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            FromStatus = RideStatus.Dispatched,
            ToStatus = RideStatus.Dispatched,
            TriggeredBy = "system",
            Notes = "Seeded for tracking test"
        });

        var offer = new RideDispatchOffer
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            VendorId = vendorId,
            Status = offerStatus,
            TrackingToken = offerToken
        };
        db.RideDispatchOffers.Add(offer);

        await db.SaveChangesAsync();
        return (ride.Id, offer.Id);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTrackingPage_ValidToken_Returns200Html()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.Confirmed);

        var response = await _client.GetAsync($"/track/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var contentType = response.Content.Headers.ContentType?.MediaType;
        contentType.Should().Be("text/html");
    }

    [Fact]
    public async Task GetTrackingPage_InvalidToken_Returns404Html()
    {
        var response = await _client.GetAsync("/track/nonexistent-token-that-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        contentType.Should().Be("text/html");
    }

    [Fact]
    public async Task GetTrackingPage_TerminalRide_Returns200CompletedHtml()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.Completed);

        var response = await _client.GetAsync($"/track/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var contentType = response.Content.Headers.ContentType?.MediaType;
        contentType.Should().Be("text/html");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Trip Complete");
    }

    [Fact]
    public async Task UpdateLocation_ValidToken_Returns200()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.EnRoute);

        var response = await _client.PostAsJsonAsync("/api/rides/location", new
        {
            TrackingToken = token,
            Latitude = 42.3314,
            Longitude = -83.0458
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task UpdateLocation_InvalidCoords_Returns400()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.EnRoute);

        var response = await _client.PostAsJsonAsync("/api/rides/location", new
        {
            TrackingToken = token,
            Latitude = 999.0,
            Longitude = -83.0458
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"Expected 400 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task UpdateTrackingStatus_IssueReport_Returns200()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.EnRoute);

        var response = await _client.PostAsJsonAsync("/api/rides/track-status", new
        {
            TrackingToken = token,
            NewStatus = "Issue"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Issue reported");
    }

    [Fact]
    public async Task UpdateTrackingStatus_ValidAdvance_Returns200()
    {
        // Seed ride in Confirmed status — next tracking status is EnRoute
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var token = Guid.NewGuid().ToString("N");
        await SeedRideWithTokenAsync(orgId, facilityId, token, RideStatus.Confirmed);

        var response = await _client.PostAsJsonAsync("/api/rides/track-status", new
        {
            TrackingToken = token,
            NewStatus = "EnRoute"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("EnRoute");
    }

    // ── Accept / Decline via tracking link ──────────────────────────────────────

    [Fact]
    public async Task GetTrackingPage_PendingOfferToken_ReturnsAcceptPageHtml()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendorId = await SeedVendorAsync(facilityId);
        var offerToken = Guid.NewGuid().ToString("N");
        await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendorId, offerToken);

        var response = await _client.GetAsync($"/track/{offerToken}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Accept This Ride");
    }

    [Fact]
    public async Task GetTrackingPage_SupersededOfferToken_ReturnsAlreadyTakenHtml()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendorId = await SeedVendorAsync(facilityId);
        var offerToken = Guid.NewGuid().ToString("N");
        await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendorId, offerToken, offerStatus: "Superseded");

        var response = await _client.GetAsync($"/track/{offerToken}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Already Taken");
    }

    [Fact]
    public async Task TrackAccept_ValidPendingOffer_ClaimsRideAndActivatesSameToken()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendorId = await SeedVendorAsync(facilityId);
        var offerToken = Guid.NewGuid().ToString("N");
        var (rideId, _) = await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendorId, offerToken);

        var response = await _client.PostAsJsonAsync("/api/rides/track-accept", new { TrackingToken = offerToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ride = await db.Rides.FindAsync(rideId);
        ride!.Status.Should().Be(RideStatus.Confirmed);
        ride.VendorId.Should().Be(vendorId);
        // Same link keeps working post-acceptance — the offer's token becomes the ride's own.
        ride.TrackingToken.Should().Be(offerToken);

        var reloadResponse = await _client.GetAsync($"/track/{offerToken}");
        var reloadBody = await reloadResponse.Content.ReadAsStringAsync();
        reloadBody.Should().NotContain("Accept This Ride");
    }

    [Fact]
    public async Task TrackAccept_RideAlreadyClaimedBySomeoneElse_ReturnsBadRequest()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendorId = await SeedVendorAsync(facilityId);
        var offerToken = Guid.NewGuid().ToString("N");
        var (rideId, _) = await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendorId, offerToken);

        // Simulate another vendor having already claimed the ride first.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ride = await db.Rides.FindAsync(rideId);
            ride!.Status = RideStatus.Confirmed;
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/rides/track-accept", new { TrackingToken = offerToken });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TrackAccept_UnknownToken_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/rides/track-accept", new { TrackingToken = "no-such-token" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TrackDecline_LastPendingOffer_CancelsRide()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendorId = await SeedVendorAsync(facilityId);
        var offerToken = Guid.NewGuid().ToString("N");
        var (rideId, _) = await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendorId, offerToken);

        var response = await _client.PostAsJsonAsync("/api/rides/track-decline", new { TrackingToken = offerToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ride = await db.Rides.FindAsync(rideId);
        ride!.Status.Should().Be(RideStatus.Cancelled);
    }

    [Fact]
    public async Task TrackDecline_OtherOffersStillPending_RideStaysDispatched()
    {
        var (orgId, facilityId) = await SeedOrgAndFacilityAsync();
        var vendor1 = await SeedVendorAsync(facilityId);
        var vendor2 = await SeedVendorAsync(facilityId);
        var offerToken1 = Guid.NewGuid().ToString("N");
        var (rideId, _) = await SeedDispatchedRideWithOfferAsync(orgId, facilityId, vendor1, offerToken1);

        // Second vendor's offer on the same ride.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RideDispatchOffers.Add(new RideDispatchOffer
            {
                Id = Guid.NewGuid(),
                RideId = rideId,
                VendorId = vendor2,
                Status = "Pending",
                TrackingToken = Guid.NewGuid().ToString("N")
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/rides/track-decline", new { TrackingToken = offerToken1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ride = await verifyDb.Rides.FindAsync(rideId);
        ride!.Status.Should().Be(RideStatus.Dispatched);
    }
}
