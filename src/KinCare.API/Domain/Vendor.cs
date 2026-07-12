namespace KinCare.API.Domain;

public class Vendor
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public VendorType VendorType { get; set; }
    public DispatchMethod DispatchMethod { get; set; }
    public VendorCapabilityTier CapabilityTier { get; set; } = VendorCapabilityTier.Basic;
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Facility Facility { get; set; } = null!;
    public ICollection<Ride> Rides { get; set; } = new List<Ride>();
}
