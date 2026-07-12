import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};

export const orgAdminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasRole('OrgAdmin') || auth.hasRole('SuperAdmin')) {
    return true;
  }
  return router.createUrlTree(['/dashboard']);
};

export const facilityAdminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.hasRole('FacilityAdmin') || auth.hasRole('OrgAdmin') || auth.hasRole('SuperAdmin')) {
    return true;
  }
  return router.createUrlTree(['/dashboard']);
};
