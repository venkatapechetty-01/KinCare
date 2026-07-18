using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

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

    private record RegisterBody(string AccessToken, string RefreshToken, Guid OrganizationId, Guid FacilityId, Guid UserId);

    private record FacilityDtoBody(Guid Id, string Name, string Address, string Timezone, int ActiveRides);

    private record OrgMetricsDtoBody(
        int FacilityCount, int RidesThisMonth, double CompletionRate,
        double AvgResponseMinutes, string? TopVendor);
}
