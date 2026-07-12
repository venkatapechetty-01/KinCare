using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace KinCare.API.IntegrationTests;

public class AuthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "nonexistent@test.com",
            Password = "WrongPassword1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/onboarding/register", new
        {
            Email = "newuser@test.com",
            Password = "ValidPass123!@",
            FirstName = "Test",
            LastName = "User",
            OrganizationName = "Test Org",
            FacilityName = "Main Facility",
            FacilityAddress = "123 Test St"
        });

        response.IsSuccessStatusCode.Should().BeTrue(
            $"Expected success but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/residents");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
