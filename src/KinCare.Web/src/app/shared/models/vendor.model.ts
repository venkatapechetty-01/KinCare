export interface Vendor {
  id: string;
  facilityId: string;
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
  isActive: boolean;
  photoUrl?: string;
  company?: string;
  serviceArea?: string;
}

export interface CreateVendorRequest {
  facilityId?: string;
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
  company?: string;
  serviceArea?: string;
}

export interface UpdateVendorRequest {
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
  company?: string;
  serviceArea?: string;
}
