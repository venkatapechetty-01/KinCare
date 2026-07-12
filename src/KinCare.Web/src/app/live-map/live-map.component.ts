import { Component, OnInit, OnDestroy, ElementRef, ViewChild, AfterViewInit, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router, ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatChipsModule } from '@angular/material/chips';
import { setOptions, importLibrary } from '@googlemaps/js-api-loader';
import { SignalRService, LocationUpdatedEvent, RideStatusChangedEvent } from '../shared/services/signalr.service';
import { ApiService } from '../shared/services/api.service';
import { AuthService } from '../shared/auth/auth.service';
import { ActiveRideLocation } from '../shared/schemas/api.schemas';
import { lastSeenLabel } from '../shared/utils/date.utils';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';

interface MapMarkerState {
  marker: google.maps.Marker;
  infoWindow: google.maps.InfoWindow;
  rideId: string;
}

@Component({
  selector: 'app-live-map',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatChipsModule,
  ],
  templateUrl: './live-map.component.html',
  styleUrl: './live-map.component.scss',
})
export class LiveMapComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('mapCanvas') mapCanvas!: ElementRef<HTMLDivElement>;

  rides: ActiveRideLocation[] = [];
  selectedRide: ActiveRideLocation | null = null;
  loading = true;
  mapReady = false;
  focusedRideId: string | null = null;

  private map: google.maps.Map | null = null;
  private markers = new Map<string, MapMarkerState>();
  private openInfoWindow: google.maps.InfoWindow | null = null;
  private subs = new Subscription();

  constructor(
    private api: ApiService,
    private signalR: SignalRService,
    private router: Router,
    private route: ActivatedRoute,
    private auth: AuthService,
    private zone: NgZone
  ) {}

  get isOrgAdmin(): boolean {
    return this.auth.currentUser?.role === 'OrgAdmin';
  }

  ngOnInit(): void {
    // Support ?rideId=xxx to focus a specific ride (for facility admin per-ride tracking)
    this.focusedRideId = this.route.snapshot.queryParamMap.get('rideId');

    this.loadRides();
    this.signalR.startAsync().catch(() => {});

    this.subs.add(
      this.signalR.locationUpdated$.subscribe((e: LocationUpdatedEvent) => {
        this.zone.run(() => {
          const ride = this.rides.find(r => r.id === e.rideId);
          if (ride) {
            ride.lat = e.latitude;
            ride.lng = e.longitude;
            ride.lastLocationAt = e.lastLocationAt;
            if (this.selectedRide?.id === e.rideId) this.selectedRide = { ...ride };
            this.moveMarker(ride);
          }
        });
      })
    );

    this.subs.add(
      this.signalR.rideStatusChanged$.subscribe((e: RideStatusChangedEvent) => {
        this.zone.run(() => {
          if (['Completed', 'Cancelled'].includes(e.toStatus)) {
            this.removeMarker(e.id);
            this.rides = this.rides.filter(r => r.id !== e.id);
            if (this.selectedRide?.id === e.id) {
              this.selectedRide = this.rides[0] ?? null;
              if (this.selectedRide) this.panToRide(this.selectedRide);
            }
          } else {
            const ride = this.rides.find(r => r.id === e.id);
            if (ride) {
              ride.status = e.toStatus as typeof ride.status;
              this.updateMarkerIcon(ride);
            }
          }
        });
      })
    );

    this.subs.add(
      this.signalR.rideCreated$.subscribe(() => this.zone.run(() => this.loadRides()))
    );
  }

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    this.markers.forEach(m => m.marker.setMap(null));
    this.markers.clear();
  }

  private async initMap(): Promise<void> {
    setOptions({
      key: environment.googleMapsApiKey || '',
      v: 'weekly',
    });

    try {
      await importLibrary('maps');
      this.map = new google.maps.Map(this.mapCanvas.nativeElement, {
        center: { lat: 42.5, lng: -83.2 }, // Michigan center
        zoom: 10,
        mapTypeId: 'roadmap',
        disableDefaultUI: false,
        zoomControl: true,
        streetViewControl: false,
        mapTypeControl: false,
        fullscreenControl: true,
        styles: [
          { featureType: 'poi', elementType: 'labels', stylers: [{ visibility: 'off' }] },
          { featureType: 'transit', stylers: [{ visibility: 'simplified' }] },
        ],
      });
      this.mapReady = true;
      // Place markers for any rides already loaded
      this.rides.forEach(r => this.addOrUpdateMarker(r));
      if (this.rides.length > 0) this.fitMapToMarkers();
    } catch (e) {
      console.error('[LiveMap] Google Maps failed to load:', e);
    }
  }

  loadRides(): void {
    this.api.getLiveMap().subscribe({
      next: (rides) => {
        this.rides = this.focusedRideId
          ? rides.filter(r => r.id === this.focusedRideId)
          : rides;

        if (!this.selectedRide && this.rides.length > 0)
          this.selectedRide = this.rides[0];

        this.loading = false;

        if (this.mapReady) {
          this.rides.forEach(r => this.addOrUpdateMarker(r));
          this.fitMapToMarkers();
        }
      },
      error: () => { this.loading = false; },
    });
  }

  selectRide(ride: ActiveRideLocation): void {
    this.selectedRide = ride;
    this.panToRide(ride);
    // Open the info window for this marker
    const state = this.markers.get(ride.id);
    if (state) {
      this.openInfoWindow?.close();
      state.infoWindow.open(this.map!, state.marker);
      this.openInfoWindow = state.infoWindow;
    }
  }

  goToRide(id: string): void {
    this.router.navigate(['/rides', id]);
  }

  private panToRide(ride: ActiveRideLocation): void {
    if (this.map && ride.lat && ride.lng) {
      this.map.panTo({ lat: ride.lat, lng: ride.lng });
      this.map.setZoom(15);
    }
  }

  private fitMapToMarkers(): void {
    if (!this.map || this.rides.length === 0) return;
    if (this.rides.length === 1) {
      this.panToRide(this.rides[0]);
      return;
    }
    const bounds = new google.maps.LatLngBounds();
    this.rides.forEach(r => bounds.extend({ lat: r.lat, lng: r.lng }));
    this.map.fitBounds(bounds, 80);
  }

  private addOrUpdateMarker(ride: ActiveRideLocation): void {
    if (!this.map) return;
    const existing = this.markers.get(ride.id);
    if (existing) {
      existing.marker.setPosition({ lat: ride.lat, lng: ride.lng });
      existing.marker.setIcon(this.buildIcon(ride));
      existing.infoWindow.setContent(this.buildInfoWindowContent(ride));
      return;
    }

    const marker = new google.maps.Marker({
      position: { lat: ride.lat, lng: ride.lng },
      map: this.map,
      icon: this.buildIcon(ride),
      title: `${ride.residentName} · ${ride.vendorName ?? 'Unknown'}`,
      animation: google.maps.Animation.DROP,
    });

    const infoWindow = new google.maps.InfoWindow({
      content: this.buildInfoWindowContent(ride),
      maxWidth: 280,
    });

    marker.addListener('click', () => {
      this.zone.run(() => {
        this.openInfoWindow?.close();
        infoWindow.open(this.map!, marker);
        this.openInfoWindow = infoWindow;
        this.selectedRide = ride;
      });
    });

    this.markers.set(ride.id, { marker, infoWindow, rideId: ride.id });
  }

  private moveMarker(ride: ActiveRideLocation): void {
    const state = this.markers.get(ride.id);
    if (state) {
      state.marker.setPosition({ lat: ride.lat, lng: ride.lng });
      state.infoWindow.setContent(this.buildInfoWindowContent(ride));
    } else {
      this.addOrUpdateMarker(ride);
    }
    // Smoothly follow if this is the selected ride
    if (this.selectedRide?.id === ride.id && this.map) {
      this.map.panTo({ lat: ride.lat, lng: ride.lng });
    }
  }

  private removeMarker(rideId: string): void {
    const state = this.markers.get(rideId);
    if (state) {
      state.infoWindow.close();
      state.marker.setMap(null);
      this.markers.delete(rideId);
    }
  }

  private updateMarkerIcon(ride: ActiveRideLocation): void {
    const state = this.markers.get(ride.id);
    if (state) state.marker.setIcon(this.buildIcon(ride));
  }

  private buildIcon(ride: ActiveRideLocation): google.maps.Icon | google.maps.Symbol {
    // If vendor has a photo URL use circular photo pin, otherwise colored status dot
    const statusColor = this.getStatusHex(ride.status);

    if (ride.vendorPhotoUrl) {
      // Photo pin: circular image with colored border
      return {
        url: this.buildPhotoPin(ride.vendorPhotoUrl, statusColor),
        scaledSize: new google.maps.Size(52, 52),
        anchor: new google.maps.Point(26, 26),
      };
    }

    // Fallback: colored circle with initials (SVG data URL)
    const initials = this.getInitials(ride.vendorName ?? ride.residentName);
    const svg = `
      <svg xmlns="http://www.w3.org/2000/svg" width="48" height="56">
        <circle cx="24" cy="24" r="22" fill="${statusColor}" stroke="white" stroke-width="3"/>
        <text x="24" y="30" text-anchor="middle" font-family="Arial" font-size="14"
              font-weight="bold" fill="white">${initials}</text>
        <polygon points="24,50 18,38 30,38" fill="${statusColor}"/>
      </svg>`;

    return {
      url: 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg),
      scaledSize: new google.maps.Size(48, 56),
      anchor: new google.maps.Point(24, 50),
    };
  }

  private buildPhotoPin(photoUrl: string, borderColor: string): string {
    // Returns an SVG data URL with the photo clipped to a circle + colored border
    const svg = `
      <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="56" height="64">
        <defs>
          <clipPath id="c"><circle cx="28" cy="28" r="24"/></clipPath>
        </defs>
        <circle cx="28" cy="28" r="26" fill="${borderColor}"/>
        <image href="${photoUrl}" x="4" y="4" width="48" height="48" clip-path="url(#c)" preserveAspectRatio="xMidYMid slice"/>
        <polygon points="28,60 20,44 36,44" fill="${borderColor}"/>
      </svg>`;
    return 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg);
  }

  private buildInfoWindowContent(ride: ActiveRideLocation): string {
    const photoHtml = ride.vendorPhotoUrl
      ? `<img src="${ride.vendorPhotoUrl}" style="width:44px;height:44px;border-radius:50%;object-fit:cover;border:2px solid ${this.getStatusHex(ride.status)}" />`
      : `<div style="width:44px;height:44px;border-radius:50%;background:${this.getStatusHex(ride.status)};display:flex;align-items:center;justify-content:center;color:white;font-weight:700;font-size:14px">${this.getInitials(ride.vendorName ?? ride.residentName)}</div>`;

    const phone = ride.vendorPhone
      ? `<a href="tel:${ride.vendorPhone}" style="color:#3f51b5;text-decoration:none;font-size:12px">📞 ${ride.vendorPhone}</a>`
      : '';

    const lastSeen = ride.lastLocationAt
      ? `GPS updated ${this.getLastSeenLabel(ride.lastLocationAt)}`
      : 'No GPS update';

    return `
      <div style="font-family:Arial,sans-serif;min-width:220px;padding:4px">
        <div style="display:flex;align-items:center;gap:10px;margin-bottom:10px">
          ${photoHtml}
          <div>
            <div style="font-weight:700;font-size:14px;color:#111">${ride.residentName}</div>
            <div style="font-size:12px;color:#555">${ride.vendorName ?? 'Unassigned'}</div>
            ${phone}
          </div>
        </div>
        <div style="background:${this.getStatusHex(ride.status)};color:white;font-size:11px;font-weight:700;padding:2px 8px;border-radius:20px;display:inline-block;margin-bottom:8px">
          ${ride.status.toUpperCase()}
        </div>
        <div style="font-size:11px;color:#666;margin-bottom:4px">📍 ${ride.pickupAddress}</div>
        <div style="font-size:11px;color:#666;margin-bottom:6px">🏥 ${ride.destinationAddress}</div>
        <div style="font-size:10px;color:#999;margin-bottom:8px">${lastSeen}</div>
        <div style="display:flex;gap:6px">
          <a href="/rides/${ride.id}" style="background:#3f51b5;color:white;padding:4px 10px;border-radius:4px;text-decoration:none;font-size:11px;font-weight:600">View Ride</a>
          <a href="https://www.google.com/maps?q=${ride.lat},${ride.lng}" target="_blank" style="background:#f5f5f5;color:#333;padding:4px 10px;border-radius:4px;text-decoration:none;font-size:11px">Open Maps</a>
        </div>
      </div>`;
  }

  private getInitials(name: string): string {
    return name.split(' ').slice(0, 2).map(n => n[0] ?? '').join('').toUpperCase();
  }

  getLastSeenLabel(lastLocationAt: string | null | undefined): string {
    return lastSeenLabel(lastLocationAt);
  }

  getStatusColor(status: string): string {
    return this.getStatusHex(status);
  }

  private getStatusHex(status: string): string {
    const map: Record<string, string> = {
      Dispatched: '#2196f3', Confirmed: '#4caf50', EnRoute: '#ff9800',
      Arrived: '#9c27b0', PickedUp: '#e65100', AtDestination: '#2e7d32', Dropped: '#00bcd4',
    };
    return map[status] ?? '#9e9e9e';
  }

  getChannelIcon(channel: string): string {
    const icons: Record<string, string> = {
      SmsNemt: 'accessible', SmsTaxi: 'local_taxi',
      Broker: 'hub',
    };
    return icons[channel] ?? 'help_outline';
  }
}
