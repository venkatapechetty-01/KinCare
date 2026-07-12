namespace KinCare.API.Infrastructure;

public class SendGridConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "noreply@kincare.io";
    public string FromName { get; set; } = "KinCare";
}
