using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KinCare.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KinCare.API.IntegrationTests;

public class OrgAdminEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;
    private Guid _facilityId;

    public OrgAdminEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        (_client, _facilityId) = await CreateAuthenticatedClientAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, Guid FacilityId)> CreateAuthenticatedClientAsync()
    {
        var uniqueEmail = $"org-test-{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/onboarding/register", new
        {
            Email = uniqueEmail,
            Password = "ValidPass123!@",
            FirstName = "Test",
            LastName = "Admin",
            OrganizationName = $"Org {Guid.NewGuid():N}",
            FacilityName = "Main Facility",
            FacilityAddress = "123 Test St, Detroit, MI"
        });

        registerResponse.IsSuccessStatusCode.Should().BeTrue(
            $"Registration failed: {await registerResponse.Content.ReadAsStringAsync()}");

        var registerBody = await registerResponse.Content.ReadFromJsonAsync<RegisterBody>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registerBody!.AccessToken);

        return (client, registerBody.FacilityId);
    }

    // Regression coverage: CreateFacility used to return only `{ id }` instead of the full record.

    [Fact]
    public async Task CreateFacility_ReturnsFullFacilityDto()
    {
        var response = await _client.PostAsJsonAsync("/api/org/facilities", new
        {
            Name = "Second Branch",
            Address = "500 Branch Ave, Austin TX",
            Timezone = "America/Chicago"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Expected 201 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<FacilityDtoBody>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("Second Branch");
        body.Address.Should().Be("500 Branch Ave, Austin TX");
        body.ActiveRides.Should().Be(0);
    }

    // Regression coverage: GetMetrics used to return a per-facility array; the Angular UI
    // (and this test) expect a single summary object with these exact numeric fields.

    [Fact]
    public async Task GetMetrics_ReturnsSummaryObjectShape_NotAnArray()
    {
        var response = await _client.GetAsync("/api/org/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OrgMetricsDtoBody>();
        body.Should().NotBeNull();
        body!.FacilityCount.Should().Be(1);
        body.RidesThisMonth.Should().Be(0);
        body.CompletionRate.Should().Be(0);
        body.AvgResponseMinutes.Should().Be(0);
        body.TopVendor.Should().BeNull();
    }

    // Regression coverage: the invite form/tests used to send the stale role name
    // "Coordinator", which no longer exists (renamed to FacilityAdmin) and was rejected.

    [Fact]
    public async Task InviteUser_WithFacilityAdminRole_Succeeds()
    {
        var response = await _client.PostAsJsonAsync("/api/org/invite", new
        {
            Email = $"invite-{Guid.NewGuid():N}@test.com",
            Role = "FacilityAdmin",
            FacilityId = _facilityId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task InviteUser_WithStaleCoordinatorRole_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/org/invite", new
        {
            Email = $"invite-{Guid.NewGuid():N}@test.com",
            Role = "Coordinator",
            FacilityId = _facilityId
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Regression coverage for the new self-service org-delete endpoint added so test/
    // onboarding orgs can be cleaned up via the API instead of direct DB access.
    // Ride.OrganizationId is a Restrict FK, so this exercises the trickiest part: rides
    // must be deleted before the organization or the whole delete fails outright.

    [Fact]
    public async Task DeleteOwnOrganization_WithRidesAndData_RemovesEverything()
    {
        var facilitiesRes = await _client.GetAsync("/api/org/facilities");
        var facilities = (await facilitiesRes.Content.ReadFromJsonAsync<List<FacilityDtoBody>>())!;
        var facilityId = facilities[0].Id;

        var residentRes = await _client.PostAsJsonAsync("/api/residents", new
        {
            FirstName = "ToDelete", LastName = "Resident",
            NeedsWheelchair = false, NeedsOxygen = false, NeedsStretcher = false, NeedsWalker = false
        });
        var residentId = (await residentRes.Content.ReadFromJsonAsync<IdBody>())!.Id;

        var vendorRes = await _client.PostAsJsonAsync("/api/vendors", new
        {
            Name = "ToDelete Vendor", PhoneNumber = "+15125550099",
            VendorType = "Ambulatory", DispatchMethod = "SmsTaxi", CapabilityTier = "Basic"
        });
        var vendorId = (await vendorRes.Content.ReadFromJsonAsync<IdBody>())!.Id;

        var rideRes = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = DateTime.UtcNow.AddHours(2).ToString("O"),
            PickupAddress = "1 Test St",
            DestinationAddress = "2 Test Ave"
        });
        rideRes.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Expected 201 but got {rideRes.StatusCode}: {await rideRes.Content.ReadAsStringAsync()}");
        var rideId = (await rideRes.Content.ReadFromJsonAsync<IdBody>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orgId = (await db.Facilities.AsNoTracking().FirstAsync(f => f.Id == facilityId)).OrganizationId;

        var deleteRes = await _client.DeleteAsync("/api/org");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"Expected 204 but got {deleteRes.StatusCode}: {await deleteRes.Content.ReadAsStringAsync()}");

        (await db.Organizations.AnyAsync(o => o.Id == orgId)).Should().BeFalse();
        (await db.Facilities.AnyAsync(f => f.Id == facilityId)).Should().BeFalse();
        (await db.Residents.AnyAsync(r => r.Id == residentId)).Should().BeFalse();
        (await db.Vendors.AnyAsync(v => v.Id == vendorId)).Should().BeFalse();
        (await db.Rides.AnyAsync(r => r.Id == rideId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOwnOrganization_DoesNotAffectOtherOrganizations()
    {
        var (otherClient, otherFacilityId) = await CreateAuthenticatedClientAsync();

        var deleteRes = await _client.DeleteAsync("/api/org");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The other org's data must be completely untouched by deleting this one.
        var getRes = await otherClient.GetAsync("/api/org/facilities");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var facilities = await getRes.Content.ReadFromJsonAsync<List<FacilityDtoBody>>();
        facilities!.Should().Contain(f => f.Id == otherFacilityId);
    }

    private record IdBody(Guid Id);

    private record RegisterBody(string AccessToken, string RefreshToken, Guid OrganizationId, Guid FacilityId, Guid UserId);

    private record FacilityDtoBody(Guid Id, string Name, string Address, string Timezone, int ActiveRides);

    private record OrgMetricsDtoBody(
        int FacilityCount, int RidesThisMonth, double CompletionRate,
        double AvgResponseMinutes, string? TopVendor);
}
