using System.Security.Cryptography;
using System.Text;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using KinCare.API.Services;
using KinCare.API.Services.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KinCare.API.Webhooks;

public static class BrokerWebhookHandler
{
    public static void MapBrokerWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhook/broker", HandleWebhook)
            .WithTags("Webhooks")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleWebhook(
        HttpContext httpContext,
        AppDbContext db,
        RideService rideService,
        IOptions<BrokerConfig> brokerConfig,
        ILogger<Program> logger)
    {
        httpContext.Request.EnableBuffering();
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        var signature = httpContext.Request.Headers["X-Broker-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
            return Results.Unauthorized();

        var secret = brokerConfig.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogError("Broker webhook secret not configured");
            return Results.StatusCode(500);
        }

        var expectedSignature = ComputeHmacSha256(body, secret);
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            logger.LogWarning("Broker webhook signature verification failed");
            return Results.Unauthorized();
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<BrokerWebhookPayload>(body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null)
            return Results.BadRequest();

        var ride = await db.Rides
            .FirstOrDefaultAsync(r => r.ExternalTripId == payload.TripId
                && r.DispatchChannel == DispatchChannel.Broker);

        if (ride is null)
        {
            logger.LogWarning("Broker webhook for unknown trip: {TripId}", payload.TripId);
            return Results.Ok();
        }

        var newStatus = BrokerDispatchService.MapBrokerStatus(payload.Status);
        if (newStatus is null)
            return Results.Ok();

        var stateMachine = new RideStateMachine();
        if (!stateMachine.CanTransition(ride.Status, newStatus.Value, ride.DispatchChannel))
            return Results.Ok();

        await rideService.AdvanceStatusAsync(
            ride.Id, newStatus.Value, "broker_webhook", $"broker_status:{payload.Status}");

        return Results.Ok();
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private record BrokerWebhookPayload(string TripId, string Status);
}
