import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Vendor, CreateVendorRequest, UpdateVendorRequest } from '../models/vendor.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class VendorService {
  private readonly apiUrl = `${environment.apiUrl}/api/vendors`;

  constructor(private http: HttpClient) {}

  getAll(type?: string): Observable<Vendor[]> {
    let params = new HttpParams();
    if (type) {
      params = params.set('type', type);
    }
    return this.http.get<Vendor[]>(this.apiUrl, { params });
  }

  create(request: CreateVendorRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.apiUrl, request);
  }

  update(id: string, request: UpdateVendorRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  uploadPhoto(id: string, file: File): Observable<{ photoUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ photoUrl: string }>(`${this.apiUrl}/${id}/photo`, formData);
  }

  removePhoto(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/photo`);
  }
}
