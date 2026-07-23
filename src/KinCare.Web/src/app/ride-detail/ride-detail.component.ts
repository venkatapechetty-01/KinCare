import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDividerModule } from '@angular/material/divider';
import { MatListModule } from '@angular/material/list';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RideService, RideDetailDto, DispatchOfferDto } from '../shared/services/ride.service';
import { SignalRService, LocationUpdatedEvent } from '../shared/services/signalr.service';
import { TrustedUrlPipe } from '../shared/pipes/trusted-url.pipe';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-ride-detail',
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
    MatDividerModule,
    MatListModule,
    MatTooltipModule,
    TrustedUrlPipe,
  ],
  templateUrl: './ride-detail.component.html',
  styleUrl: './ride-detail.component.scss',
})
export class RideDetailComponent implements OnInit, OnDestroy {
  rideDetail: RideDetailDto | null = null;
  pendingOffers: DispatchOfferDto[] = [];
  loading = true;
  rideId: string = '';
  private subscriptions = new Subscription();

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private rideService: RideService,
    private signalR: SignalRService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.rideId = this.route.snapshot.paramMap.get('id') || '';
    if (this.rideId) {
      this.loadRideDetail();
      this.connectSignalR();
    }
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private connectSignalR(): void {
    this.signalR.startAsync().catch(() => {});
    this.subscriptions.add(
      this.signalR.rideStatusChanged$.subscribe((event) => {
        if (event.id === this.rideId && this.rideDetail) {
          this.rideDetail = { ...this.rideDetail, status: event.toStatus };
          if (['Completed', 'Cancelled'].includes(event.toStatus))
            this.rideDetail = { ...this.rideDetail, trackingToken: undefined };
          this.loadRideDetail();
        }
      })
    );
    this.subscriptions.add(
      this.signalR.locationUpdated$.subscribe((event: LocationUpdatedEvent) => {
        if (event.rideId === this.rideId && this.rideDetail) {
          this.rideDetail = {
            ...this.rideDetail,
            lastKnownLat: event.latitude,
            lastKnownLng: event.longitude,
            lastLocationAt: event.lastLocationAt,
          };
        }
      })
    );
  }

  loadRideDetail(): void {
    this.loading = true;
    this.subscriptions.add(
      this.rideService.getRideDetail(this.rideId).subscribe({
        next: (detail) => {
          this.rideDetail = detail;
          this.loading = false;
          if (detail.status === 'Dispatched') this.loadPendingOffers();
          else this.pendingOffers = [];
        },
        error: () => { this.snackBar.open('Failed to load ride details.', 'Close', { duration: 5000 }); this.loading = false; },
      })
    );
  }

  private loadPendingOffers(): void {
    this.subscriptions.add(
      this.rideService.getDispatchOffers(this.rideId).subscribe({
        next: (offers) => { this.pendingOffers = offers.filter((o) => o.status === 'Pending' && o.trackingUrl); },
        error: () => {},
      })
    );
  }

  copyOfferLink(offer: DispatchOfferDto): void {
    if (!offer.trackingUrl) return;
    navigator.clipboard.writeText(offer.trackingUrl).then(() =>
      this.snackBar.open(`Accept link for ${offer.vendorName} copied to clipboard!`, 'Close', { duration: 2500 })
    );
  }

  advanceStatus(newStatus: string): void {
    this.subscriptions.add(
      this.rideService.advanceStatus(this.rideId, { newStatus: newStatus as any }).subscribe({
        next: () => { this.snackBar.open('Status updated!', 'Close', { duration: 3000 }); this.loadRideDetail(); },
        error: () => this.snackBar.open('Failed to update status.', 'Close', { duration: 5000 }),
      })
    );
  }

  cancelRide(): void {
    if (!confirm('Cancel this ride?')) return;
    this.subscriptions.add(
      this.rideService.cancelRide(this.rideId).subscribe({
        next: () => { this.snackBar.open('Ride cancelled.', 'Close', { duration: 3000 }); this.loadRideDetail(); },
        error: () => this.snackBar.open('Failed to cancel ride.', 'Close', { duration: 5000 }),
      })
    );
  }

  redispatch(): void {
    if (!confirm('Re-dispatch this cancelled ride to a new vendor?')) return;
    this.subscriptions.add(
      this.rideService.redispatch(this.rideId).subscribe({
        next: (result) => {
          this.snackBar.open(`Re-dispatched via ${this.getChannelLabel(result.dispatchChannel)}!`, 'Close', { duration: 4000 });
          this.router.navigate(['/rides', result.id]);
        },
        error: (err) => this.snackBar.open(err?.error?.error || 'Failed to re-dispatch.', 'Close', { duration: 5000 }),
      })
    );
  }

  copyTrackingLink(): void {
    if (!this.trackingUrl) return;
    navigator.clipboard.writeText(this.trackingUrl).then(() =>
      this.snackBar.open('Tracking link copied to clipboard!', 'Close', { duration: 2500 })
    );
  }

