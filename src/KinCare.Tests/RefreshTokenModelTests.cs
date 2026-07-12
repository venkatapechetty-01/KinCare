using FluentAssertions;
using KinCare.API.Domain;

namespace KinCare.Tests;

public class RefreshTokenModelTests
{
    [Fact]
    public void IsExpired_FutureDate_ReturnsFalse()
    {
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(7) };
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_PastDate_ReturnsTrue()
    {
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(-1) };
        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsRevoked_NoRevokedDate_ReturnsFalse()
    {
        var token = new RefreshToken { RevokedAt = null };
        token.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void IsRevoked_WithRevokedDate_ReturnsTrue()
    {
        var token = new RefreshToken { RevokedAt = DateTime.UtcNow };
        token.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public void IsActive_NotExpiredNotRevoked_ReturnsTrue()
    {
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null
        };
        token.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_Expired_ReturnsFalse()
    {
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            RevokedAt = null
        };
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_Revoked_ReturnsFalse()
    {
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow
        };
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_ExpiredAndRevoked_ReturnsFalse()
    {
        var token = new RefreshToken
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            RevokedAt = DateTime.UtcNow
        };
        token.IsActive.Should().BeFalse();
    }
}
