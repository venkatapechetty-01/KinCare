using System.Security.Cryptography;
using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class OrgAdminEndpoints
{
    public static void MapOrgAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/org").WithTags("OrgAdmin").RequireAuthorization();

        group.MapGet("/facilities", GetFacilities);
        group.MapGet("/facilities/{id:guid}", GetFacility);
        group.MapPost("/facilities", CreateFacility);
        group.MapGet("/facilities/{id:guid}/residents", GetFacilityResidents);
        group.MapGet("/users", GetUsers);
        group.MapPost("/invite", InviteUser);
        group.MapDelete("/users/{id:guid}", DeactivateUser);
        group.MapGet("/metrics", GetMetrics);
        group.MapGet("/my-facility", GetMyFacility);
        group.MapGet("/live-map", GetLiveMap);
    }

    private static async Task<IResult> GetMyFacility(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        Guid? facilityId = tenant.FacilityId;
        if (facilityId is null && tenant.Role == UserRole.OrgAdmin)
        {
            facilityId = await db.Facilities
                .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
                .Select(f => (Guid?)f.Id)
                .FirstOrDefaultAsync();
        }
        if (facilityId is null) return Results.NotFound();

        var facility = await db.Facilities.AsNoTracking()
            .Where(f => f.Id == facilityId.Value && f.OrganizationId == tenant.OrganizationId)
            .Select(f => new { f.Id, f.Name, f.Address, f.Timezone })
            .FirstOrDefaultAsync();

        return facility is null ? Results.NotFound() : Results.Ok(facility);
    }

    private static async Task<IResult> GetFacilities(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin && tenant.Role != UserRole.SuperAdmin)
            return Results.Forbid();

        var facilities = await db.Facilities
            .AsNoTracking()
            .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
            .Select(f => new FacilityDto(
                f.Id, f.Name, f.Address, f.Timezone,
                f.Rides.Count(r => r.Status != RideStatus.Completed && r.Status != RideStatus.Cancelled)))
            .ToListAsync();

        return Results.Ok(facilities);
    }

    private static async Task<IResult> CreateFacility(
        CreateFacilityRequest request,
        IValidator<CreateFacilityRequest> validator,
        HttpContext httpContext,
        AppDbContext db)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();

        var facility = new Facility
        {
            Id = Guid.NewGuid(),
            OrganizationId = tenant.OrganizationId,
            Name = request.Name,
            Address = request.Address,
            Timezone = request.Timezone ?? "America/New_York"
        };

        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        return Results.Created($"/api/org/facilities/{facility.Id}", new { id = facility.Id });
    }

    private static async Task<IResult> GetUsers(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin && tenant.Role != UserRole.SuperAdmin)
            return Results.Forbid();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == tenant.OrganizationId)
            .Select(u => new OrgUserDto(
                u.Id, u.FirstName, u.LastName, u.Email!,
                u.Role.ToString(), u.FacilityId,
                u.Facility != null ? u.Facility.Name : null,
                u.IsActive))
            .ToListAsync();

        return Results.Ok(users);
    }

    private static async Task<IResult> InviteUser(
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

        if (request.Role == UserRole.SuperAdmin || request.Role == UserRole.OrgAdmin)
            return Results.BadRequest(new { error = "Cannot assign SuperAdmin or OrgAdmin role via invite." });

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

    private static async Task<IResult> DeactivateUser(
        Guid id,
        HttpContext httpContext,
        UserManager<AppUser> userManager)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.OrganizationId != tenant.OrganizationId)
            return Results.NotFound();

        user.IsActive = false;
        await userManager.UpdateAsync(user);

        return Results.NoContent();
    }

    private static async Task<IResult> GetFacility(
        Guid id,
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin && tenant.Role != UserRole.SuperAdmin)
            return Results.Forbid();

        var facility = await db.Facilities.AsNoTracking()
            .Where(f => f.Id == id && f.OrganizationId == tenant.OrganizationId && f.IsActive)
            .Select(f => new FacilityDto(
                f.Id, f.Name, f.Address, f.Timezone,
                f.Rides.Count(r => r.Status != RideStatus.Completed && r.Status != RideStatus.Cancelled)))
            .FirstOrDefaultAsync();

        if (facility is null) return Results.NotFound();
        return Results.Ok(facility);
    }

    private static async Task<IResult> GetFacilityResidents(
        Guid id,
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin && tenant.Role != UserRole.SuperAdmin)
            return Results.Forbid();

        var facilityExists = await db.Facilities.AsNoTracking()
            .AnyAsync(f => f.Id == id && f.OrganizationId == tenant.OrganizationId && f.IsActive);

        if (!facilityExists) return Results.NotFound();

        var residents = await db.Residents.AsNoTracking()
            .Where(r => r.FacilityId == id && r.IsActive)
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .Select(r => new ResidentSummaryDto(
                r.Id, r.FacilityId, r.FirstName, r.LastName,
                r.NeedsWheelchair, r.NeedsOxygen, r.NeedsStretcher, r.NeedsWalker,
                r.DriverNotes))
            .ToListAsync();

        return Results.Ok(residents);
    }

    private static async Task<IResult> GetLiveMap(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();

        IQueryable<Ride> query = db.Rides.AsNoTracking()
            .Include(r => r.Resident)
            .Include(r => r.Vendor)
            .Include(r => r.Facility);

        if (tenant.Role == UserRole.OrgAdmin || tenant.Role == UserRole.SuperAdmin)
            query = query.Where(r => r.Facility.OrganizationId == tenant.OrganizationId);
        else if (tenant.FacilityId.HasValue)
            query = query.Where(r => r.FacilityId == tenant.FacilityId.Value);
        else
            return Results.BadRequest(new { error = "Facility context required." });

        var activeRides = await query
            .Where(r =>
                r.LastKnownLat != null && r.LastKnownLng != null &&
                r.TrackingToken != null &&
                r.Status != RideStatus.Completed && r.Status != RideStatus.Cancelled)
            .Select(r => new ActiveRideLocationDto(
                r.Id,
                r.Facility.Name,
                r.Resident != null ? r.Resident.FirstName + " " + r.Resident.LastName : "Unknown",
                r.Vendor != null ? r.Vendor.Name : null,
                r.Vendor != null ? r.Vendor.PhoneNumber : null,
                r.Vendor != null ? r.Vendor.PhotoUrl : null,
                r.Status.ToString(),
                r.DispatchChannel.ToString(),
                r.PickupAddress,
                r.DestinationAddress,
                r.PickupTime,
                r.LastKnownLat!.Value,
                r.LastKnownLng!.Value,
                r.LastLocationAt))
            .ToListAsync();

        return Results.Ok(activeRides);
    }

    private static async Task<IResult> GetMetrics(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin && tenant.Role != UserRole.SuperAdmin)
            return Results.Forbid();

        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var metrics = await db.Facilities
            .AsNoTracking()
            .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
            .Select(f => new FacilityMetricsDto(
                f.Id,
                f.Name,
                f.Rides.Count(r => r.CreatedAt >= thirtyDaysAgo),
                f.Rides.Count(r => r.CreatedAt >= thirtyDaysAgo && r.Status == RideStatus.Completed),
                f.Rides.Count(r => r.CreatedAt >= thirtyDaysAgo && r.Status == RideStatus.Cancelled)))
            .ToListAsync();

        return Results.Ok(metrics);
    }
}

public record FacilityDto(
    Guid Id, string Name, string Address, string Timezone, int ActiveRides);

public record CreateFacilityRequest(
    string Name, string Address, string? Timezone);

public record OrgUserDto(
    Guid Id, string FirstName, string LastName, string Email,
    string Role, Guid? FacilityId, string? FacilityName, bool IsActive);

public record FacilityMetricsDto(
    Guid FacilityId, string FacilityName,
    int TotalRides, int CompletedRides, int CancelledRides);

public record ResidentSummaryDto(
    Guid Id, Guid FacilityId, string FirstName, string LastName,
    bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
    string? DriverNotes);

public record ActiveRideLocationDto(
    Guid Id, string FacilityName, string ResidentName,
    string? VendorName, string? VendorPhone, string? VendorPhotoUrl,
    string Status, string DispatchChannel,
    string PickupAddress, string DestinationAddress, DateTime PickupTime,
    double Lat, double Lng, DateTime? LastLocationAt);
