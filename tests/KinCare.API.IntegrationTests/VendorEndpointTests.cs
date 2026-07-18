using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace KinCare.API.IntegrationTests;

public class VendorEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public VendorEndpointTests(CustomWebApplicationFactory factory)
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
        var uniqueEmail = $"vendor-test-{Guid.NewGuid():N}@test.com";
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

    // ── Create returns the full DTO, not just {id} ──────────────────────────────
    // Regression coverage: Create used to return only `{ id }`, so any caller expecting
    // the created record back (this test, the e2e suite, and — before this session — the
    // real Angular UI after adding a driver) got `undefined` for every other field.

    [Fact]
    public async Task CreateVendor_ReturnsFullVendorDto()
    {
        var response = await _client.PostAsJsonAsync("/api/vendors", new
        {
            Name = "Metro Taxi Driver",
            PhoneNumber = "+15125551111",
            VendorType = "Ambulatory",
            DispatchMethod = "SmsTaxi",
            CapabilityTier = "Basic",
            Company = "Metro Taxi Co",
            ServiceArea = "Detroit Metro"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Expected 201 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<VendorDtoBody>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("Metro Taxi Driver");
        body.PhoneNumber.Should().Be("+15125551111");
        body.VendorType.Should().Be("Ambulatory");
        body.DispatchMethod.Should().Be("SmsTaxi");
        body.CapabilityTier.Should().Be("Basic");
        body.IsActive.Should().BeTrue();
        body.Company.Should().Be("Metro Taxi Co");
        body.ServiceArea.Should().Be("Detroit Metro");
    }

    [Fact]
    public async Task CreateVendor_WithoutCompanyOrServiceArea_SucceedsWithNulls()
    {
        var response = await _client.PostAsJsonAsync("/api/vendors", new
        {
            Name = "No Company Driver",
            PhoneNumber = "+15125552222",
            VendorType = "Ambulatory",
            DispatchMethod = "SmsTaxi",
            CapabilityTier = "Basic"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<VendorDtoBody>();
        body!.Company.Should().BeNull();
        body.ServiceArea.Should().BeNull();
    }

    [Fact]
    public async Task UpdateVendor_UpdatesCompanyAndServiceArea()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/vendors", new
        {
            Name = "Original Name",
            PhoneNumber = "+15125553333",
            VendorType = "Ambulatory",
            DispatchMethod = "SmsTaxi",
            CapabilityTier = "Basic"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<VendorDtoBody>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/vendors/{created!.Id}", new
        {
            Name = "Updated Name",
            PhoneNumber = "+15125553333",
            VendorType = "Ambulatory",
            DispatchMethod = "SmsTaxi",
            CapabilityTier = "Smart",
            Company = "Riverside NEMT",
            ServiceArea = "Southfield MI"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _client.GetAsync("/api/vendors");
        var list = await listResponse.Content.ReadFromJsonAsync<List<VendorDtoBody>>();
        var updated = list!.Single(v => v.Id == created.Id);
        updated.Name.Should().Be("Updated Name");
        updated.CapabilityTier.Should().Be("Smart");
        updated.Company.Should().Be("Riverside NEMT");
        updated.ServiceArea.Should().Be("Southfield MI");
    }

    private record RegisterBody(string AccessToken, string RefreshToken, Guid OrganizationId, Guid FacilityId, Guid UserId);

    private record VendorDtoBody(
        Guid Id, Guid FacilityId, string Name, string PhoneNumber,
        string VendorType, string DispatchMethod, string CapabilityTier, bool IsActive, string? PhotoUrl,
        string? Company, string? ServiceArea);
}
