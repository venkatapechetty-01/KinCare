using KinCare.API.Infrastructure;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace KinCare.API.Services;

public class EmailService
{
    private readonly SendGridConfig _config;
    private readonly AppConfig _appConfig;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<SendGridConfig> config,
        IOptions<AppConfig> appConfig,
        ILogger<EmailService> logger)
    {
        _config = config.Value;
        _appConfig = appConfig.Value;
        _logger = logger;
    }

    public async Task SendInvitationAsync(string toEmail, string organizationName, string? facilityName, string inviteToken)
    {
        var acceptUrl = $"{_appConfig.BaseUrl}/accept-invite?token={Uri.EscapeDataString(inviteToken)}";
        var facilityLine = facilityName is not null ? $"<p>Facility: <strong>{facilityName}</strong></p>" : "";

        var subject = $"You've been invited to join {organizationName} on KinCare";
        var html = $"""
            <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
              <h2>You're invited to KinCare</h2>
              <p>You've been invited to join <strong>{organizationName}</strong> as a coordinator.</p>
              {facilityLine}
              <p>Click the button below to set your password and get started. This link expires in 7 days.</p>
              <p style="margin: 32px 0;">
                <a href="{acceptUrl}"
                   style="background:#1976d2;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;">
                  Accept Invitation
                </a>
              </p>
              <p style="color:#888;font-size:12px;">
                If the button doesn't work, paste this URL into your browser:<br>{acceptUrl}
              </p>
            </div>
            """;

        await SendAsync(toEmail, subject, html);
    }

    public async Task SendPasswordResetAsync(string toEmail, string resetToken)
    {
        var resetUrl = $"{_appConfig.BaseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var subject = "Reset your KinCare password";
        var html = $"""
            <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
              <h2>Reset your password</h2>
              <p>We received a request to reset the password for your KinCare account.</p>
              <p style="margin: 32px 0;">
                <a href="{resetUrl}"
                   style="background:#1976d2;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;">
                  Reset Password
                </a>
              </p>
              <p style="color:#888;font-size:12px;">This link expires in 1 hour. If you didn't request a reset, you can ignore this email.</p>
            </div>
            """;

        await SendAsync(toEmail, subject, html);
    }

    private async Task SendAsync(string toEmail, string subject, string html)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _logger.LogWarning("SendGrid API key not configured — skipping email to {Email}", toEmail);
            return;
        }

        try
        {
            var client = new SendGridClient(_config.ApiKey);
            var from = new EmailAddress(_config.FromEmail, _config.FromName);
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: html);

            var response = await client.SendEmailAsync(msg);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid returned {Status} for {Email}: {Body}", response.StatusCode, toEmail, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
