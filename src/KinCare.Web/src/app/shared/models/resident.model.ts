export interface Resident {
  id: string;
  facilityId: string;
  firstName: string;
  lastName: string;
  needsWheelchair: boolean;
  needsOxygen: boolean;
  needsStretcher: boolean;
  needsWalker: boolean;
  driverNotes?: string;
}

export interface CreateResidentRequest {
  facilityId?: string;
  firstName: string;
  lastName: string;
  needsWheelchair: boolean;
  needsOxygen: boolean;
  needsStretcher: boolean;
  needsWalker: boolean;
  driverNotes?: string;
}

export interface UpdateResidentRequest {
  firstName: string;
  lastName: string;
  needsWheelchair: boolean;
  needsOxygen: boolean;
  needsStretcher: boolean;
  needsWalker: boolean;
  driverNotes?: string;
}
