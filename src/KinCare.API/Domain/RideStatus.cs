namespace KinCare.API.Domain;

public enum RideStatus
{
    Dispatched,
    Confirmed,
    EnRoute,         // Driver on the way to facility
    Arrived,         // Driver reached facility
    PickedUp,        // Resident in vehicle, heading to destination
    AtDestination,   // Driver arrived at destination
    Dropped,         // Resident dropped off at destination
    Completed,       // Trip fully complete (driver confirmed return)
    Cancelled
}
