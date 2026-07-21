import { Routes } from '@angular/router';
import { authGuard, orgAdminGuard } from './shared/auth/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'login', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () => import('./login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    loadComponent: () => import('./register/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'invite/:token',
    loadComponent: () =>
      import('./accept-invite/accept-invite.component').then((m) => m.AcceptInviteComponent),
  },
  {
    path: 'terms',
    loadComponent: () => import('./terms/terms.component').then((m) => m.TermsComponent),
  },
  {
    path: 'privacy',
    loadComponent: () => import('./privacy/privacy.component').then((m) => m.PrivacyComponent),
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'residents',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./residents/residents.component').then((m) => m.ResidentsComponent),
  },
  {
    path: 'vendors',
    canActivate: [authGuard],
    loadComponent: () => import('./vendors/vendors.component').then((m) => m.VendorsComponent),
  },
  {
    path: 'history',
    canActivate: [authGuard],
    loadComponent: () => import('./history/history.component').then((m) => m.HistoryComponent),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () => import('./settings/settings.component').then((m) => m.SettingsComponent),
  },
  {
    path: 'booking',
    canActivate: [authGuard],
    loadComponent: () => import('./booking/booking.component').then((m) => m.BookingComponent),
  },
  {
    path: 'rides/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./ride-detail/ride-detail.component').then((m) => m.RideDetailComponent),
  },
  {
    path: 'live-map',
    canActivate: [authGuard],
    loadComponent: () => import('./live-map/live-map.component').then((m) => m.LiveMapComponent),
  },
  {
    path: 'billing',
    canActivate: [authGuard, orgAdminGuard],
    loadComponent: () => import('./billing/billing.component').then((m) => m.BillingComponent),
  },
  {
    path: 'org',
    canActivate: [authGuard, orgAdminGuard],
    loadComponent: () => import('./org/org.component').then((m) => m.OrgComponent),
  },
  {
    path: 'branches',
    canActivate: [authGuard, orgAdminGuard],
    loadComponent: () => import('./branches/branches.component').then((m) => m.BranchesComponent),
  },
  {
    path: 'branches/:id',
    canActivate: [authGuard, orgAdminGuard],
    loadComponent: () => import('./branches/branch-detail.component').then((m) => m.BranchDetailComponent),
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./forgot-password/forgot-password.component').then((m) => m.ForgotPasswordComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./reset-password/reset-password.component').then((m) => m.ResetPasswordComponent),
  },
  { path: '**', redirectTo: 'login' },
];
