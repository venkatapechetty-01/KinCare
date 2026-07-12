import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Resident, CreateResidentRequest, UpdateResidentRequest } from '../models/resident.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ResidentService {
  private readonly apiUrl = `${environment.apiUrl}/api/residents`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<Resident[]> {
    return this.http.get<Resident[]>(this.apiUrl);
  }

  create(request: CreateResidentRequest): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.apiUrl, request);
  }

  update(id: string, request: UpdateResidentRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
