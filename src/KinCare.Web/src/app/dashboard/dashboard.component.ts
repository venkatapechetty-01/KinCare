import { Component, OnInit, OnDestroy, AfterViewInit } from '@angular/core';
import { gsap } from 'gsap';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RideService, RideDto } from '../shared/services/ride.service';
import { AuthService } from '../shared/auth/auth.service';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import * as signalR from '@microsoft/signalr';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit, AfterViewInit, OnDestroy {
  rides: RideDto[] = [];
  loading = true;
  currentDate = new Date();
  private subscriptions = new Subscription();
  private hubConnection: signalR.HubConnection | null = null;

  constructor(
    private router: Router,
    private rideService: RideService,
    private snackBar: MatSnackBar,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    this.loadTodaysRides();
    this.connectSignalR();
  }

  ngAfterViewInit(): void {
    this.animateCards();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.hubConnection?.stop();
  }

  private connectSignalR(): void {
    const token = this.auth.getToken();
    if (!token) return;

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/ride-status`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hubConnection.on('RideStatusChanged', (rideId: string, newStatus: string) => {
      const ride = this.rides.find(r => r.id === rideId);
      if (ride) {
        ride.status = newStatus;
      } else {
        // New ride dispatched — reload to get full card data
        this.loadTodaysRides();
      }
    });

    this.hubConnection.on('LocationUpdated', (rideId: string, lat: number, lng: number) => {
      const ride = this.rides.find(r => r.id === rideId);
      if (ride) {
        ride.lastKnownLat = lat;
        ride.lastKnownLng = lng;
        ride.lastLocationAt = new Date().toISOString();
      }
    });

    this.hubConnection.onreconnecting(() => {
      console.warn('SignalR reconnecting...');
    });

    this.hubConnection.start().catch(err => {
      console.warn('SignalR connection failed (real-time updates unavailable):', err);
    });
  }

  loadTodaysRides(): void {
    this.loading = true;
    this.subscriptions.add(
      this.rideService.getTodaysRides().subscribe({
        next: (rides) => {
          this.rides = rides;
          this.loading = false;
          setTimeout(() => this.animateCards(), 0);
        },
        error: (error) => {
          console.error('Error loading rides:', error);
          this.snackBar.open('Failed to load today\'s rides. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar']
          });
          this.loading = false;
        }
      })
    );
  }

  bookRide(): void {
    this.router.navigate(['/booking']);
  }

  viewRideDetail(id: string): void {
    this.router.navigate(['/rides', id]);
  }

  isUrgent(ride: RideDto): boolean {
    const pickupTime = new Date(ride.pickupTime);
    const now = new Date();
    const diff = pickupTime.getTime() - now.getTime();
    return diff < 30 * 60 * 1000 && diff > 0;
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      'Dispatched':    'Dispatched',
      'Confirmed':     'Confirmed',
      'EnRoute':       'On the Way',
      'Arrived':       'At Facility',
      'PickedUp':      'Picked Up',
      'AtDestination': 'At Destination',
      'Dropped':       'Dropped Off',
      'Completed':     'Completed',
      'Cancelled':     'Cancelled'
    };
    return labels[status] || status;
  }

  getChannelIcon(channel: string): string {
    const icons: Record<string, string> = {
      'SmsNemt': 'sms',
      'SmsTaxi': 'local_taxi',
      'Broker': 'phone'
    };
    return icons[channel] || 'help_outline';
  }

  private animateCards(): void {
    gsap.from('.ride-card', {
      duration: 0.4,
      y: 24,
      opacity: 0,
      stagger: 0.08,
      ease: 'power2.out',
      clearProps: 'all'
    });
  }

  animateStatusBadge(element: HTMLElement): void {
    gsap.fromTo(element, { scale: 1.3 }, { scale: 1, duration: 0.3, ease: 'back.out(2)' });
  }
}
