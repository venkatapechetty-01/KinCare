using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", GetMe);
        group.MapPut("/me", UpdateProfile);
        group.MapPost("/me/change-password", ChangePassword);
        // IFormFile binding makes ASP.NET Core require an antiforgery token by default,
        // which assumes cookie-based sessions. This API is Bearer-token authenticated
        // (no ambient credentials a browser would auto-attach cross-site), so CSRF
        // protection isn't meaningful here — opt this endpoint out explicitly.
        group.MapPost("/me/photo", UploadPhoto).DisableAntiforgery();
        group.MapDelete("/me/photo", RemovePhoto);
    }

    private static async Task<IResult> GetMe(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == tenant.UserId)
            .Select(u => new UserProfileDto(
                u.Id, u.FirstName, u.LastName, u.Email!,
                u.Role.ToString(), u.OrganizationId, u.FacilityId,
                u.Facility != null ? u.Facility.Name : null,
                u.PhotoUrl))
            .FirstOrDefaultAsync();

        return user is null ? Results.NotFound() : Results.Ok(user);
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest request,
        IValidator<UpdateProfileRequest> validator,
        HttpContext httpContext,
        UserManager<AppUser> userManager)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        var user = await userManager.FindByIdAsync(tenant.UserId.ToString());
        if (user is null) return Results.NotFound();

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

        return Results.Ok(new { message = "Profile updated." });
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        IValidator<ChangePasswordRequest> validator,
        HttpContext httpContext,
        UserManager<AppUser> userManager)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        var user = await userManager.FindByIdAsync(tenant.UserId.ToString());
        if (user is null) return Results.NotFound();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

        return Results.Ok(new { message = "Password changed." });
    }

    private static async Task<IResult> UploadPhoto(
        IFormFile file,
        HttpContext httpContext,
        AppDbContext db)
    {
        if (file.Length == 0)
            return Results.BadRequest(new { error = "File is empty." });

        const long maxBytes = 5 * 1024 * 1024; // 5 MB
        if (file.Length > maxBytes)
            return Results.BadRequest(new { error = "File exceeds 5 MB limit." });

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(file.ContentType))
            return Results.BadRequest(new { error = "Only JPEG, PNG, or WebP images are accepted." });

        var tenant = httpContext.GetTenantContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId);
        if (user is null) return Results.NotFound();

        // Store as base64 data URL for portability (small profile photos only).
        // For production scale, swap this for an S3/GCS upload returning a CDN URL.
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        user.PhotoUrl = $"data:{file.ContentType};base64,{base64}";

        await db.SaveChangesAsync();
        return Results.Ok(new { photoUrl = user.PhotoUrl });
    }

    private static async Task<IResult> RemovePhoto(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == tenant.UserId);
        if (user is null) return Results.NotFound();

        user.PhotoUrl = null;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}

public record UserProfileDto(
    Guid Id, string FirstName, string LastName, string Email,
    string Role, Guid OrganizationId, Guid? FacilityId,
    string? FacilityName, string? PhotoUrl);

public record UpdateProfileRequest(string FirstName, string LastName);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
