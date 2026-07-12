export interface InviteDetails {
  email: string;
  role: string;
  organizationName: string;
  facilityName?: string;
}

export interface InviteResponse {
  token: string;
  expiresAt: string;
}
