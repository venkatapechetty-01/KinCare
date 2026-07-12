export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  role: string;
  organizationId: string;
  facilityId?: string;
}

export interface RegisterRequest {
  organizationName: string;
  facilityName?: string;
  facilityAddress?: string;
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: 'OrgAdmin' | 'FacilityAdmin';
}

export interface RegisterResponse {
  accessToken: string;
  refreshToken: string;
  organizationId: string;
  facilityId: string;
  userId: string;
}

export interface AcceptInviteRequest {
  token: string;
  firstName: string;
  lastName: string;
  password: string;
}

export interface CurrentUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: 'SuperAdmin' | 'OrgAdmin' | 'FacilityAdmin';
  organizationId: string;
  facilityId?: string;
  organizationName?: string;
  licenseNumber?: string;
}
