using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using KinCare.API.Data;
using KinCare.API.Domain;
using KinCare.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KinCare.API.Services;

public class FcmService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FcmService> _logger;
    private readonly bool _initialized;

    public FcmService(AppDbContext db, IOptions<FcmConfig> config, ILogger<FcmService> logger)
    {
        _db = db;
        _logger = logger;

        if (FirebaseApp.DefaultInstance is null && File.Exists(config.Value.CredentialPath))
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(config.Value.CredentialPath)
            });
            _initialized = true;
        }
        else
        {
            _initialized = FirebaseApp.DefaultInstance is not null;
        }
    }

    public async Task SendToFacilityUsersAsync(Guid facilityId, string title, string body)
    {
        if (!_initialized)
        {
            _logger.LogWarning("FCM not initialized — skipping push notification");
            return;
        }

        var tokens = await _db.Set<DeviceRegistration>()
            .Where(d => d.User.FacilityId == facilityId)
            .Select(d => d.FcmToken)
            .ToListAsync();

        if (tokens.Count == 0) return;

        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High },
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            _logger.LogInformation("FCM sent to {Count} devices, {Success} successful",
                tokens.Count, response.SuccessCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM send failed");
        }
    }

    public async Task SendToUserAsync(Guid userId, string title, string body)
    {
        if (!_initialized) return;

        var tokens = await _db.Set<DeviceRegistration>()
            .Where(d => d.UserId == userId)
            .Select(d => d.FcmToken)
            .ToListAsync();

        if (tokens.Count == 0) return;

        foreach (var token in tokens)
        {
            var message = new Message
            {
                Token = token,
                Notification = new Notification { Title = title, Body = body },
                Android = new AndroidConfig { Priority = Priority.High },
            };

            try
            {
                await FirebaseMessaging.DefaultInstance.SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM send to user {UserId} failed", userId);
            }
        }
    }
}
