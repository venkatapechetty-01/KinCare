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
}
