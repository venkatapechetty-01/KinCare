import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface AddressSuggestion {
  displayName: string;
  lat: number;
  lon: number;
}

@Injectable({ providedIn: 'root' })
export class GeocodeService {
  private readonly apiUrl = `${environment.apiUrl}/api/geocode`;

  constructor(private http: HttpClient) {}

  autocomplete(query: string): Observable<AddressSuggestion[]> {
    const params = new HttpParams().set('query', query);
    return this.http.get<AddressSuggestion[]>(`${this.apiUrl}/autocomplete`, { params }).pipe(
      catchError(() => of([]))
    );
  }
}
