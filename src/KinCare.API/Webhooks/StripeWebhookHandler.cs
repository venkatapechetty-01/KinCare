using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using PlanTier = KinCare.API.Domain.PlanTier;

namespace KinCare.API.Webhooks;

public static class StripeWebhookHandler
{
    public static void MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhook/stripe", HandleWebhook)
            .WithTags("Webhooks")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleWebhook(
        HttpContext httpContext,
        AppDbContext db,
        IOptions<StripeConfig> stripeConfig,
        ILogger<Program> logger)
    {
        const int maxBodySize = 65536; // 64KB
        httpContext.Request.EnableBuffering();
        if (httpContext.Request.ContentLength > maxBodySize)
            return Results.StatusCode(413);

        using var reader = new StreamReader(httpContext.Request.Body);
        var buffer = new char[maxBodySize];
        var charsRead = await reader.ReadAsync(buffer, 0, maxBodySize);
        if (!reader.EndOfStream)
            return Results.StatusCode(413);

        var json = new string(buffer, 0, charsRead);
        var signature = httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(signature))
            return Results.BadRequest();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json, signature, stripeConfig.Value.WebhookSecret);
        }
        catch (Exception ex) when (ex is StripeException || ex is ArgumentException || ex is InvalidOperationException)
        {
            logger.LogWarning("Stripe webhook signature validation failed: {Message}", ex.Message);
            return Results.BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case "invoice.paid":
                await HandleInvoicePaid(stripeEvent, db, logger);
                break;
            case "invoice.payment_failed":
                await HandlePaymentFailed(stripeEvent, db, logger);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeleted(stripeEvent, db, logger);
                break;
            case "customer.subscription.updated":
                await HandleSubscriptionUpdated(stripeEvent, db, logger);
                break;
        }

        return Results.Ok();
    }

    private static async Task HandleInvoicePaid(Event stripeEvent, AppDbContext db, ILogger logger)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice?.CustomerId is null) return;

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.StripeCustomerId == invoice.CustomerId);
        if (org is null) return;

        org.IsActive = true;
        await db.SaveChangesAsync();
        logger.LogInformation("Organization {OrgId} activated after payment", org.Id);
    }

    private static async Task HandlePaymentFailed(Event stripeEvent, AppDbContext db, ILogger logger)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice?.CustomerId is null) return;

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.StripeCustomerId == invoice.CustomerId);
        if (org is null) return;

        org.IsActive = false;
        await db.SaveChangesAsync();
        logger.LogWarning("Organization {OrgId} deactivated — payment failed", org.Id);
    }

    private static async Task HandleSubscriptionDeleted(Event stripeEvent, AppDbContext db, ILogger logger)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription?.CustomerId is null) return;

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.StripeCustomerId == subscription.CustomerId);
        if (org is null) return;

        org.IsActive = false;
        org.StripeSubscriptionId = null;
        await db.SaveChangesAsync();
        logger.LogWarning("Organization {OrgId} subscription deleted", org.Id);
    }

    private static async Task HandleSubscriptionUpdated(Event stripeEvent, AppDbContext db, ILogger logger)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription?.CustomerId is null) return;

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.StripeCustomerId == subscription.CustomerId);
        if (org is null) return;

        var metadata = subscription.Metadata;
        if (metadata.TryGetValue("plan_tier", out var tierStr) && Enum.TryParse<PlanTier>(tierStr, out var tier))
        {
            org.PlanTier = tier;
            await db.SaveChangesAsync();
            logger.LogInformation("Organization {OrgId} plan updated to {Tier}", org.Id, tier);
        }
    }
}
