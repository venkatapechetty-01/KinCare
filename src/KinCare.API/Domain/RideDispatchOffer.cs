namespace KinCare.API.Domain;

public class RideDispatchOffer
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public Guid VendorId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Accepted | Declined | Superseded
    public string? TrackingToken { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    public Ride Ride { get; set; } = null!;
    public Vendor Vendor { get; set; } = null!;
}
