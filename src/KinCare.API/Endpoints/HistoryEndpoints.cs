using System.Text;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rides/history").WithTags("History").RequireAuthorization();

        group.MapGet("/", GetHistory);
        group.MapGet("/export", ExportCsv);
    }

    private static async Task<IResult> GetHistory(
        HttpContext httpContext,
        AppDbContext db,
        int page = 1,
        int pageSize = 25,
        string? status = null,
        string? channel = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);

        var tenant = httpContext.GetTenantContext();
        var query = db.Rides.AsNoTracking().AsQueryable();

        if (tenant.FacilityId.HasValue)
            query = query.Where(r => r.FacilityId == tenant.FacilityId.Value);
        else
            query = query.Where(r => r.OrganizationId == tenant.OrganizationId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RideStatus>(status, true, out var s))
            query = query.Where(r => r.Status == s);

        if (!string.IsNullOrEmpty(channel) && Enum.TryParse<DispatchChannel>(channel, true, out var c))
            query = query.Where(r => r.DispatchChannel == c);

        if (from.HasValue)
            query = query.Where(r => r.PickupTime >= from.Value);

        if (to.HasValue)
            query = query.Where(r => r.PickupTime <= to.Value);

        var total = await query.CountAsync();

        var rides = await query
            .OrderByDescending(r => r.PickupTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new HistoryRideDto(
                r.Id,
                r.Resident != null ? r.Resident.FirstName + " " + r.Resident.LastName : "Unknown",
                r.Vendor != null ? r.Vendor.Name : null,
                r.Status.ToString(),
                r.DispatchChannel.ToString(),
                r.PickupTime,
                r.PickupAddress,
                r.DestinationAddress,
                r.Facility.Name))
            .ToListAsync();

        return Results.Ok(new HistoryResponse(rides, total, page, pageSize));
    }

    private static async Task<IResult> ExportCsv(
        HttpContext httpContext,
        AppDbContext db,
        IPlanGate planGate,
        DateTime? from = null,
        DateTime? to = null)
    {
        var tenant = httpContext.GetTenantContext();
        planGate.Requires(tenant.Organization, PlanFeature.CsvExport);

        var query = db.Rides.AsNoTracking()
            .Include(r => r.Resident)
            .Include(r => r.Vendor)
            .Include(r => r.Events)
            .AsQueryable();

        if (tenant.FacilityId.HasValue)
            query = query.Where(r => r.FacilityId == tenant.FacilityId.Value);
        else
            query = query.Where(r => r.OrganizationId == tenant.OrganizationId);

        if (from.HasValue)
            query = query.Where(r => r.PickupTime >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.PickupTime <= to.Value);

        const int maxExportRows = 10000;
        var rides = await query.OrderByDescending(r => r.PickupTime).Take(maxExportRows).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Date,Channel,Resident,Vendor,Pickup,Destination,Status,Dispatched,Confirmed,EnRoute,Arrived,Dropped,Completed");

        foreach (var ride in rides)
        {
            var events = ride.Events.ToDictionary(e => e.ToStatus, e => (DateTime?)e.OccurredAt);
            sb.AppendLine(string.Join(",",
                Escape(ride.PickupTime.ToString("yyyy-MM-dd HH:mm")),
                ride.DispatchChannel.ToString(),
                Escape(ride.Resident != null ? ride.Resident.FirstName + " " + ride.Resident.LastName : "Unknown"),
                Escape(ride.Vendor?.Name ?? ""),
                Escape(ride.PickupAddress),
                Escape(ride.DestinationAddress),
                ride.Status.ToString(),
                events.GetValueOrDefault(RideStatus.Dispatched)?.ToString("HH:mm") ?? "",
                events.GetValueOrDefault(RideStatus.Confirmed)?.ToString("HH:mm") ?? "",
                events.GetValueOrDefault(RideStatus.EnRoute)?.ToString("HH:mm") ?? "",
                events.GetValueOrDefault(RideStatus.Arrived)?.ToString("HH:mm") ?? "",
                events.GetValueOrDefault(RideStatus.Dropped)?.ToString("HH:mm") ?? "",
                events.GetValueOrDefault(RideStatus.Completed)?.ToString("HH:mm") ?? ""));
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Results.File(bytes, "text/csv", "ride-history.csv");
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Prevent CSV formula injection
        if (value.Length > 0 && "=+@-\t\r".Contains(value[0]))
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

public record HistoryRideDto(
    Guid Id, string ResidentName, string? VendorName,
    string Status, string DispatchChannel,
    DateTime PickupTime, string PickupAddress, string DestinationAddress,
    string FacilityName);

public record HistoryResponse(
    List<HistoryRideDto> Rides, int Total, int Page, int PageSize);
