using KinCare.API.Domain;

namespace KinCare.API.Services;

public class RideStateMachine
{
    private static readonly Dictionary<RideStatus, HashSet<RideStatus>> ValidTransitions = new()
    {
        { RideStatus.Dispatched,    new() { RideStatus.Confirmed,     RideStatus.Cancelled } },
        { RideStatus.Confirmed,     new() { RideStatus.EnRoute,       RideStatus.Cancelled } },
        { RideStatus.EnRoute,       new() { RideStatus.Arrived,       RideStatus.Cancelled } },
        { RideStatus.Arrived,       new() { RideStatus.PickedUp,      RideStatus.Cancelled } },
        { RideStatus.PickedUp,      new() { RideStatus.AtDestination, RideStatus.Cancelled } },
        { RideStatus.AtDestination, new() { RideStatus.Dropped,       RideStatus.Cancelled } },
        { RideStatus.Dropped,       new() { RideStatus.Completed,     RideStatus.Cancelled } },
        { RideStatus.Completed,     new() },
        { RideStatus.Cancelled,     new() },
    };

    public bool CanTransition(RideStatus from, RideStatus to)
        => ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public void Validate(RideStatus from, RideStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"Invalid ride status transition: {from} → {to}");
    }

    public static bool IsTerminal(RideStatus status)
        => status is RideStatus.Completed or RideStatus.Cancelled;

    // The next status the tracking page should advance to
    public static RideStatus? NextTrackingStatus(RideStatus current) => current switch
    {
        RideStatus.Confirmed     => RideStatus.EnRoute,
        RideStatus.EnRoute       => RideStatus.Arrived,
        RideStatus.Arrived       => RideStatus.PickedUp,
        RideStatus.PickedUp      => RideStatus.AtDestination,
        RideStatus.AtDestination => RideStatus.Dropped,
        RideStatus.Dropped       => RideStatus.Completed,
        _                        => null,
    };
}