  callVendor(): void {
    if (this.rideDetail?.vendorPhone)
      window.location.href = `tel:${this.rideDetail.vendorPhone}`;
  }

  goBack(): void { this.router.navigate(['/dashboard']); }

  get trackingUrl(): string {
    return this.rideDetail?.trackingToken
      ? `${environment.apiUrl}/track/${this.rideDetail.trackingToken}`
      : '';
  }

  get hasLiveLocation(): boolean {
    return !!(this.rideDetail?.lastKnownLat && this.rideDetail?.lastKnownLng
      && this.rideDetail?.trackingToken && !this.isTerminal);
  }

  get isTerminal(): boolean { return ['Completed', 'Cancelled'].includes(this.rideDetail?.status ?? ''); }
  get isCancelled(): boolean { return this.rideDetail?.status === 'Cancelled'; }

  get mapsEmbedUrl(): string {
    if (!this.rideDetail?.lastKnownLat || !this.rideDetail?.lastKnownLng) return '';
    const key = environment.googleMapsApiKey ? `&key=${environment.googleMapsApiKey}` : '';
    return `https://www.google.com/maps/embed/v1/place?q=${this.rideDetail.lastKnownLat},${this.rideDetail.lastKnownLng}&zoom=15${key}`;
  }

  get mapsDirectLink(): string {
    if (!this.rideDetail?.lastKnownLat || !this.rideDetail?.lastKnownLng) return '';
    return `https://www.google.com/maps?q=${this.rideDetail.lastKnownLat},${this.rideDetail.lastKnownLng}`;
  }


  getLastSeenLabel(): string {
    if (!this.rideDetail?.lastLocationAt) return '';
    const diffMin = Math.floor((Date.now() - new Date(this.rideDetail.lastLocationAt).getTime()) / 60000);
    if (diffMin < 1) return 'Just now';
    if (diffMin === 1) return '1 min ago';
    if (diffMin < 60) return `${diffMin} min ago`;
    return new Date(this.rideDetail.lastLocationAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      Dispatched: 'Awaiting Acceptance', Confirmed: 'Confirmed', EnRoute: 'On the Way',
      Arrived: 'At Facility', PickedUp: 'Picked Up', AtDestination: 'At Destination',
      Dropped: 'Dropped Off', AwaitingReturn: 'Awaiting Return Pickup',
      ReturnEnRoute: 'Returning', ReturnPickedUp: 'Picked Up (Return)',
      Completed: 'Completed', Cancelled: 'Cancelled',
    };
    return labels[status] || status;
  }

  getChannelIcon(channel: string): string {
    const icons: Record<string, string> = {
      SmsNemt: 'accessible', SmsTaxi: 'local_taxi',
      Broker: 'phone',
    };
    return icons[channel] || 'help_outline';
  }

  getChannelLabel(channel: string): string {
    const labels: Record<string, string> = {
      SmsNemt: 'NEMT (SMS)', SmsTaxi: 'Local Taxi (SMS)',
      Broker: 'Roundtrip Broker',
    };
    return labels[channel] || channel;
  }

  canAdvanceTo(status: string): boolean {
    if (!this.rideDetail) return false;
    const t: Record<string, string[]> = {
      Dispatched: ['Confirmed'], Confirmed: ['EnRoute'],
      EnRoute: ['Arrived'], Arrived: ['PickedUp'], PickedUp: ['AtDestination'],
      AtDestination: ['Dropped'], Dropped: ['Completed', 'AwaitingReturn'],
      AwaitingReturn: ['ReturnEnRoute'], ReturnEnRoute: ['ReturnPickedUp'],
      ReturnPickedUp: ['Completed'],
    };
    if (status === 'AwaitingReturn' && this.rideDetail.dispatchChannel !== 'SmsNemt') return false;
    return t[this.rideDetail.status]?.includes(status) ?? false;
  }

  canCancel(): boolean { return !this.isTerminal; }

  readonly statusSteps = [
    { status: 'Dispatched',    label: 'Requested',       icon: 'schedule' },
    { status: 'Confirmed',     label: 'Confirmed',        icon: 'check_circle' },
    { status: 'EnRoute',       label: 'En Route',         icon: 'directions_car' },
    { status: 'Arrived',       label: 'At Facility',      icon: 'location_on' },
    { status: 'PickedUp',      label: 'Picked Up',        icon: 'person' },
    { status: 'AtDestination', label: 'At Destination',   icon: 'local_hospital' },
    { status: 'Dropped',       label: 'Dropped Off',      icon: 'done_all' },
    { status: 'Completed',     label: 'Completed',        icon: 'verified' },
  ];

  private readonly statusOrder = ['Dispatched','Confirmed','EnRoute','Arrived','PickedUp','AtDestination','Dropped','AwaitingReturn','ReturnEnRoute','ReturnPickedUp','Completed','Cancelled'];

  isStatusPast(status: string): boolean {
    if (!this.rideDetail) return false;
    return this.statusOrder.indexOf(status) < this.statusOrder.indexOf(this.rideDetail.status);
  }
}
