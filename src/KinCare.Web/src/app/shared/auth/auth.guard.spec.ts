import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { authGuard, orgAdminGuard, coordinatorGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

describe('Auth Guards', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let mockUrlTree: UrlTree;

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['isAuthenticated', 'hasRole']);
    routerSpy = jasmine.createSpyObj('Router', ['createUrlTree']);
    mockUrlTree = {} as UrlTree;
    routerSpy.createUrlTree.and.returnValue(mockUrlTree);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
      ],
    });
  });

  describe('authGuard', () => {
    it('should allow access when authenticated', () => {
      authServiceSpy.isAuthenticated.and.returnValue(true);

      const result = TestBed.runInInjectionContext(() =>
        authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(result).toBeTrue();
    });

    it('should redirect to /login when not authenticated', () => {
      authServiceSpy.isAuthenticated.and.returnValue(false);

      const result = TestBed.runInInjectionContext(() =>
        authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(routerSpy.createUrlTree).toHaveBeenCalledWith(['/login']);
      expect(result).toBe(mockUrlTree);
    });
  });

  describe('orgAdminGuard', () => {
    it('should allow access for OrgAdmin role', () => {
      authServiceSpy.hasRole.and.callFake((role: string) => role === 'OrgAdmin');

      const result = TestBed.runInInjectionContext(() =>
        orgAdminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(result).toBeTrue();
    });

    it('should allow access for SuperAdmin role', () => {
      authServiceSpy.hasRole.and.callFake((role: string) => role === 'SuperAdmin');

      const result = TestBed.runInInjectionContext(() =>
        orgAdminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(result).toBeTrue();
    });

    it('should redirect Coordinator to /dashboard', () => {
      authServiceSpy.hasRole.and.returnValue(false);

      const result = TestBed.runInInjectionContext(() =>
        orgAdminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(routerSpy.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
      expect(result).toBe(mockUrlTree);
    });
  });

  describe('coordinatorGuard', () => {
    it('should allow access when authenticated', () => {
      authServiceSpy.isAuthenticated.and.returnValue(true);

      const result = TestBed.runInInjectionContext(() =>
        coordinatorGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(result).toBeTrue();
    });

    it('should redirect to /login when not authenticated', () => {
      authServiceSpy.isAuthenticated.and.returnValue(false);

      const result = TestBed.runInInjectionContext(() =>
        coordinatorGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)
      );

      expect(routerSpy.createUrlTree).toHaveBeenCalledWith(['/login']);
      expect(result).toBe(mockUrlTree);
    });
  });
});
