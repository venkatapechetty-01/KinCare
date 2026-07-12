export interface Vendor {
  id: string;
  facilityId: string;
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
  isActive: boolean;
}

export interface CreateVendorRequest {
  facilityId?: string;
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
}

export interface UpdateVendorRequest {
  name: string;
  phoneNumber: string;
  vendorType: 'Wheelchair' | 'Ambulatory';
  dispatchMethod: 'SmsNemt' | 'SmsTaxi' | 'Broker';
  capabilityTier: 'Basic' | 'Smart';
}
