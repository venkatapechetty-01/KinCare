using System.Security.Cryptography;
using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        group.MapPost("/request-password-reset", RequestPasswordReset);
        group.MapPost("/reset-password", ResetPassword);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        UserManager<AppUser> userManager,
        TokenService tokenService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

        if (await userManager.IsLockedOutAsync(user))
            return Results.Json(new { error = "Account is temporarily locked. Try again later." }, statusCode: 429);

        var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id);

        return Results.Ok(new LoginResponse(
            accessToken,
            refreshToken.Token,
            user.Role.ToString(),
            user.OrganizationId,
            user.FacilityId
        ));
    }

    private static async Task<IResult> Refresh(
        RefreshRequest request,
        TokenService tokenService)
    {
        var (user, newToken) = await tokenService.RotateRefreshTokenAsync(request.RefreshToken);
        if (user is null || newToken is null)
            return Results.Unauthorized();

        var accessToken = tokenService.GenerateAccessToken(user);
        return Results.Ok(new RefreshResponse(accessToken, newToken.Token));
    }

    private static async Task<IResult> Logout(
        LogoutRequest request,
        TokenService tokenService)
    {
        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken);
        return Results.Ok();
    }

    private static async Task<IResult> RequestPasswordReset(
        RequestPasswordResetRequest request,
        IValidator<RequestPasswordResetRequest> validator,
        UserManager<AppUser> userManager,
        AppDbContext db,
        EmailService emailService,
        ILoggerFactory loggerFactory)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        // Always return 200 to prevent email enumeration
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Results.Ok(new { message = "If that email exists you will receive a reset link." });

        // Invalidate any existing active tokens for this user
        var existing = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in existing)
            t.UsedAt = DateTime.UtcNow;

        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
                .Replace("+", "-").Replace("/", "_").Replace("=", ""),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow
        };

        db.PasswordResetTokens.Add(resetToken);
        await db.SaveChangesAsync();

        var logger = loggerFactory.CreateLogger("Auth.PasswordReset");
        _ = emailService.SendPasswordResetAsync(user.Email!, resetToken.Token)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                    logger.LogError(t.Exception, "Failed to send password reset email to {Email}", user.Email);
            });

        return Results.Ok(new { message = "If that email exists you will receive a reset link." });
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest request,
        IValidator<ResetPasswordRequest> validator,
        UserManager<AppUser> userManager,
        AppDbContext db,
        TokenService tokenService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var resetToken = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.Token);

        if (resetToken is null || !resetToken.IsValid)
            return Results.Json(new { error = "Reset token is invalid or has expired." }, statusCode: 400);

        var result = await userManager.RemovePasswordAsync(resetToken.User);
        if (!result.Succeeded)
            return Results.Json(new { error = "Password reset failed." }, statusCode: 400);

        result = await userManager.AddPasswordAsync(resetToken.User, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description);
            return Results.Json(new { error = string.Join(" ", errors) }, statusCode: 400);
        }

        // Mark token as used and revoke all refresh tokens
        resetToken.UsedAt = DateTime.UtcNow;
        var activeRefreshTokens = await db.RefreshTokens
            .Where(rt => rt.UserId == resetToken.UserId && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var rt in activeRefreshTokens)
            rt.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Results.Ok(new { message = "Password has been reset. Please sign in with your new password." });
    }
}

public record LoginRequest(string Email, string Password);
public record RequestPasswordResetRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    string Role,
    Guid OrganizationId,
    Guid? FacilityId
);

public record RefreshRequest(string RefreshToken);
public record RefreshResponse(string AccessToken, string RefreshToken);
public record LogoutRequest(string RefreshToken);
