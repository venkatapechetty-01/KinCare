namespace KinCare.API.Infrastructure;

public class StripeConfig
{
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string StarterPriceId { get; set; } = string.Empty;
    public string ProfessionalPriceId { get; set; } = string.Empty;
    public string EnterprisePriceId { get; set; } = string.Empty;
}
