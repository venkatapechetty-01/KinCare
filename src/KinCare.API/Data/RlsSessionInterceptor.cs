using System.Data.Common;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KinCare.API.Data;

public class RlsSessionInterceptor : DbConnectionInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RlsSessionInterceptor> _logger;

    public RlsSessionInterceptor(IHttpContextAccessor httpContextAccessor, ILogger<RlsSessionInterceptor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetRlsVariablesAsync(connection, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetRlsVariablesAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task SetRlsVariablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        var tenant = context.Items["TenantContext"] as TenantContext;
        if (tenant is null)
            return;

        try
        {
            await using var cmd = connection.CreateCommand();

            // Set facility_id for RLS policies scoped to coordinators
            var facilityId = tenant.FacilityId?.ToString() ?? "";
            var orgId = tenant.OrganizationId.ToString();

            cmd.CommandText = $"""
                SET LOCAL app.current_facility_id = '{facilityId}';
                SET LOCAL app.current_organization_id = '{orgId}';
                """;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set RLS session variables for tenant {OrgId}", tenant.OrganizationId);
        }
    }
}
