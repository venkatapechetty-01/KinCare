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
import { SignalRService } from '../shared/services/signalr.service';
import { Subscription } from 'rxjs';

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
  upcomingRides: RideDto[] = [];
  loadingUpcoming = true;
  currentDate = new Date();
  private subscriptions = new Subscription();

  constructor(
    private router: Router,
    private rideService: RideService,
    private snackBar: MatSnackBar,
    private signalR: SignalRService
  ) {}

  ngOnInit(): void {
    this.loadTodaysRides();
    this.loadUpcomingRides();
    this.connectSignalR();
  }

  ngAfterViewInit(): void {
    this.animateCards();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private connectSignalR(): void {
    this.signalR.startAsync().catch(err => {
      console.warn('SignalR connection failed (real-time updates unavailable):', err);
    });

    this.subscriptions.add(
      this.signalR.rideStatusChanged$.subscribe((e) => {
        const ride = this.rides.find(r => r.id === e.id);
        const upcomingRide = this.upcomingRides.find(r => r.id === e.id);
        if (ride) {
          ride.status = e.toStatus;
        } else if (upcomingRide) {
          upcomingRide.status = e.toStatus;
        }
      })
    );

    this.subscriptions.add(
      this.signalR.rideCreated$.subscribe(() => {
        // A new ride was dispatched for this facility — reload to pick up the full card data.
        this.loadTodaysRides();
        this.loadUpcomingRides();
      })
    );

    this.subscriptions.add(
      this.signalR.locationUpdated$.subscribe((e) => {
        const ride = this.rides.find(r => r.id === e.rideId) ?? this.upcomingRides.find(r => r.id === e.rideId);
        if (ride) {
          ride.lastKnownLat = e.latitude;
          ride.lastKnownLng = e.longitude;
          ride.lastLocationAt = e.lastLocationAt;
        }
      })
    );
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

  loadUpcomingRides(): void {
    this.loadingUpcoming = true;
    this.subscriptions.add(
      this.rideService.getUpcomingRides().subscribe({
        next: (rides) => {
          this.upcomingRides = rides;
          this.loadingUpcoming = false;
          setTimeout(() => this.animateCards(), 0);
        },
        error: (error) => {
          console.error('Error loading upcoming rides:', error);
          this.loadingUpcoming = false;
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
      'Dispatched':    'Awaiting Acceptance',
      'Confirmed':     'Confirmed',
      'EnRoute':       'On the Way',
      'Arrived':       'At Facility',
      'PickedUp':      'Picked Up',
      'AtDestination': 'At Destination',
      'Dropped':       'Dropped Off',
      'AwaitingReturn':  'Awaiting Return Pickup',
      'ReturnEnRoute':   'Returning',
      'ReturnPickedUp':  'Picked Up (Return)',
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
