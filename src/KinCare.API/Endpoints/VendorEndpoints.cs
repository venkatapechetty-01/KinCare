using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class VendorEndpoints
{
    public static void MapVendorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vendors").WithTags("Vendors").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapPost("/", Create);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> GetAll(
        HttpContext httpContext,
        AppDbContext db,
        string? type = null)
    {
        var tenant = httpContext.GetTenantContext();
        var query = db.Vendors.AsNoTracking().Where(v => v.IsActive);

        if (tenant.FacilityId.HasValue)
            query = query.Where(v => v.FacilityId == tenant.FacilityId.Value);
        else
            query = query.Where(v => v.Facility.OrganizationId == tenant.OrganizationId);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<VendorType>(type, true, out var vendorType))
            query = query.Where(v => v.VendorType == vendorType);

        var vendors = await query
            .OrderBy(v => v.Name)
            .Select(v => new VendorDto(
                v.Id, v.FacilityId, v.Name, v.PhoneNumber,
                v.VendorType.ToString(), v.DispatchMethod.ToString(),
                v.CapabilityTier.ToString(), v.IsActive))
            .ToListAsync();

        return Results.Ok(vendors);
    }

    private static async Task<IResult> Create(
        CreateVendorRequest request,
        IValidator<CreateVendorRequest> validator,
        HttpContext httpContext,
        AppDbContext db,
        IPlanGate planGate)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();

        if (request.DispatchMethod == DispatchMethod.Broker)
            planGate.Requires(tenant.Organization, PlanFeature.BrokerDispatch);

        var facilityId = request.FacilityId ?? tenant.FacilityId;
        if (facilityId is null)
        {
            facilityId = await db.Facilities
                .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
                .Select(f => (Guid?)f.Id)
                .FirstOrDefaultAsync();
        }
        if (facilityId is null)
            return Results.BadRequest(new { error = "No active facility found for this organization." });

        var facility = await db.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facilityId);
        if (facility is null || facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId!.Value,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            VendorType = request.VendorType,
            DispatchMethod = request.DispatchMethod,
            CapabilityTier = request.CapabilityTier
        };

        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();

        return Results.Created($"/api/vendors/{vendor.Id}", new { id = vendor.Id });
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateVendorRequest request,
        IValidator<UpdateVendorRequest> validator,
        HttpContext httpContext,
        AppDbContext db,
        IPlanGate planGate)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        var vendor = await db.Vendors.Include(v => v.Facility).FirstOrDefaultAsync(v => v.Id == id);

        if (vendor is null)
            return Results.NotFound();

        if (vendor.Facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        if (tenant.FacilityId.HasValue && vendor.FacilityId != tenant.FacilityId.Value)
            return Results.Forbid();

        vendor.Name = request.Name;
        vendor.PhoneNumber = request.PhoneNumber;
        vendor.VendorType = request.VendorType;
        vendor.DispatchMethod = request.DispatchMethod;
        vendor.CapabilityTier = request.CapabilityTier;

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        Guid id,
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var vendor = await db.Vendors.Include(v => v.Facility).FirstOrDefaultAsync(v => v.Id == id);

        if (vendor is null)
            return Results.NotFound();

        if (vendor.Facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        if (tenant.FacilityId.HasValue && vendor.FacilityId != tenant.FacilityId.Value)
            return Results.Forbid();

        vendor.IsActive = false;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}

public record VendorDto(
    Guid Id, Guid FacilityId, string Name, string PhoneNumber,
    string VendorType, string DispatchMethod, string CapabilityTier, bool IsActive);

public record CreateVendorRequest(
    Guid? FacilityId, string Name, string PhoneNumber,
    VendorType VendorType, DispatchMethod DispatchMethod,
    VendorCapabilityTier CapabilityTier);

public record UpdateVendorRequest(
    string Name, string PhoneNumber,
    VendorType VendorType, DispatchMethod DispatchMethod,
    VendorCapabilityTier CapabilityTier);
