namespace KinCare.API.Domain;

public class Ride
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ResidentId { get; set; }
    public Guid? VendorId { get; set; }
    public RideStatus Status { get; set; } = RideStatus.Dispatched;
    public DispatchChannel DispatchChannel { get; set; }
    public string? ExternalTripId { get; set; }
    public DateTime PickupTime { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public string? TrackingToken { get; set; }
    public double? LastKnownLat { get; set; }
    public double? LastKnownLng { get; set; }
    public DateTime? LastLocationAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Facility Facility { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public Resident? Resident { get; set; }
    public Vendor? Vendor { get; set; }
    public ICollection<RideEvent> Events { get; set; } = new List<RideEvent>();
}
