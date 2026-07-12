namespace KinCare.API.Domain;

public class RideEvent
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public RideStatus FromStatus { get; set; }
    public RideStatus ToStatus { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public Ride Ride { get; set; } = null!;
}
