using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace KinCare.API.IntegrationTests;

public class RideEndpointTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;
    private Guid _organizationId;
    private Guid _facilityId;

    public RideEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        (_client, _organizationId, _facilityId) = await CreateAuthenticatedClientAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid OrganizationId, Guid FacilityId)> CreateAuthenticatedClientAsync(
        string? email = null)
    {
        var uniqueEmail = email ?? $"rider-{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/onboarding/register", new
        {
            Email = uniqueEmail,
            Password = "ValidPass123!@",
            FirstName = "Test",
            LastName = "Coordinator",
            OrganizationName = $"Org {Guid.NewGuid():N}",
            FacilityName = "Main Facility",
            FacilityAddress = "123 Test St, Detroit, MI"
        });

        registerResponse.IsSuccessStatusCode.Should().BeTrue(
            $"Registration failed: {await registerResponse.Content.ReadAsStringAsync()}");

        var registerBody = await registerResponse.Content.ReadFromJsonAsync<RegisterBody>();
        registerBody.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registerBody!.AccessToken);

        return (client, registerBody.OrganizationId, registerBody.FacilityId);
    }

    private static DateTime FuturePickupTime() =>
        DateTime.UtcNow.AddHours(2);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodaysRides_Authenticated_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/rides/today");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("[]");
    }

    [Fact]
    public async Task BookRide_ValidRequest_Returns201()
    {
        var response = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = FuturePickupTime().ToString("O"),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Expected 201 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadFromJsonAsync<RideCreatedBody>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BookRide_PastPickupTime_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = DateTime.UtcNow.AddHours(-1).ToString("O"),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"Expected 400 but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task GetRideDetail_ValidId_ReturnsRide()
    {
        // Book a ride first
        var bookResponse = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = FuturePickupTime().ToString("O"),
            PickupAddress = "100 First Ave, Detroit, MI",
            DestinationAddress = "200 Second Ave, Detroit, MI"
        });
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookBody = await bookResponse.Content.ReadFromJsonAsync<RideCreatedBody>();
        bookBody.Should().NotBeNull();

        // Fetch the detail
        var detailResponse = await _client.GetAsync($"/api/rides/{bookBody!.Id}");

        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {detailResponse.StatusCode}: {await detailResponse.Content.ReadAsStringAsync()}");

        var body = await detailResponse.Content.ReadAsStringAsync();
        body.Should().Contain(bookBody.Id.ToString());
    }

    [Fact]
    public async Task GetRideDetail_WrongOrg_Returns404()
    {
        // Create a second client for a different org — one registration per cross-tenant test
        var (clientB, _, _) = await CreateAuthenticatedClientAsync();

        // Shared client books a ride
        var bookResponse = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = FuturePickupTime().ToString("O"),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        });
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookBody = await bookResponse.Content.ReadFromJsonAsync<RideCreatedBody>();
        bookBody.Should().NotBeNull();

        // Client B (different org) tries to access it
        var detailResponse = await clientB.GetAsync($"/api/rides/{bookBody!.Id}");

        detailResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdvanceStatus_ValidTransition_Returns200()
    {
        // Book a ride — it starts in Dispatched status
        var bookResponse = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = FuturePickupTime().ToString("O"),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        });
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookBody = await bookResponse.Content.ReadFromJsonAsync<RideCreatedBody>();
        bookBody.Should().NotBeNull();

        // Advance directly via coordinator endpoint (Dispatched → Confirmed)
        var advanceResponse = await _client.PutAsJsonAsync($"/api/rides/{bookBody!.Id}/status", new
        {
            NewStatus = "Confirmed",
            Notes = "Test advance"
        });

        advanceResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {advanceResponse.StatusCode}: {await advanceResponse.Content.ReadAsStringAsync()}");

        var body = await advanceResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Confirmed");
    }

    [Fact]
    public async Task CancelRide_Returns200()
    {
        var bookResponse = await _client.PostAsJsonAsync("/api/rides/", new
        {
            PickupTime = FuturePickupTime().ToString("O"),
            PickupAddress = "123 Main St, Detroit, MI",
            DestinationAddress = "456 Oak Ave, Detroit, MI"
        });
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookBody = await bookResponse.Content.ReadFromJsonAsync<RideCreatedBody>();
        bookBody.Should().NotBeNull();

        var cancelResponse = await _client.DeleteAsync($"/api/rides/{bookBody!.Id}");

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Expected 200 but got {cancelResponse.StatusCode}: {await cancelResponse.Content.ReadAsStringAsync()}");

        var body = await cancelResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Cancelled");
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private record RegisterBody(string AccessToken, string RefreshToken, Guid OrganizationId, Guid FacilityId, Guid UserId);
    private record RideCreatedBody(Guid Id, string Status, string DispatchChannel, Guid? VendorId);
}
