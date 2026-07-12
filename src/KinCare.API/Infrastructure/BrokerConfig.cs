namespace KinCare.API.Infrastructure;

public class BrokerConfig
{
    public string BaseUrl { get; set; } = "https://api.roundtriphealth.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Audience { get; set; } = "https://api.roundtriphealth.com";
    public string OrganizationId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
