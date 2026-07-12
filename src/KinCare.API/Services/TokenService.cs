using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KinCare.API.Services;

public class TokenService
{
    private readonly JwtConfig _config;
    private readonly AppDbContext _db;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IOptions<JwtConfig> config, AppDbContext db, ILogger<TokenService> logger)
    {
        _config = config.Value;
        _db = db;
        _logger = logger;
    }

    public string GenerateAccessToken(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new("organization_id", user.OrganizationId.ToString()),
            new("role", user.Role.ToString()),
            new("first_name", user.FirstName),
            new("last_name", user.LastName),
        };

        if (user.FacilityId.HasValue)
            claims.Add(new Claim("facility_id", user.FacilityId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_config.AccessTokenExpiryMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();
        return token;
    }

    public async Task<(AppUser? user, RefreshToken? newToken)> RotateRefreshTokenAsync(string token)
    {
        var existing = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (existing is null)
            return (null, null);

        if (!existing.IsActive)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}. Revoking entire token family",
                existing.UserId);
            await RevokeTokenFamilyAsync(existing.UserId);
            return (null, null);
        }

        if (!existing.User.IsActive)
        {
            _logger.LogWarning("Refresh token attempt by deactivated user {UserId}", existing.UserId);
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (null, null);
        }

        existing.RevokedAt = DateTime.UtcNow;

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existing.UserId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(_config.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow
        };

        existing.ReplacedByToken = newToken.Token;
        _db.RefreshTokens.Add(newToken);
        await _db.SaveChangesAsync();

        return (existing.User, newToken);
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == token);
        if (existing is not null && existing.IsActive)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    private async Task RevokeTokenFamilyAsync(Guid userId)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var t in activeTokens)
            t.RevokedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}
