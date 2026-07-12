using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using PlanTier = KinCare.API.Domain.PlanTier;

namespace KinCare.API.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing").WithTags("Billing").RequireAuthorization();

        group.MapPost("/subscribe", Subscribe);
        group.MapGet("/portal", GetPortalUrl);
        group.MapGet("/plan", GetPlan);
    }

    private static async Task<IResult> Subscribe(
        SubscribeRequest request,
        HttpContext httpContext,
        AppDbContext db,
        IOptions<StripeConfig> stripeConfig)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();

        var org = await db.Organizations.FirstAsync(o => o.Id == tenant.OrganizationId);
        var config = stripeConfig.Value;
        var client = new StripeClient(config.SecretKey);

        try
        {
            if (string.IsNullOrEmpty(org.StripeCustomerId))
            {
                var customerService = new CustomerService(client);
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = org.BillingEmail,
                    Name = org.Name,
                    Metadata = new Dictionary<string, string> { { "organization_id", org.Id.ToString() } }
                });
                org.StripeCustomerId = customer.Id;
                await db.SaveChangesAsync();
            }

            var priceId = request.PlanTier switch
            {
                PlanTier.Professional => config.ProfessionalPriceId,
                PlanTier.Enterprise => config.EnterprisePriceId,
                _ => config.StarterPriceId
            };

            var subscriptionService = new SubscriptionService(client);
            var subscription = await subscriptionService.CreateAsync(new SubscriptionCreateOptions
            {
                Customer = org.StripeCustomerId,
                Items = new List<SubscriptionItemOptions>
                {
                    new() { Price = priceId }
                },
                TrialPeriodDays = 14,
                Metadata = new Dictionary<string, string> { { "organization_id", org.Id.ToString() } }
            });

            org.StripeSubscriptionId = subscription.Id;
            org.PlanTier = request.PlanTier;
            await db.SaveChangesAsync();

            return Results.Ok(new { subscriptionId = subscription.Id });
        }
        catch (StripeException ex)
        {
            return Results.BadRequest(new { error = ex.StripeError?.Message ?? "Billing setup failed." });
        }
    }

    private static async Task<IResult> GetPortalUrl(
        HttpContext httpContext,
        AppDbContext db,
        IOptions<StripeConfig> stripeConfig,
        IConfiguration configuration)
    {
        var tenant = httpContext.GetTenantContext();
        if (tenant.Role != UserRole.OrgAdmin)
            return Results.Forbid();

        var org = await db.Organizations.AsNoTracking()
            .FirstAsync(o => o.Id == tenant.OrganizationId);

        if (string.IsNullOrEmpty(org.StripeCustomerId))
            return Results.BadRequest(new { error = "No billing account set up." });

        var config = stripeConfig.Value;
        var client = new StripeClient(config.SecretKey);
        var returnUrl = configuration["App:BaseUrl"] ?? "https://app.kincare.com";

        try
        {
            var portalService = new Stripe.BillingPortal.SessionService(client);
            var session = await portalService.CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = org.StripeCustomerId,
                    ReturnUrl = $"{returnUrl}/billing"
                });

            return Results.Ok(new { url = session.Url });
        }
        catch (StripeException ex)
        {
            return Results.BadRequest(new { error = ex.StripeError?.Message ?? "Could not create billing portal session." });
        }
    }

    private static async Task<IResult> GetPlan(
        HttpContext httpContext,
        AppDbContext db)
    {
        var tenant = httpContext.GetTenantContext();
        var org = await db.Organizations.AsNoTracking()
            .FirstAsync(o => o.Id == tenant.OrganizationId);

        return Results.Ok(new PlanResponse(
            org.PlanTier.ToString(),
            org.IsActive,
            org.StripeSubscriptionId));
    }
}

public record SubscribeRequest(PlanTier PlanTier);
public record PlanResponse(string PlanTier, bool IsActive, string? SubscriptionId);
