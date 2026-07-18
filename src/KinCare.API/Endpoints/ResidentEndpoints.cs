using FluentValidation;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class ResidentEndpoints
{
    public static void MapResidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/residents").WithTags("Residents").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/{id:guid}", GetById);
        group.MapPost("/", Create);
        group.MapPost("/bulk", BulkCreate);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
    }

    private static async Task<IResult> GetById(Guid id, HttpContext httpContext, AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var resident = await db.Residents.AsNoTracking()
            .Include(r => r.Facility)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);

        if (resident is null)
            return Results.NotFound();

        if (resident.Facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        if (tenant.FacilityId.HasValue && resident.FacilityId != tenant.FacilityId.Value)
            return Results.Forbid();

        return Results.Ok(new ResidentDto(
            resident.Id, resident.FacilityId, resident.FirstName, resident.LastName,
            resident.NeedsWheelchair, resident.NeedsOxygen, resident.NeedsStretcher, resident.NeedsWalker,
            resident.DriverNotes));
    }

    private static async Task<IResult> GetAll(HttpContext httpContext, AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();

        // For OrgAdmins, we need to include Facility to access OrganizationId
        var query = tenant.FacilityId.HasValue
            ? db.Residents.AsNoTracking().Where(r => r.IsActive)
            : db.Residents.AsNoTracking().Include(r => r.Facility).Where(r => r.IsActive);

        if (tenant.FacilityId.HasValue)
            query = query.Where(r => r.FacilityId == tenant.FacilityId.Value);
        else
            query = query.Where(r => r.Facility.OrganizationId == tenant.OrganizationId);

        var residents = await query
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .Select(r => new ResidentDto(
                r.Id, r.FacilityId, r.FirstName, r.LastName,
                r.NeedsWheelchair, r.NeedsOxygen, r.NeedsStretcher, r.NeedsWalker,
                r.DriverNotes))
            .ToListAsync();

        return Results.Ok(residents);
    }

    private static async Task<IResult> Create(
        CreateResidentRequest request,
        IValidator<CreateResidentRequest> validator,
        HttpContext httpContext,
        AppDbContext db)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
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

        var resident = new Resident
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId!.Value,
            FirstName = request.FirstName,
            LastName = request.LastName,
            NeedsWheelchair = request.NeedsWheelchair,
            NeedsOxygen = request.NeedsOxygen,
            NeedsStretcher = request.NeedsStretcher,
            NeedsWalker = request.NeedsWalker,
            DriverNotes = request.DriverNotes
        };

        db.Residents.Add(resident);
        await db.SaveChangesAsync();

        return Results.Created($"/api/residents/{resident.Id}", new ResidentDto(
            resident.Id, resident.FacilityId, resident.FirstName, resident.LastName,
            resident.NeedsWheelchair, resident.NeedsOxygen, resident.NeedsStretcher, resident.NeedsWalker,
            resident.DriverNotes));
    }

    private static async Task<IResult> BulkCreate(
        List<CreateResidentRequest> requests,
        IValidator<CreateResidentRequest> validator,
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var created = new List<object>();
        var errors = new List<object>();

        foreach (var request in requests)
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
            {
                errors.Add(new { name = $"{request.FirstName} {request.LastName}", errors = validation.ToDictionary() });
                continue;
            }

            var facilityId = request.FacilityId ?? tenant.FacilityId;
            if (facilityId is null)
            {
                facilityId = await db.Facilities
                    .Where(f => f.OrganizationId == tenant.OrganizationId && f.IsActive)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefaultAsync();
            }
            if (facilityId is null)
            {
                errors.Add(new { name = $"{request.FirstName} {request.LastName}", errors = new { FacilityId = new[] { "No active facility found." } } });
                continue;
            }

            var facility = await db.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facilityId);
            if (facility is null || facility.OrganizationId != tenant.OrganizationId)
            {
                errors.Add(new { name = $"{request.FirstName} {request.LastName}", errors = new { FacilityId = new[] { "Invalid facility." } } });
                continue;
            }

            var resident = new Resident
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId.Value,
                FirstName = request.FirstName,
                LastName = request.LastName,
                NeedsWheelchair = request.NeedsWheelchair,
                NeedsOxygen = request.NeedsOxygen,
                NeedsStretcher = request.NeedsStretcher,
                NeedsWalker = request.NeedsWalker,
                DriverNotes = request.DriverNotes
            };
            db.Residents.Add(resident);
            created.Add(new { id = resident.Id, firstName = resident.FirstName, lastName = resident.LastName });
        }

        await db.SaveChangesAsync();
        return Results.Ok(new { created, errors });
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateResidentRequest request,
        IValidator<UpdateResidentRequest> validator,
        HttpContext httpContext,
        AppDbContext db)
    {
        var validation = await validator.ValidateAsync(request);
        if (!validation.IsValid)
            return Results.ValidationProblem(validation.ToDictionary());

        var tenant = httpContext.GetTenantContext();
        var resident = await db.Residents.Include(r => r.Facility).FirstOrDefaultAsync(r => r.Id == id);

        if (resident is null)
            return Results.NotFound();

        if (resident.Facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        if (tenant.FacilityId.HasValue && resident.FacilityId != tenant.FacilityId.Value)
            return Results.Forbid();

        resident.FirstName = request.FirstName;
        resident.LastName = request.LastName;
        resident.NeedsWheelchair = request.NeedsWheelchair;
        resident.NeedsOxygen = request.NeedsOxygen;
        resident.NeedsStretcher = request.NeedsStretcher;
        resident.NeedsWalker = request.NeedsWalker;
        resident.DriverNotes = request.DriverNotes;

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        Guid id,
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var resident = await db.Residents.Include(r => r.Facility).FirstOrDefaultAsync(r => r.Id == id);

        if (resident is null)
            return Results.NotFound();

        if (resident.Facility.OrganizationId != tenant.OrganizationId)
            return Results.Forbid();

        if (tenant.FacilityId.HasValue && resident.FacilityId != tenant.FacilityId.Value)
            return Results.Forbid();

        resident.IsActive = false;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}

public record ResidentDto(
    Guid Id, Guid FacilityId, string FirstName, string LastName,
    bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
    string? DriverNotes);

public record CreateResidentRequest(
    Guid? FacilityId, string FirstName, string LastName,
    bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
    string? DriverNotes);

public record UpdateResidentRequest(
    string FirstName, string LastName,
    bool NeedsWheelchair, bool NeedsOxygen, bool NeedsStretcher, bool NeedsWalker,
    string? DriverNotes);
