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
        group.MapDelete("/", DeleteOwnOrganization);
    }

    // Permanently deletes the caller's OWN organization and everything under it
    // (facilities, residents, vendors, rides, users, invitations). Deliberately takes no
    // organization id — an OrgAdmin can only ever nuke their own tenant, never another
    // org's, no matter what. Mainly here so test/onboarding orgs can be cleaned up via
    // the API instead of direct database access.
    //
    // Every entity is deleted explicitly, in dependency order, rather than relying on the
    // OnDelete(Cascade) config in AppDbContext to do it — real Postgres does enforce those
    // cascades correctly, but this is a destructive, irreversible operation, so it's worth
    // not just trusting FK config to be right. It also means this is fully verifiable by an
    // integration test: EF Core's InMemory provider (used in tests) does NOT actually
    // execute configured cascade deletes the way a real relational DB does, so a version of
    // this that relied on cascades would silently leave orphaned rows under test while
    // appearing to pass — which is exactly what happened while writing this.
    private static async Task<IResult> DeleteOwnOrganization(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();
        var orgId = tenant.OrganizationId;

        await using var tx = await db.Database.BeginTransactionAsync();

        var facilityIds = await db.Facilities.Where(f => f.OrganizationId == orgId).Select(f => f.Id).ToListAsync();
        var rideIds = await db.Rides.Where(r => r.OrganizationId == orgId).Select(r => r.Id).ToListAsync();
        var userIds = await db.Users.Where(u => u.OrganizationId == orgId).Select(u => u.Id).ToListAsync();

        db.RideEvents.RemoveRange(await db.RideEvents.Where(e => rideIds.Contains(e.RideId)).ToListAsync());
        db.RideDispatchOffers.RemoveRange(await db.RideDispatchOffers.Where(o => rideIds.Contains(o.RideId)).ToListAsync());
        db.Rides.RemoveRange(await db.Rides.Where(r => r.OrganizationId == orgId).ToListAsync());
        db.Residents.RemoveRange(await db.Residents.Where(r => facilityIds.Contains(r.FacilityId)).ToListAsync());
        db.Vendors.RemoveRange(await db.Vendors.Where(v => facilityIds.Contains(v.FacilityId)).ToListAsync());
        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => userIds.Contains(t.UserId)).ToListAsync());
        db.DeviceRegistrations.RemoveRange(await db.DeviceRegistrations.Where(d => userIds.Contains(d.UserId)).ToListAsync());
        db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(p => userIds.Contains(p.UserId)).ToListAsync());
        db.Users.RemoveRange(await db.Users.Where(u => u.OrganizationId == orgId).ToListAsync());
        db.Invitations.RemoveRange(await db.Invitations.Where(i => i.OrganizationId == orgId).ToListAsync());
        db.Facilities.RemoveRange(await db.Facilities.Where(f => f.OrganizationId == orgId).ToListAsync());
        await db.SaveChangesAsync();

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId);
        if (org is null)
            return Results.NotFound();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
        return Results.NoContent();
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

        return Results.Created($"/api/org/facilities/{facility.Id}",
            new FacilityDto(facility.Id, facility.Name, facility.Address, facility.Timezone, 0));
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
                r.VendorId,
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

        var facilityCount = await db.Facilities
            .AsNoTracking()
            .CountAsync(f => f.OrganizationId == tenant.OrganizationId && f.IsActive);

        var recentRides = db.Rides.AsNoTracking()
            .Where(r => r.Facility.OrganizationId == tenant.OrganizationId && r.CreatedAt >= thirtyDaysAgo);

        var ridesThisMonth = await recentRides.CountAsync();
        var completedRides = await recentRides.CountAsync(r => r.Status == RideStatus.Completed);
        var completionRate = ridesThisMonth == 0 ? 0 : Math.Round(100.0 * completedRides / ridesThisMonth, 1);

        // "Response time" = how long a ride sat waiting before a vendor accepted it
        // (Dispatched → Confirmed), averaged across the same 30-day window.
        var responsePairs = await db.RideEvents.AsNoTracking()
            .Where(e => e.ToStatus == RideStatus.Confirmed
                && e.Ride.Facility.OrganizationId == tenant.OrganizationId
                && e.Ride.CreatedAt >= thirtyDaysAgo)
            .Select(e => new { e.Ride.CreatedAt, e.OccurredAt })
            .ToListAsync();
        var avgResponseMinutes = responsePairs.Count == 0
            ? 0
            : Math.Round(responsePairs.Average(p => (p.OccurredAt - p.CreatedAt).TotalMinutes), 1);

        var topVendor = await db.Rides.AsNoTracking()
            .Where(r => r.Facility.OrganizationId == tenant.OrganizationId
                && r.CreatedAt >= thirtyDaysAgo
                && r.Status == RideStatus.Completed
                && r.VendorId != null)
            .GroupBy(r => r.Vendor!.Name)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefaultAsync();

        return Results.Ok(new OrgMetricsDto(facilityCount, ridesThisMonth, completionRate, avgResponseMinutes, topVendor));
    }
}

public record FacilityDto(
    Guid Id, string Name, string Address, string Timezone, int ActiveRides);

public record CreateFacilityRequest(
    string Name, string Address, string? Timezone);

public record OrgUserDto(
    Guid Id, string FirstName, string LastName, string Email,
    string Role, Guid? FacilityId, string? FacilityName, bool IsActive);

public record OrgMetricsDto(
    int FacilityCount, int RidesThisMonth, double CompletionRate,
    double AvgResponseMinutes, string? TopVendor);

public record ResidentSummaryDto(
    Guid Id, Guid FacilityId, string FirstName, string LastName,
    bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
    string? DriverNotes);

public record ActiveRideLocationDto(
    Guid Id, string FacilityName, string ResidentName,
    Guid? VendorId, string? VendorName, string? VendorPhone, string? VendorPhotoUrl,
    string Status, string DispatchChannel,
    string PickupAddress, string DestinationAddress, DateTime PickupTime,
    double Lat, double Lng, DateTime? LastLocationAt);
