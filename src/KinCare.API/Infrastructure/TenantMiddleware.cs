using KinCare.API.Data;
using KinCare.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace KinCare.API.Infrastructure;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogInformation("TenantMiddleware: User not authenticated, skipping");
            await _next(context);
            return;
        }

        var userIdClaim = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("TenantMiddleware: Missing or invalid sub claim");
            context.Response.StatusCode = 401;
            return;
        }

        var orgIdClaim = context.User.FindFirst("organization_id")?.Value;
        _logger.LogInformation("TenantMiddleware: orgIdClaim = {OrgIdClaim}", orgIdClaim);
        if (orgIdClaim is null || !Guid.TryParse(orgIdClaim, out var orgId))
        {
            _logger.LogWarning("TenantMiddleware: Invalid or missing organization_id claim");
            context.Response.StatusCode = 401;
            return;
        }

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId);
        _logger.LogInformation("TenantMiddleware: org found = {OrgFound}, IsActive = {IsActive}", org != null, org?.IsActive);
        if (org is null)
        {
            _logger.LogWarning("TenantMiddleware: Organization {OrgId} not found", orgId);
            context.Response.StatusCode = 401;
            return;
        }

        if (!org.IsActive)
        {
            _logger.LogWarning("TenantMiddleware: Organization {OrgId} is inactive", orgId);
            context.Response.StatusCode = 402;
            await context.Response.WriteAsJsonAsync(new { error = "Organization subscription is inactive." });
            return;
        }

        var roleClaim = context.User.FindFirst("role")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        _logger.LogInformation("TenantMiddleware: roleClaim = {RoleClaim}", roleClaim);
        if (!Enum.TryParse<UserRole>(roleClaim, out var role) || !Enum.IsDefined(role))
        {
            _logger.LogWarning("TenantMiddleware: Invalid role claim: {RoleClaim}", roleClaim);
            context.Response.StatusCode = 401;
            return;
        }

        Guid? facilityId = null;
        var facilityIdClaim = context.User.FindFirst("facility_id")?.Value;
        if (facilityIdClaim is not null && Guid.TryParse(facilityIdClaim, out var fId))
            facilityId = fId;

        _logger.LogInformation("TenantMiddleware: Success - Role={Role}, FacilityId={FacilityId}", role, facilityId);

        var tenantContext = new TenantContext
        {
            UserId = userId,
            OrganizationId = orgId,
            FacilityId = facilityId,
            Role = role,
            Organization = org
        };

        context.Items["TenantContext"] = tenantContext;
        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static TenantContext GetTenantContext(this HttpContext context)
    {
        return (TenantContext)context.Items["TenantContext"]!;
    }
}
