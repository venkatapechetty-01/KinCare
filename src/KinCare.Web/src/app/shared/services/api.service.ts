import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { z } from 'zod';
import {
  ResidentListSchema, ResidentSchema,
  VendorListSchema, VendorSchema,
  RideSummaryListSchema, RideSummarySchema,
  RideDetailSchema, RideHistorySchema,
  ActiveRideLocationListSchema,
  DispatchOfferListSchema,
  FacilityListSchema, FacilitySchema,
  OrgUserListSchema,
  FacilityMetricsListSchema,
  LoginResponseSchema, RegisterResponseSchema,
  InviteDetailsSchema,
  BookRideResponseSchema,
  TodayCountSchema,
  Resident, Vendor, RideSummary, RideDetail,
  ActiveRideLocation, DispatchOffer,
  Facility, OrgUser, FacilityMetrics,
  LoginResponse, RegisterResponse, InviteDetails,
  BookRideResponse,
} from '../schemas/api.schemas';
import { environment } from '../../../environments/environment';

function parse<T>(schema: z.ZodType<T>) {
  return map((data: unknown): T => schema.parse(data));
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);
  private api = environment.apiUrl;

  // ── Auth ──────────────────────────────────────────────────────────────────

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post(`${this.api}/api/auth/login`, { email, password }).pipe(
      parse(LoginResponseSchema),
    );
  }

  register(body: object): Observable<RegisterResponse> {
    return this.http.post(`${this.api}/api/onboarding/register`, body).pipe(
      parse(RegisterResponseSchema),
    );
  }

  getInviteDetails(token: string): Observable<InviteDetails> {
    return this.http.get(`${this.api}/api/onboarding/invite/${token}`).pipe(
      parse(InviteDetailsSchema),
    );
  }

  acceptInvite(body: object): Observable<LoginResponse> {
    return this.http.post(`${this.api}/api/onboarding/accept`, body).pipe(
      parse(LoginResponseSchema),
    );
  }

  // ── Residents ─────────────────────────────────────────────────────────────

  getResidents(): Observable<Resident[]> {
    return this.http.get(`${this.api}/api/residents`).pipe(
      parse(ResidentListSchema),
    );
  }

  createResident(body: object): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.api}/api/residents`, body);
  }

  updateResident(id: string, body: object): Observable<void> {
    return this.http.put<void>(`${this.api}/api/residents/${id}`, body);
  }

  deleteResident(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/api/residents/${id}`);
  }

  // ── Vendors ───────────────────────────────────────────────────────────────

  getVendors(type?: string): Observable<Vendor[]> {
    let params = new HttpParams();
    if (type) params = params.set('type', type);
    return this.http.get(`${this.api}/api/vendors`, { params }).pipe(
      parse(VendorListSchema),
    );
  }

  createVendor(body: object): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.api}/api/vendors`, body);
  }

  updateVendor(id: string, body: object): Observable<void> {
    return this.http.put<void>(`${this.api}/api/vendors/${id}`, body);
  }

  deleteVendor(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/api/vendors/${id}`);
  }

  // ── Rides ─────────────────────────────────────────────────────────────────

  getTodaysRides(): Observable<RideSummary[]> {
    return this.http.get(`${this.api}/api/rides/today`).pipe(
      parse(RideSummaryListSchema),
    );
  }

  getTodayCount(): Observable<number> {
    return this.http.get(`${this.api}/api/rides/today/count`).pipe(
      parse(TodayCountSchema),
      map((r) => r.count),
    );
  }

  bookRide(body: object): Observable<BookRideResponse> {
    return this.http.post(`${this.api}/api/rides`, body).pipe(
      parse(BookRideResponseSchema),
    );
  }

  getRideDetail(id: string): Observable<RideDetail> {
    return this.http.get(`${this.api}/api/rides/${id}`).pipe(
      parse(RideDetailSchema),
    );
  }

  advanceStatus(id: string, body: object): Observable<{ id: string; status: string }> {
    return this.http.put<{ id: string; status: string }>(`${this.api}/api/rides/${id}/status`, body);
  }

  cancelRide(id: string): Observable<{ id: string; status: string }> {
    return this.http.delete<{ id: string; status: string }>(`${this.api}/api/rides/${id}`);
  }

  redispatch(id: string): Observable<{ id: string; status: string; dispatchChannel: string }> {
    return this.http.post<{ id: string; status: string; dispatchChannel: string }>(
      `${this.api}/api/rides/${id}/redispatch`, {},
    );
  }

  getRideHistory(params?: {
    startDate?: string; endDate?: string; status?: string;
    page?: number; pageSize?: number;
  }): Observable<{ items: RideSummary[]; totalCount: number }> {
    return this.http.get(`${this.api}/api/rides/history`, {
      params: params as Record<string, string>,
    }).pipe(parse(RideHistorySchema));
  }

  getDispatchOffers(rideId: string): Observable<DispatchOffer[]> {
    return this.http.get(`${this.api}/api/rides/${rideId}/offers`).pipe(
      parse(DispatchOfferListSchema),
    );
  }

  // ── Live map ──────────────────────────────────────────────────────────────

  getLiveMap(): Observable<ActiveRideLocation[]> {
    return this.http.get(`${this.api}/api/org/live-map`).pipe(
      parse(ActiveRideLocationListSchema),
    );
  }

  // ── Org Admin ─────────────────────────────────────────────────────────────

  getFacilities(): Observable<Facility[]> {
    return this.http.get(`${this.api}/api/org/facilities`).pipe(
      parse(FacilityListSchema),
    );
  }

  getFacility(id: string): Observable<Facility> {
    return this.http.get(`${this.api}/api/org/facilities/${id}`).pipe(
      parse(FacilitySchema),
    );
  }

  createFacility(body: object): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(`${this.api}/api/org/facilities`, body);
  }

  getOrgUsers(): Observable<OrgUser[]> {
    return this.http.get(`${this.api}/api/org/users`).pipe(
      parse(OrgUserListSchema),
    );
  }

  inviteUser(body: object): Observable<{ token: string; expiresAt: string }> {
    return this.http.post<{ token: string; expiresAt: string }>(`${this.api}/api/org/invite`, body);
  }

  deactivateUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/api/org/users/${id}`);
  }

  getOrgMetrics(): Observable<FacilityMetrics[]> {
    return this.http.get(`${this.api}/api/org/metrics`).pipe(
      parse(FacilityMetricsListSchema),
    );
  }
}
