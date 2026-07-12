using System.Security.Cryptography;
using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KinCare.API.Endpoints;

public static class OnboardingEndpoints
{
    public static void MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding").WithTags("Onboarding");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/invite", Invite).RequireAuthorization();
        group.MapGet("/invite/{token}", GetInvite).AllowAnonymous();
        group.MapPost("/accept", AcceptInvite).AllowAnonymous();
        group.MapGet("/lookup-facility", LookupFacility).AllowAnonymous();
    }

    // Returns matching org+facility for FacilityAdmin self-registration preview
    private static async Task<IResult> LookupFacility(
        string organizationName,
        string facilityName,
        string facilityAddress,
        AppDbContext db)
    {
        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Name.ToLower() == organizationName.ToLower() && o.IsActive);

        if (org is null)
            return Results.NotFound(new { error = "Organization not found." });

        var facility = await db.Facilities.AsNoTracking()
            .Where(f => f.OrganizationId == org.Id
                && f.IsActive
                && f.Name.ToLower() == facilityName.ToLower()
                && f.Address.ToLower().Contains(facilityAddress.ToLower().Substring(0, Math.Min(facilityAddress.Length, 10))))
            .FirstOrDefaultAsync();

        if (facility is null)
            return Results.NotFound(new { error = "Facility not found. Please check the name and address." });

        return Results.Ok(new { organizationId = org.Id, facilityId = facility.Id, facilityName = facility.Name, organizationName = org.Name });
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AppDbContext db,
        UserManager<AppUser> userManager,
        TokenService tokenService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        // FacilityAdmin: look up existing org + facility by name
        if (request.Role == UserRole.FacilityAdmin)
        {
            var existingOrg = await db.Organizations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Name.ToLower() == request.OrganizationName.ToLower() && o.IsActive);

            if (existingOrg is null)
                return Results.BadRequest(new { error = $"No active organization named \"{request.OrganizationName}\" was found. Check the exact name with your Org Admin (names are case-sensitive)." });

            var existingFacility = await db.Facilities.AsNoTracking()
                .Where(f => f.OrganizationId == existingOrg.Id
                    && f.IsActive
                    && f.Name.ToLower() == (request.FacilityName ?? "").ToLower())
                .FirstOrDefaultAsync();

            if (existingFacility is null)
                return Results.BadRequest(new { error = $"No active facility named \"{request.FacilityName}\" was found under \"{existingOrg.Name}\". Check the exact name with your Org Admin." });

            var facilityUser = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                OrganizationId = existingOrg.Id,
                FacilityId = existingFacility.Id,
                Role = UserRole.FacilityAdmin,
                IsActive = true
            };

            var facilityResult = await userManager.CreateAsync(facilityUser, request.Password);
            if (!facilityResult.Succeeded)
                return Results.ValidationProblem(facilityResult.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

            var facilityToken = tokenService.GenerateAccessToken(facilityUser);
            var facilityRefresh = await tokenService.GenerateRefreshTokenAsync(facilityUser.Id);

            return Results.Ok(new RegisterResponse(
                facilityToken, facilityRefresh.Token,
                existingOrg.Id, existingFacility.Id, facilityUser.Id));
        }

        // OrgAdmin: create new org + facility
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.OrganizationName,
            BillingEmail = request.Email,
            PlanTier = PlanTier.Starter,
            IsActive = true
        };

        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = request.FacilityName ?? $"{request.OrganizationName} - Main Branch",
            Address = request.FacilityAddress ?? ""
        };

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            OrganizationId = org.Id,
            FacilityId = facility.Id,
            Role = UserRole.OrgAdmin,
            IsActive = true
        };

        db.Organizations.Add(org);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

        var token = tokenService.GenerateAccessToken(user);
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id);

        return Results.Ok(new RegisterResponse(
            token, refreshToken.Token,
            org.Id, facility.Id, user.Id));
    }

    private static async Task<IResult> Invite(
        InviteRequest request,
        IValidator<InviteRequest> validator,
        HttpContext httpContext,
        AppDbContext db,
        EmailService emailService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();

        if (request.Role == UserRole.SuperAdmin)
            return Results.BadRequest(new { error = "Cannot assign SuperAdmin role." });

        string? facilityName = null;
        if (request.FacilityId.HasValue)
        {
            var facility = await db.Facilities.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FacilityId.Value);
            if (facility is null || facility.OrganizationId != tenant.OrganizationId)
                return Results.BadRequest(new { error = "Facility does not belong to your organization." });
            facilityName = facility.Name;
        }

        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == tenant.OrganizationId);

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = tenant.OrganizationId,
            FacilityId = request.FacilityId,
            Email = request.Email,
            Role = request.Role,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        _ = emailService.SendInvitationAsync(request.Email, org?.Name ?? "KinCare", facilityName, invitation.Token);

        return Results.Ok(new InviteResponse(invitation.Token, invitation.ExpiresAt));
    }

    private static async Task<IResult> GetInvite(
        string token,
        AppDbContext db)
    {
        var invitation = await db.Invitations
            .AsNoTracking()
            .Include(i => i.Organization)
            .Include(i => i.Facility)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invitation is null)
            return Results.NotFound(new { error = "Invitation not found." });

        if (invitation.AcceptedAt.HasValue)
            return Results.BadRequest(new { error = "Invitation already accepted." });

        if (invitation.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Invitation has expired." });

        return Results.Ok(new InviteDetailsResponse(
            invitation.Email,
            invitation.Role.ToString(),
            invitation.Organization.Name,
            invitation.Facility?.Name
        ));
    }

    private static async Task<IResult> AcceptInvite(
        AcceptInviteRequest request,
        IValidator<AcceptInviteRequest> validator,
        AppDbContext db,
        UserManager<AppUser> userManager,
        TokenService tokenService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Token == request.Token);

        if (invitation is null)
            return Results.NotFound(new { error = "Invitation not found." });

        if (invitation.AcceptedAt.HasValue)
            return Results.BadRequest(new { error = "Invitation already accepted." });

        if (invitation.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Invitation has expired." });

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = invitation.Email,
            Email = invitation.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            OrganizationId = invitation.OrganizationId,
            FacilityId = invitation.FacilityId,
            Role = invitation.Role,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

        invitation.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var token = tokenService.GenerateAccessToken(user);
        var refreshToken = await tokenService.GenerateRefreshTokenAsync(user.Id);

        return Results.Ok(new AcceptInviteResponse(token, refreshToken.Token, user.Id));
    }
}

public record RegisterRequest(
    string OrganizationName,
    string? FacilityName,
    string? FacilityAddress,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    UserRole Role = UserRole.OrgAdmin
);

public record RegisterResponse(
    string AccessToken,
    string RefreshToken,
    Guid OrganizationId,
    Guid FacilityId,
    Guid UserId
);

public record InviteRequest(
    string Email,
    Guid? FacilityId,
    UserRole Role
);

public record InviteResponse(string Token, DateTime ExpiresAt);

public record InviteDetailsResponse(
    string Email,
    string Role,
    string OrganizationName,
    string? FacilityName
);

public record AcceptInviteRequest(
    string Token,
    string FirstName,
    string LastName,
    string Password
);

public record AcceptInviteResponse(
    string AccessToken,
    string RefreshToken,
    Guid UserId
);
