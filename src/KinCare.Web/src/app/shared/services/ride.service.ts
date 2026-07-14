import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface RideDto {
  id: string;
  facilityId: string;
  organizationId: string;
  residentId: string;
  vendorId?: string;
  status: string;
  dispatchChannel: string;
  pickupTime: string;
  pickupAddress: string;
  destinationAddress: string;
  trackingToken?: string;
  lastKnownLat?: number;
  lastKnownLng?: number;
  lastLocationAt?: string;
  createdAt: string;
  residentName?: string;
  vendorName?: string;
}

export type DispatchChannelValue = 'SmsNemt' | 'SmsTaxi' | 'Broker';

export interface BookRideRequest {
  facilityId?: string;
  residentId?: string;
  pickupTime: string;
  pickupAddress: string;
  destinationAddress: string;
  preferredChannel?: DispatchChannelValue;
}

export interface AdvanceStatusRequest {
  newStatus: 'Dispatched' | 'Confirmed' | 'EnRoute' | 'Arrived' | 'PickedUp' | 'AtDestination' | 'Dropped' | 'Completed' | 'Cancelled';
  notes?: string;
}

export interface RideEventDto {
  fromStatus: string;
  toStatus: string;
  triggeredBy: string;
  notes?: string;
  occurredAt: string;
}

export interface RideDetailDto {
  id: string;
  facilityId: string;
  residentId?: string;
  residentName: string;
  vendorId?: string;
  vendorName?: string;
  vendorPhone?: string;
  status: string;
  dispatchChannel: string;
  externalTripId?: string;
  pickupTime: string;
  pickupAddress: string;
  destinationAddress: string;
  trackingToken?: string;
  lastKnownLat?: number;
  lastKnownLng?: number;
  lastLocationAt?: string;
  events: RideEventDto[];
}

@Injectable({ providedIn: 'root' })
export class RideService {
  private readonly apiUrl = `${environment.apiUrl}/api/rides`;

  constructor(private http: HttpClient) {}

  getTodaysRides(): Observable<RideDto[]> {
    return this.http.get<RideDto[]>(`${this.apiUrl}/today`);
  }

  getUpcomingRides(): Observable<RideDto[]> {
    return this.http.get<RideDto[]>(`${this.apiUrl}/upcoming`);
  }

  bookRide(request: BookRideRequest): Observable<{ id: string; status: string; dispatchChannel: string; vendorId?: string }> {
    return this.http.post<{ id: string; status: string; dispatchChannel: string; vendorId?: string }>(this.apiUrl, request);
  }

  getRideDetail(id: string): Observable<RideDetailDto> {
    return this.http.get<RideDetailDto>(`${this.apiUrl}/${id}`);
  }

  getHistory(params?: { startDate?: string; endDate?: string; status?: string; page?: number; pageSize?: number }): Observable<{ items: RideDto[]; totalCount: number }> {
    return this.http.get<{ items: RideDto[]; totalCount: number }>(`${this.apiUrl}/history`, { params: params as Record<string, string> });
  }

  advanceStatus(id: string, request: AdvanceStatusRequest): Observable<{ id: string; status: string }> {
    return this.http.put<{ id: string; status: string }>(`${this.apiUrl}/${id}/status`, request);
  }

  cancelRide(id: string): Observable<{ id: string; status: string }> {
    return this.http.delete<{ id: string; status: string }>(`${this.apiUrl}/${id}`);
  }

  redispatch(id: string): Observable<{ id: string; status: string; dispatchChannel: string }> {
    return this.http.post<{ id: string; status: string; dispatchChannel: string }>(`${this.apiUrl}/${id}/redispatch`, {});
  }

  getTodayCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.apiUrl}/today/count`);
  }
}
