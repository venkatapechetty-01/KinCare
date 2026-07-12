export interface Ride {
  id: string;
  facilityId: string;
  organizationId: string;
  residentId: string;
  vendorId?: string;
  status: RideStatus;
  dispatchChannel: DispatchChannel;
  externalTripId?: string;
  pickupTime: string;
  pickupAddress: string;
  destinationAddress: string;
  trackingToken?: string;
  lastKnownLat?: number;
  lastKnownLng?: number;
  lastLocationAt?: string;
  createdAt: string;
  residentName?: string;
  vendorName?: string;
}

export type RideStatus =
  | 'Dispatched'
  | 'Confirmed'
  | 'EnRoute'
  | 'Arrived'
  | 'PickedUp'
  | 'AtDestination'
  | 'Dropped'
  | 'Completed'
  | 'Cancelled';

export type DispatchChannel = 'SmsNemt' | 'SmsTaxi' | 'Broker';
