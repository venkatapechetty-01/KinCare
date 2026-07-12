namespace KinCare.API.Domain;

public class Facility
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Timezone { get; set; } = "America/New_York";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Organization Organization { get; set; } = null!;
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Resident> Residents { get; set; } = new List<Resident>();
    public ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
    public ICollection<Ride> Rides { get; set; } = new List<Ride>();
}
