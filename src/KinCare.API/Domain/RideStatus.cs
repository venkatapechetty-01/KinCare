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
    AwaitingReturn,  // NEMT only: coordinator has requested the return leg
    ReturnEnRoute,   // NEMT only: vendor heading back to pick up resident
    ReturnPickedUp,  // NEMT only: resident back in vehicle for the return leg
    Completed,       // Trip fully complete (driver confirmed return)
    Cancelled
}
