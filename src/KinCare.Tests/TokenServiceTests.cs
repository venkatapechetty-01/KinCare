using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KinCare.Tests;

public class TokenServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TokenService _sut;
    private readonly JwtConfig _config;

    public TokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("TokenServiceTests_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        _config = new JwtConfig
        {
            SecretKey = "TestSecretKeyThatIs256BitsLong!1234567890",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpiryMinutes = 60,
            RefreshTokenExpiryDays = 7
        };

        _sut = new TokenService(Options.Create(_config), _db, NullLogger<TokenService>.Instance);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt()
    {
        var user = CreateTestUser();

        var token = _sut.GenerateAccessToken(user);

        token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be(_config.Issuer);
        jwt.Audiences.Should().Contain(_config.Audience);
    }

    [Fact]
    public void GenerateAccessToken_ContainsRequiredClaims()
    {
        var user = CreateTestUser();

        var token = _sut.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwt.Claims.Should().Contain(c => c.Type == "organization_id" && c.Value == user.OrganizationId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == user.Role.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "first_name" && c.Value == user.FirstName);
        jwt.Claims.Should().Contain(c => c.Type == "last_name" && c.Value == user.LastName);
    }

    [Fact]
    public void GenerateAccessToken_WithFacilityId_IncludesFacilityClaim()
    {
        var facilityId = Guid.NewGuid();
        var user = CreateTestUser();
        user.FacilityId = facilityId;

        var token = _sut.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "facility_id" && c.Value == facilityId.ToString());
    }

    [Fact]
    public void GenerateAccessToken_WithoutFacilityId_ExcludesFacilityClaim()
    {
        var user = CreateTestUser();
        user.FacilityId = null;

        var token = _sut.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "facility_id");
    }

    [Fact]
    public void GenerateAccessToken_ExpiresInConfiguredMinutes()
    {
        var user = CreateTestUser();

        var token = _sut.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.ValidTo.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(_config.AccessTokenExpiryMinutes),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_CreatesTokenInDatabase()
    {
        var userId = Guid.NewGuid();

        var refreshToken = await _sut.GenerateRefreshTokenAsync(userId);

        refreshToken.Token.Should().NotBeNullOrEmpty();
        refreshToken.UserId.Should().Be(userId);
        refreshToken.IsActive.Should().BeTrue();

        var stored = await _db.RefreshTokens.FirstAsync(rt => rt.Id == refreshToken.Id);
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_TokenExpiresInConfiguredDays()
    {
        var userId = Guid.NewGuid();

        var refreshToken = await _sut.GenerateRefreshTokenAsync(userId);

        refreshToken.ExpiresAt.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(_config.RefreshTokenExpiryDays),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ValidToken_ReturnsNewTokenAndRevokesOld()
    {
        var user = CreateTestUser();
        _db.Users.Add(user);
        var original = await _sut.GenerateRefreshTokenAsync(user.Id);

        var (returnedUser, newToken) = await _sut.RotateRefreshTokenAsync(original.Token);

        returnedUser.Should().NotBeNull();
        newToken.Should().NotBeNull();
        newToken!.Token.Should().NotBe(original.Token);

        var old = await _db.RefreshTokens.FirstAsync(rt => rt.Id == original.Id);
        old.IsRevoked.Should().BeTrue();
        old.ReplacedByToken.Should().Be(newToken.Token);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ExpiredToken_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var expired = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        };
        _db.RefreshTokens.Add(expired);
        await _db.SaveChangesAsync();

        var (user, newToken) = await _sut.RotateRefreshTokenAsync("expired-token");

        user.Should().BeNull();
        newToken.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokedToken_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        var revoked = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "revoked-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            RevokedAt = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(revoked);
        await _db.SaveChangesAsync();

        var (user, newToken) = await _sut.RotateRefreshTokenAsync("revoked-token");

        user.Should().BeNull();
        newToken.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_NonexistentToken_ReturnsNull()
    {
        var (user, newToken) = await _sut.RotateRefreshTokenAsync("does-not-exist");

        user.Should().BeNull();
        newToken.Should().BeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_ActiveToken_MarksAsRevoked()
    {
        var userId = Guid.NewGuid();
        var token = await _sut.GenerateRefreshTokenAsync(userId);

        await _sut.RevokeRefreshTokenAsync(token.Token);

        var stored = await _db.RefreshTokens.FirstAsync(rt => rt.Id == token.Id);
        stored.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_NonexistentToken_DoesNotThrow()
    {
        var act = async () => await _sut.RevokeRefreshTokenAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    private static AppUser CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@example.com",
        UserName = "test@example.com",
        FirstName = "Test",
        LastName = "User",
        OrganizationId = Guid.NewGuid(),
        FacilityId = Guid.NewGuid(),
        Role = UserRole.FacilityAdmin,
        IsActive = true
    };

    public void Dispose() => _db.Dispose();
}
