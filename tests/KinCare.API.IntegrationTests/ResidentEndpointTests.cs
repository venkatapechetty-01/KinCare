using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace KinCare.API.IntegrationTests;

public class ResidentEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public ResidentEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = await CreateAuthenticatedClientAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var uniqueEmail = $"resident-test-{Guid.NewGuid():N}@test.com";
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

        return client;
    }

    // Regression coverage: Create used to return only `{ id }` instead of the full record.

    [Fact]
    public async Task CreateResident_ReturnsFullResidentDto()
    {
        var response = await _client.PostAsJsonAsync("/api/residents", new
        {
            FirstName = "Alice",
            LastName = "Resident",
            NeedsWheelchair = true,
            NeedsOxygen = true,
            NeedsStretcher = false,
            NeedsWalker = false,
            DriverNotes = "Needs extra time"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Expected 201 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<ResidentDtoBody>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.FirstName.Should().Be("Alice");
        body.LastName.Should().Be("Resident");
        body.NeedsWheelchair.Should().BeTrue();
        body.NeedsOxygen.Should().BeTrue();
        body.NeedsStretcher.Should().BeFalse();
        body.DriverNotes.Should().Be("Needs extra time");
    }

    private record RegisterBody(string AccessToken, string RefreshToken, Guid OrganizationId, Guid FacilityId, Guid UserId);

    private record ResidentDtoBody(
        Guid Id, Guid FacilityId, string FirstName, string LastName,
        bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
        string? DriverNotes);
}
