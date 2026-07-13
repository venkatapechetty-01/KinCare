import { LoginRequest, LoginResponse, RegisterRequest, RegisterResponse, AcceptInviteRequest, CurrentUser } from './auth.model';
import { InviteDetails } from './invitation.model';
import { Resident } from './resident.model';
import { Vendor } from './vendor.model';
import { Ride } from './ride.model';

describe('Model interfaces', () => {
  describe('LoginRequest', () => {
    it('should accept valid shape', () => {
      const req: LoginRequest = { email: 'test@test.com', password: 'pass' };
      expect(req.email).toBe('test@test.com');
      expect(req.password).toBe('pass');
    });
  });

  describe('LoginResponse', () => {
    it('should accept valid shape', () => {
      const res: LoginResponse = {
        accessToken: 'tok',
        refreshToken: 'ref',
        role: 'Coordinator',
        organizationId: 'org-1',
      };
      expect(res.accessToken).toBeTruthy();
      expect(res.facilityId).toBeUndefined();
    });

    it('should accept optional facilityId', () => {
      const res: LoginResponse = {
        accessToken: 'tok',
        refreshToken: 'ref',
        role: 'Coordinator',
        organizationId: 'org-1',
        facilityId: 'fac-1',
      };
      expect(res.facilityId).toBe('fac-1');
    });
  });

  describe('RegisterRequest', () => {
    it('should have all required fields', () => {
      const req: RegisterRequest = {
        role: 'OrgAdmin',
        organizationName: 'Org',
        facilityName: 'Fac',
        facilityAddress: '123 St',
        firstName: 'John',
        lastName: 'Doe',
        email: 'j@d.com',
        password: 'pass1234',
      };
      expect(Object.keys(req).length).toBe(8);
    });
  });

  describe('RegisterResponse', () => {
    it('should have accessToken, refreshToken, orgId, facId, userId', () => {
      const res: RegisterResponse = {
        accessToken: 'tok',
        refreshToken: 'ref',
        organizationId: 'org-1',
        facilityId: 'fac-1',
        userId: 'u-1',
      };
      expect(res.userId).toBe('u-1');
    });
  });

  describe('AcceptInviteRequest', () => {
    it('should accept valid shape', () => {
      const req: AcceptInviteRequest = {
        token: 'abc-123',
        firstName: 'Jane',
        lastName: 'Doe',
        password: 'SecurePass',
      };
      expect(req.token).toBe('abc-123');
    });
  });

  describe('CurrentUser', () => {
    it('should accept Coordinator role', () => {
      const user: CurrentUser = {
        id: 'u-1',
        email: 'user@test.com',
        firstName: 'Test',
        lastName: 'User',
        role: 'OrgAdmin',
        organizationId: 'org-1',
      };
      expect(user.role).toBe('OrgAdmin');
      expect(user.facilityId).toBeUndefined();
    });

    it('should accept OrgAdmin role with facilityId', () => {
      const user: CurrentUser = {
        id: 'u-2',
        email: 'admin@test.com',
        firstName: 'Admin',
        lastName: 'User',
        role: 'OrgAdmin',
        organizationId: 'org-1',
        facilityId: 'fac-1',
      };
      expect(user.role).toBe('OrgAdmin');
      expect(user.facilityId).toBe('fac-1');
    });
  });
});
