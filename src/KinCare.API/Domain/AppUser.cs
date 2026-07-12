using Microsoft.AspNetCore.Identity;

namespace KinCare.API.Domain;

public class AppUser : IdentityUser<Guid>
{
    public Guid OrganizationId { get; set; }
    public Guid? FacilityId { get; set; }
    public UserRole Role { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? PhotoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
    public Facility? Facility { get; set; }
}
