using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class RideEndpoints
{
    public static void MapRideEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides").WithTags("Rides").RequireAuthorization();

        group.MapGet("/today", GetToday);
        group.MapGet("/today/count", GetTodayCount);
        group.MapPost("/", BookRide);
        group.MapGet("/{id:guid}", GetDetail);
        group.MapPut("/{id:guid}/status", AdvanceStatus);
        group.MapDelete("/{id:guid}", CancelRide);
        group.MapPost("/{id:guid}/redispatch", Redispatch);
        group.MapGet("/{id:guid}/offers", GetDispatchOffers);
    }

    private static async Task<IResult> GetToday(
        HttpContext httpContext,
        AppDbContext db,
        RideService rideService)
    {
        var tenant = httpContext.GetTenantContext();

        // OrgAdmin: get rides from all facilities in the organization
        if (tenant.Role == UserRole.OrgAdmin && tenant.FacilityId is null)
        {
            var facilities = await db.Facilities.AsNoTracking()
                .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
                .ToListAsync();

            var allRides = new List<object>();
            foreach (var facility in facilities)
            {
                var rides = await rideService.GetTodaysRidesAsync(facility.Id, facility.Timezone);
                allRides.AddRange(rides);
            }

            return Results.Ok(allRides);
        }

        // FacilityAdmin: get rides from their assigned facility
        if (tenant.FacilityId is null)
            return Results.BadRequest(new { error = "Facility context required." });

        var singleFacility = await db.Facilities.AsNoTracking()
            .FirstAsync(f => f.Id == tenant.FacilityId.Value);

        var facilityRides = await rideService.GetTodaysRidesAsync(
            tenant.FacilityId.Value, singleFacility.Timezone);

        return Results.Ok(facilityRides);
    }

    private static async Task<IResult> BookRide(
        BookRideRequest request,
        IValidator<BookRideRequest> validator,
        HttpContext httpContext,
        AppDbContext db,
        RideService rideService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();

        // Resolve facilityId: explicit → from JWT → first facility in org (OrgAdmin fallback)
        Guid facilityId;
        if (request.FacilityId.HasValue)
            facilityId = request.FacilityId.Value;
        else if (tenant.FacilityId.HasValue)
            facilityId = tenant.FacilityId.Value;
        else
        {
            var firstFacility = await db.Facilities
                .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
                .Select(f => f.Id)
                .FirstOrDefaultAsync();
            if (firstFacility == Guid.Empty)
                return Results.BadRequest(new { error = "No active facility found for this organization." });
            facilityId = firstFacility;
        }

        var ride = await rideService.BookRideAsync(
            facilityId,
            tenant.OrganizationId,
            request.ResidentId,
            request.PickupTime,
            request.PickupAddress,
            request.DestinationAddress,
            request.PreferredChannel);


        return Results.Created($"/api/rides/{ride.Id}", new
        {
            ride.Id,
            Status = ride.Status.ToString(),
            DispatchChannel = ride.DispatchChannel.ToString(),
            ride.VendorId
        });
    }

    private static async Task<IResult> GetDetail(
        Guid id,
        HttpContext httpContext,
        RideService rideService)
    {
        var tenant = httpContext.GetTenantContext();
        var detail = await rideService.GetRideDetailAsync(id, tenant.OrganizationId);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> AdvanceStatus(
        Guid id,
        AdvanceStatusRequest request,
        IValidator<AdvanceStatusRequest> validator,
        HttpContext httpContext,
        RideService rideService)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        var ride = await rideService.AdvanceStatusAsync(
            id, request.NewStatus, "coordinator", request.Notes, tenant.OrganizationId);

        return Results.Ok(new { ride.Id, Status = ride.Status.ToString() });
    }

    private static async Task<IResult> CancelRide(
        Guid id,
        HttpContext httpContext,
        RideService rideService)
    {
        var tenant = httpContext.GetTenantContext();
        var ride = await rideService.AdvanceStatusAsync(
            id, RideStatus.Cancelled, "coordinator", "Cancelled by coordinator", tenant.OrganizationId);

        return Results.Ok(new { ride.Id, Status = ride.Status.ToString() });
    }

    private static async Task<IResult> GetTodayCount(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        int count;
        if (tenant.Role == UserRole.OrgAdmin)
        {
            count = await db.Rides
                .Where(r => r.Facility.OrganizationId == tenant.OrganizationId
                    && r.PickupTime >= today && r.PickupTime < tomorrow
                    && r.Status != RideStatus.Cancelled)
                .CountAsync();
        }
        else
        {
            count = await db.Rides
                .Where(r => r.FacilityId == tenant.FacilityId
                    && r.PickupTime >= today && r.PickupTime < tomorrow
                    && r.Status != RideStatus.Cancelled)
                .CountAsync();
        }

        return Results.Ok(new { count });
    }

    private static async Task<IResult> Redispatch(
        Guid id,
        HttpContext httpContext,
        RideService rideService)
    {
        var tenant = httpContext.GetTenantContext();
        try
        {
            var ride = await rideService.RedispatchAsync(id, tenant.OrganizationId);
            return Results.Ok(new { id = ride.Id, status = ride.Status.ToString(), dispatchChannel = ride.DispatchChannel.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> GetDispatchOffers(
        Guid id,
        HttpContext httpContext,
        RideService rideService)
    {
        var tenant = httpContext.GetTenantContext();
        var offers = await rideService.GetDispatchOffersAsync(id, tenant.OrganizationId);
        return Results.Ok(offers);
    }
}

public record BookRideRequest(
    Guid? FacilityId,
    Guid? ResidentId,
    DateTime PickupTime,
    string PickupAddress,
    string DestinationAddress,
    DispatchChannel? PreferredChannel);

public record AdvanceStatusRequest(RideStatus NewStatus, string? Notes);
