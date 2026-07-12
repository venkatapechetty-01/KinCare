import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, map, tap, throwError } from 'rxjs';
import { Router } from '@angular/router';
import {
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  AcceptInviteRequest,
  CurrentUser,
} from '../models/auth.model';
import { DeviceService } from '../services/device.service';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly apiUrl = `${environment.apiUrl}/api`;
  private currentUserSubject = new BehaviorSubject<CurrentUser | null>(null);

  currentUser$ = this.currentUserSubject.asObservable();

  get currentUser(): CurrentUser | null {
    return this.currentUserSubject.value;
  }

  constructor(
    private http: HttpClient,
    private router: Router,
    private deviceService: DeviceService,
  ) {
    this.loadUserFromToken();
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/auth/login`, request).pipe(
      tap((response) => {
        localStorage.setItem('access_token', response.accessToken);
        localStorage.setItem('refresh_token', response.refreshToken);
        this.loadUserFromToken();
        this.deviceService.registerFcmToken();
      })
    );
  }

  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.apiUrl}/onboarding/register`, request).pipe(
      tap((response) => {
        localStorage.setItem('access_token', response.accessToken);
        localStorage.setItem('refresh_token', response.refreshToken);
        this.loadUserFromToken();
        this.deviceService.registerFcmToken();
      })
    );
  }

  acceptInvite(request: AcceptInviteRequest): Observable<{ accessToken: string; refreshToken: string; userId: string }> {
    return this.http.post<{ accessToken: string; refreshToken: string; userId: string }>(
      `${this.apiUrl}/onboarding/accept`,
      request
    ).pipe(
      tap((response) => {
        localStorage.setItem('access_token', response.accessToken);
        localStorage.setItem('refresh_token', response.refreshToken);
        this.loadUserFromToken();
        this.deviceService.registerFcmToken();
      })
    );
  }

  logout(): void {
    localStorage.removeItem('access_token');
    localStorage.removeItem('refresh_token');
    this.currentUserSubject.next(null);
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem('access_token');
  }

  refreshAccessToken(): Observable<string> {
    const refreshToken = localStorage.getItem('refresh_token');
    if (!refreshToken) {
      this.logout();
      return throwError(() => new Error('No refresh token'));
    }
    return this.http
      .post<{ accessToken: string; refreshToken: string }>(
        `${this.apiUrl}/auth/refresh`,
        { refreshToken }
      )
      .pipe(
        tap((res) => {
          localStorage.setItem('access_token', res.accessToken);
          localStorage.setItem('refresh_token', res.refreshToken);
          this.loadUserFromToken();
        }),
        map((res) => res.accessToken)
      );
  }

  isAuthenticated(): boolean {
    if (this.currentUserSubject.value === null) return false;
    const token = this.getToken();
    if (!token) return false;
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 > Date.now();
    } catch {
      return false;
    }
  }

  hasRole(role: string): boolean {
    return this.currentUserSubject.value?.role === role;
  }

  private loadUserFromToken(): void {
    const token = this.getToken();
    if (!token) {
      this.currentUserSubject.next(null);
      return;
    }
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      if (payload.exp * 1000 <= Date.now()) {
        localStorage.removeItem('access_token');
        this.currentUserSubject.next(null);
        return;
      }
      this.currentUserSubject.next({
        id: payload.sub,
        email: payload.email,
        firstName: payload.first_name,
        lastName: payload.last_name,
        role: payload.role,
        organizationId: payload.organization_id,
        facilityId: payload.facility_id,
      });
    } catch {
      this.currentUserSubject.next(null);
    }
  }
}
