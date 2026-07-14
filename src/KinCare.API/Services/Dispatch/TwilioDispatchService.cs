using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace KinCare.API.Services.Dispatch;

public class TwilioDispatchService
{
    private readonly TwilioConfig _config;
    private readonly ILogger<TwilioDispatchService> _logger;

    public TwilioDispatchService(IOptions<TwilioConfig> config, ILogger<TwilioDispatchService> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    private void EnsureClientInitialized()
    {
        if (string.IsNullOrWhiteSpace(_config.AccountSid) || string.IsNullOrWhiteSpace(_config.AuthToken))
        {
            _logger.LogWarning("Twilio credentials not configured — SMS will be skipped");
            return;
        }
        TwilioClient.Init(_config.AccountSid, _config.AuthToken);
    }

    public async Task SendBookingSmsAsync(Ride ride, Vendor vendor, Resident resident, string? trackingUrl = null)
    {
        var specialNeeds = BuildSpecialNeedsTags(resident, ride.DispatchChannel);
        var body = BuildBookingSms(ride, resident, specialNeeds, trackingUrl);

        await SendSmsAsync(vendor.PhoneNumber, body);
    }

    public async Task SendCheckpointSmsAsync(Ride ride, Vendor vendor, string checkpoint)
    {
        // Reply menu matches the digit scheme in TwilioWebhookHandler.PostAcceptReplyMap,
        // which is keyed on the ride's current status — the return leg reuses the same
        // digits with different meanings.
        var replyMenu = ride.Status == RideStatus.AwaitingReturn || ride.Status == RideStatus.ReturnEnRoute
            ? "Reply with number:\n" +
              "3 = On My Way Back\n" +
              "5 = Picked Up\n" +
              "8 = Trip Complete\n" +
              "9 = Report Issue"
            : "Reply with number:\n" +
              "3 = On My Way\n" +
              "4 = Arrived\n" +
              "5 = Picked Up\n" +
              "6 = At Destination\n" +
              "7 = Dropped Off\n" +
              "8 = Trip Complete\n" +
              "9 = Report Issue";

        var body = $"KinCare Ride #{ride.Id.ToString()[..8]}\n" +
                   $"{checkpoint}\n" +
                   replyMenu;

        await SendSmsAsync(vendor.PhoneNumber, body);
    }

    private string BuildBookingSms(Ride ride, Resident resident, string specialNeeds, string? trackingUrl)
    {
        var lines = new List<string>
        {
            "KinCare Transport Request",
            $"Resident: {resident.FirstName} {resident.LastName}",
        };

        if (!string.IsNullOrEmpty(specialNeeds))
            lines.Add($"Needs: {specialNeeds}");

        lines.Add($"Pickup: {ride.PickupAddress}");
        lines.Add($"Dropoff: {ride.DestinationAddress}");
        lines.Add($"Time: {ride.PickupTime:g}");

        if (!string.IsNullOrEmpty(trackingUrl))
            lines.Add($"Track: {trackingUrl}");

        lines.Add("");
        lines.Add("Reply 1 = ACCEPT");
        lines.Add("Reply 2 = DECLINE");

        return string.Join("\n", lines);
    }

    private static string BuildSpecialNeedsTags(Resident resident, DispatchChannel channel)
    {
        if (channel == DispatchChannel.SmsTaxi)
            return string.Empty;

        var tags = new List<string>();
        if (resident.NeedsWheelchair) tags.Add("Wheelchair");
        if (resident.NeedsOxygen) tags.Add("Oxygen");
        if (resident.NeedsStretcher) tags.Add("Stretcher");
        if (resident.NeedsWalker) tags.Add("Walker");
        return string.Join(", ", tags);
    }

    public async Task SendRideClaimedSmsAsync(Vendor vendor, Ride ride)
    {
        var body = $"KinCare: Ride #{ride.Id.ToString()[..8]} was already accepted by another driver. No action needed.";
        await SendSmsAsync(vendor.PhoneNumber, body);
    }

    private async Task SendSmsAsync(string toNumber, string body)
    {
        if (string.IsNullOrWhiteSpace(_config.AccountSid) || string.IsNullOrWhiteSpace(_config.AuthToken))
        {
            _logger.LogWarning("Twilio not configured — skipping SMS to {PhoneNumber}", toNumber);
            return;
        }

        try
        {
            EnsureClientInitialized();
            await MessageResource.CreateAsync(
                to: new PhoneNumber(toNumber),
                from: new PhoneNumber(_config.FromNumber),
                body: body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", toNumber);
        }
    }
}
