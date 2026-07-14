using KinCare.API.Domain;

namespace KinCare.API.Services;

public class RideStateMachine
{
    private static readonly Dictionary<RideStatus, HashSet<RideStatus>> ValidTransitions = new()
    {
        { RideStatus.Dispatched,     new() { RideStatus.Confirmed,      RideStatus.Cancelled } },
        { RideStatus.Confirmed,      new() { RideStatus.EnRoute,        RideStatus.Cancelled } },
        { RideStatus.EnRoute,        new() { RideStatus.Arrived,        RideStatus.Cancelled } },
        { RideStatus.Arrived,        new() { RideStatus.PickedUp,       RideStatus.Cancelled } },
        { RideStatus.PickedUp,       new() { RideStatus.AtDestination,  RideStatus.Cancelled } },
        { RideStatus.AtDestination,  new() { RideStatus.Dropped,        RideStatus.Cancelled } },
        // AwaitingReturn is only reachable when channel == SmsNemt — see CanTransition.
        { RideStatus.Dropped,        new() { RideStatus.Completed,      RideStatus.AwaitingReturn, RideStatus.Cancelled } },
        { RideStatus.AwaitingReturn, new() { RideStatus.ReturnEnRoute,  RideStatus.Cancelled } },
        { RideStatus.ReturnEnRoute,  new() { RideStatus.ReturnPickedUp, RideStatus.Cancelled } },
        { RideStatus.ReturnPickedUp, new() { RideStatus.Completed,      RideStatus.Cancelled } },
        { RideStatus.Completed,      new() },
        { RideStatus.Cancelled,      new() },
    };

    public bool CanTransition(RideStatus from, RideStatus to, DispatchChannel channel)
    {
        if (to == RideStatus.AwaitingReturn && channel != DispatchChannel.SmsNemt)
            return false;

        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public void Validate(RideStatus from, RideStatus to, DispatchChannel channel)
    {
        if (!CanTransition(from, to, channel))
            throw new InvalidOperationException($"Invalid ride status transition: {from} → {to}");
    }

    public static bool IsTerminal(RideStatus status)
        => status is RideStatus.Completed or RideStatus.Cancelled;

    // The next status the tracking page should advance to.
    // Dropped intentionally returns null for SmsNemt — AwaitingReturn is coordinator-only-triggered,
    // never offered as a driver-facing one-tap action.
    public static RideStatus? NextTrackingStatus(RideStatus current, DispatchChannel channel) => current switch
    {
        RideStatus.Confirmed      => RideStatus.EnRoute,
        RideStatus.EnRoute        => RideStatus.Arrived,
        RideStatus.Arrived        => RideStatus.PickedUp,
        RideStatus.PickedUp       => RideStatus.AtDestination,
        RideStatus.AtDestination  => RideStatus.Dropped,
        RideStatus.Dropped        => channel == DispatchChannel.SmsNemt ? null : RideStatus.Completed,
        RideStatus.AwaitingReturn => RideStatus.ReturnEnRoute,
        RideStatus.ReturnEnRoute  => RideStatus.ReturnPickedUp,
        RideStatus.ReturnPickedUp => RideStatus.Completed,
        _                         => null,
    };
}
