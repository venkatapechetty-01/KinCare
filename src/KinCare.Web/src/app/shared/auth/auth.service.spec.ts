import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { LoginRequest, RegisterRequest } from '../models/auth.model';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    localStorage.clear();

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        AuthService,
        { provide: Router, useValue: routerSpy },
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('login', () => {
    it('should POST to /api/auth/login', () => {
      const request: LoginRequest = { email: 'test@test.com', password: 'pass123' };

      service.login(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/auth/login');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({
        accessToken: createFakeJwt(),
        refreshToken: 'refresh-123',
        role: 'Coordinator',
        organizationId: 'org-1',
      });
    });

    it('should store tokens in localStorage on success', () => {
      const request: LoginRequest = { email: 'test@test.com', password: 'pass' };

      service.login(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/auth/login');
      req.flush({
        accessToken: createFakeJwt(),
        refreshToken: 'refresh-abc',
        role: 'Coordinator',
        organizationId: 'org-1',
      });

      expect(localStorage.getItem('access_token')).toBeTruthy();
      expect(localStorage.getItem('refresh_token')).toBe('refresh-abc');
    });

    it('should update currentUser$ after login', () => {
      const request: LoginRequest = { email: 'test@test.com', password: 'pass' };
      let user: any = null;
      service.currentUser$.subscribe((u) => (user = u));

      service.login(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/auth/login');
      req.flush({
        accessToken: createFakeJwt({ email: 'test@test.com', role: 'Coordinator' }),
        refreshToken: 'r-1',
        role: 'Coordinator',
        organizationId: 'org-1',
      });

      expect(user).toBeTruthy();
      expect(user.email).toBe('test@test.com');
      expect(user.role).toBe('Coordinator');
    });
  });

  describe('register', () => {
    it('should POST to /api/onboarding/register', () => {
      const request: RegisterRequest = {
        organizationName: 'Org',
        facilityName: 'Fac',
        facilityAddress: '123 St',
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        password: 'Password1!',
      };

      service.register(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/onboarding/register');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({
        accessToken: createFakeJwt(),
        refreshToken: 'r-1',
        organizationId: 'org-1',
        facilityId: 'fac-1',
        userId: 'user-1',
      });
    });

    it('should store tokens on successful registration', () => {
      const request: RegisterRequest = {
        organizationName: 'O',
        facilityName: 'F',
        facilityAddress: 'A',
        firstName: 'J',
        lastName: 'D',
        email: 'j@d.com',
        password: 'Pass1234!',
      };

      service.register(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/onboarding/register');
      req.flush({
        accessToken: createFakeJwt(),
        refreshToken: 'reg-refresh',
        organizationId: 'org-1',
        facilityId: 'fac-1',
        userId: 'user-1',
      });

      expect(localStorage.getItem('refresh_token')).toBe('reg-refresh');
    });
  });

  describe('logout', () => {
    it('should clear localStorage', () => {
      localStorage.setItem('access_token', 'tok');
      localStorage.setItem('refresh_token', 'ref');

      service.logout();

      expect(localStorage.getItem('access_token')).toBeNull();
      expect(localStorage.getItem('refresh_token')).toBeNull();
    });

    it('should set currentUser$ to null', () => {
      localStorage.setItem('access_token', createFakeJwt());
      let user: any = 'initial';
      service.currentUser$.subscribe((u) => (user = u));

      service.logout();

      expect(user).toBeNull();
    });

    it('should navigate to /login', () => {
      service.logout();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    });
  });

  describe('getToken', () => {
    it('should return token from localStorage', () => {
      localStorage.setItem('access_token', 'my-token');
      expect(service.getToken()).toBe('my-token');
    });

    it('should return null when no token', () => {
      expect(service.getToken()).toBeNull();
    });
  });

  describe('isAuthenticated', () => {
    it('should return false when no token', () => {
      expect(service.isAuthenticated()).toBeFalse();
    });

    it('should return true when valid token exists', () => {
      localStorage.setItem('access_token', createFakeJwt());
      const freshService = TestBed.inject(AuthService);
      expect(freshService.isAuthenticated()).toBeTrue();
    });
  });

  describe('hasRole', () => {
    it('should return true when user has matching role', () => {
      localStorage.setItem('access_token', createFakeJwt({ role: 'OrgAdmin' }));
      const freshService = new AuthService(
        TestBed.inject(HttpClientTestingModule as any),
        routerSpy
      );
      // Use service after login to test role
      service.login({ email: 'a@b.com', password: 'p' }).subscribe();
      const req = httpMock.expectOne('http://localhost:5000/api/auth/login');
      req.flush({
        accessToken: createFakeJwt({ role: 'OrgAdmin' }),
        refreshToken: 'r',
        role: 'OrgAdmin',
        organizationId: 'o',
      });

      expect(service.hasRole('OrgAdmin')).toBeTrue();
    });

    it('should return false when user has different role', () => {
      service.login({ email: 'a@b.com', password: 'p' }).subscribe();
      const req = httpMock.expectOne('http://localhost:5000/api/auth/login');
      req.flush({
        accessToken: createFakeJwt({ role: 'Coordinator' }),
        refreshToken: 'r',
        role: 'Coordinator',
        organizationId: 'o',
      });

      expect(service.hasRole('OrgAdmin')).toBeFalse();
    });
  });

  describe('acceptInvite', () => {
    it('should POST to /api/onboarding/accept', () => {
      const request = { token: 'abc', firstName: 'A', lastName: 'B', password: 'Pass1!' };

      service.acceptInvite(request).subscribe();

      const req = httpMock.expectOne('http://localhost:5000/api/onboarding/accept');
      expect(req.request.method).toBe('POST');
      req.flush({
        accessToken: createFakeJwt(),
        refreshToken: 'ref',
        userId: 'u-1',
      });
    });
  });
});

function createFakeJwt(claims: any = {}): string {
  const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
  const payload = btoa(
    JSON.stringify({
      sub: claims.sub || 'user-123',
      email: claims.email || 'test@test.com',
      first_name: claims.first_name || 'Test',
      last_name: claims.last_name || 'User',
      role: claims.role || 'Coordinator',
      organization_id: claims.organization_id || 'org-123',
      facility_id: claims.facility_id || 'fac-123',
      exp: Math.floor(Date.now() / 1000) + 3600,
    })
  );
  const signature = 'fake-signature';
  return `${header}.${payload}.${signature}`;
}
