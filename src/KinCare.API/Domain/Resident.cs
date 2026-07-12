namespace KinCare.API.Domain;

public class Resident
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool NeedsWheelchair { get; set; }
    public bool NeedsOxygen { get; set; }
    public bool NeedsStretcher { get; set; }
    public bool NeedsWalker { get; set; }
    public string? DriverNotes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Facility Facility { get; set; } = null!;
    public ICollection<Ride> Rides { get; set; } = new List<Ride>();
}
