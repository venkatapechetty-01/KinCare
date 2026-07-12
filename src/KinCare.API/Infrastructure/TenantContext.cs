using KinCare.API.Domain;

namespace KinCare.API.Infrastructure;

public class TenantContext
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? FacilityId { get; set; }
    public UserRole Role { get; set; }
    public Organization Organization { get; set; } = null!;
}
