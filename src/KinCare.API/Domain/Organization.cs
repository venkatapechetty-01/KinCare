namespace KinCare.API.Domain;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PlanTier PlanTier { get; set; } = PlanTier.Starter;
    public bool IsActive { get; set; } = true;
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string BillingEmail { get; set; } = string.Empty;
    public bool BrokerEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Facility> Facilities { get; set; } = new List<Facility>();
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
}
