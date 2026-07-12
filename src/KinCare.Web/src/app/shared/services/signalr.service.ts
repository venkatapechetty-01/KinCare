import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export interface RideStatusChangedEvent {
  id: string;
  fromStatus: string;
  toStatus: string;
  residentId?: string;
  vendorId?: string;
}

export interface LocationUpdatedEvent {
  rideId: string;
  latitude: number;
  longitude: number;
  lastLocationAt: string;
}

@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private connection: signalR.HubConnection | null = null;

  readonly rideStatusChanged$ = new Subject<RideStatusChangedEvent>();
  readonly rideCreated$ = new Subject<{ id: string; status: string; dispatchChannel: string }>();
  readonly locationUpdated$ = new Subject<LocationUpdatedEvent>();

  constructor(private auth: AuthService) {}

  async startAsync(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/ride-status`, {
        accessTokenFactory: () => this.auth.getToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('RideStatusChanged', (data: RideStatusChangedEvent) => {
      this.rideStatusChanged$.next(data);
    });

    this.connection.on('RideCreated', (data: { id: string; status: string; dispatchChannel: string }) => {
      this.rideCreated$.next(data);
    });

    this.connection.on('LocationUpdated', (data: LocationUpdatedEvent) => {
      this.locationUpdated$.next(data);
    });

    await this.connection.start();
  }

  async stopAsync(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }

  ngOnDestroy(): void {
    this.stopAsync();
  }
}
