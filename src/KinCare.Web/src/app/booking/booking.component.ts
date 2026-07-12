import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RideService } from '../shared/services/ride.service';
import { ResidentService } from '../shared/services/resident.service';
import { AuthService } from '../shared/auth/auth.service';
import { Resident } from '../shared/models/resident.model';
import { HttpClient } from '@angular/common/http';
import { offsetNow, formatPickupTime } from '../shared/utils/date.utils';
import { Subscription, Observable } from 'rxjs';
import { map, startWith } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface TransportMode {
  value: string;
  label: string;
  description: string;
  icon: string;
  color: string;
  requiresProfessional?: boolean;
  noSpecialNeeds?: boolean;
}

interface QuickTime {
  label: string;
  offsetMinutes: number;
}

@Component({
  selector: 'app-booking',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatAutocompleteModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './booking.component.html',
  styleUrl: './booking.component.scss',
})
export class BookingComponent implements OnInit, OnDestroy {
  bookingForm: FormGroup;
  residents: Resident[] = [];
  filteredResidents: Observable<Resident[]> | undefined;
  selectedResident: Resident | null = null;
  loading = false;
  submitting = false;
  facilityAddress: string | null = null;
  private subscriptions = new Subscription();

  readonly transportModes: TransportMode[] = [
    {
      value: 'auto',
      label: 'Auto-Select',
      description: 'System picks the best transport based on resident needs and your plan',
      icon: 'auto_awesome',
      color: '#3f51b5',
    },
    {
      value: 'SmsNemt',
      label: 'NEMT',
      description: 'Wheelchair, oxygen, stretcher vehicles via SMS dispatch',
      icon: 'accessible',
      color: '#c62828',
    },
    {
      value: 'SmsTaxi',
      label: 'Local Taxi',
      description: 'Local taxi for ambulatory rides via SMS',
      icon: 'local_taxi',
      color: '#f57c00',
    },
{
      value: 'Broker',
      label: 'Broker',
      description: 'Roundtrip Health NEMT broker for hard-to-cover routes',
      icon: 'hub',
      color: '#6a1b9a',
      requiresProfessional: true,
    },
  ];

  readonly quickTimes: QuickTime[] = [
    { label: 'Now +30m', offsetMinutes: 30 },
    { label: 'Now +1h',  offsetMinutes: 60 },
    { label: 'Now +2h',  offsetMinutes: 120 },
  ];

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private rideService: RideService,
    private residentService: ResidentService,
    private auth: AuthService,
    private http: HttpClient,
    private snackBar: MatSnackBar
  ) {
    this.bookingForm = this.fb.group({
      residentName: ['', [Validators.required]],
      passengerPhone: ['', [Validators.required, Validators.pattern(/^\+?[\d\s\-\(\)]{7,15}$/)]],
      transportMode: ['auto', [Validators.required]],
      pickupDate: [new Date(), [Validators.required]],
      pickupTime: ['', [Validators.required]],
      pickupAddress: ['', [Validators.required, Validators.maxLength(500)]],
      destinationAddress: ['', [Validators.required, Validators.maxLength(500)]],
    });
  }

  ngOnInit(): void {
    this.loadResidents();
    this.loadFacilityAddress();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadFacilityAddress(): void {
    this.http.get<{ address: string }>(`${environment.apiUrl}/api/org/my-facility`).subscribe({
      next: (res) => { this.facilityAddress = res.address ?? null; },
      error: () => {},
    });
  }

  useFacilityAddress(): void {
    if (this.facilityAddress) {
      this.bookingForm.patchValue({ pickupAddress: this.facilityAddress });
    }
  }

  loadResidents(): void {
    this.loading = true;
    this.subscriptions.add(
      this.residentService.getAll().subscribe({
        next: (residents) => {
          this.residents = residents;
          this.loading = false;
          this.filteredResidents = this.bookingForm.get('residentName')?.valueChanges.pipe(
            startWith(''),
            map(value => this._filterResidents(value || ''))
          );
        },
        error: () => {
          this.snackBar.open('Failed to load residents. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar'],
          });
          this.loading = false;
        },
      })
    );
  }

  onResidentSelected(event: MatAutocompleteSelectedEvent): void {
    const display = event.option.value as string;
    this.selectedResident = this.residents.find(r =>
      this.getResidentDisplay(r) === display ||
      `${r.firstName} ${r.lastName}` === display
    ) ?? null;
  }

  selectTransportMode(value: string): void {
    this.bookingForm.patchValue({ transportMode: value });
  }

  setQuickTime(qt: QuickTime): void {
    const { date, time } = offsetNow(qt.offsetMinutes);
    this.bookingForm.patchValue({ pickupDate: new Date(date), pickupTime: time });
  }

  getFormattedDateTime(): string {
    const date = this.bookingForm.get('pickupDate')?.value;
    const time = this.bookingForm.get('pickupTime')?.value as string;
    if (!date || !time) return '';
    const iso = `${new Date(date).toISOString().split('T')[0]}T${time}:00`;
    return formatPickupTime(iso);
  }

  private _filterResidents(value: string): Resident[] {
    const filterValue = value.toLowerCase();
    return this.residents.filter(r =>
      this.getResidentDisplay(r).toLowerCase().includes(filterValue)
    );
  }

  getSelectedTransportMode(): TransportMode | undefined {
    const val = this.bookingForm.get('transportMode')?.value;
    return this.transportModes.find(m => m.value === val);
  }

  getResidentDisplay(resident: Resident): string {
    const needs: string[] = [];
    if (resident.needsWheelchair) needs.push('Wheelchair');
    if (resident.needsOxygen) needs.push('Oxygen');
    if (resident.needsStretcher) needs.push('Stretcher');
    if (resident.needsWalker) needs.push('Walker');
    const name = `${resident.firstName} ${resident.lastName}`;
    return needs.length > 0 ? `${name} (${needs.join(', ')})` : name;
  }

  onSubmit(): void {
    if (this.bookingForm.invalid) {
      this.bookingForm.markAllAsTouched();
      return;
    }

    const formValue = this.bookingForm.value;
    const pickupDate = new Date(formValue.pickupDate);
    const [hours, minutes] = (formValue.pickupTime as string).split(':').map(Number);
    pickupDate.setHours(hours, minutes, 0, 0);

    const residentName = (formValue.residentName || '').trim();
    const resident = this.selectedResident ?? this.residents.find(r =>
      this.getResidentDisplay(r).toLowerCase() === residentName.toLowerCase() ||
      `${r.firstName} ${r.lastName}`.toLowerCase() === residentName.toLowerCase()
    );

    if (!resident && residentName) {
      this.snackBar.open(
        `No resident named "${residentName}" found. Add them on the Residents page first.`,
        'Close',
        { duration: 6000, panelClass: ['error-snackbar'] }
      );
      return;
    }

    const mode = formValue.transportMode as string;
    const selectedMode = this.transportModes.find(m => m.value === mode);
    if (selectedMode?.noSpecialNeeds && resident) {
      const hasSpecialNeeds = resident.needsWheelchair || resident.needsOxygen || resident.needsStretcher;
      if (hasSpecialNeeds) {
        this.snackBar.open(
          `${selectedMode.label} cannot accommodate this resident's special needs. Please select NEMT.`,
          'Close',
          { duration: 7000, panelClass: ['error-snackbar'] }
        );
        return;
      }
    }

    const request: Record<string, unknown> = {
      pickupTime: pickupDate.toISOString(),
      pickupAddress: formValue.pickupAddress,
      destinationAddress: formValue.destinationAddress,
    };

    if (resident?.id) request['residentId'] = resident.id;
    if (resident?.facilityId) request['facilityId'] = resident.facilityId;
    if (mode !== 'auto') request['preferredChannel'] = mode;

    this.submitting = true;
    this.subscriptions.add(
      this.rideService.bookRide(request as any).subscribe({
        next: (result) => {
          const channelLabel = this.transportModes.find(m => m.value === result.dispatchChannel)?.label
            || result.dispatchChannel;
          this.snackBar.open(`Ride booked via ${channelLabel}! 🚐`, 'View Dashboard', {
            duration: 5000,
            panelClass: ['success-snackbar'],
          });
          this.router.navigate(['/dashboard']);
        },
        error: (error) => {
          console.error('[Booking] submit error:', error);
          const errors = error.error?.errors;
          const msg = errors
            ? Object.values(errors).flat().join(', ')
            : error.error?.error || error.error?.title || 'Failed to book ride. Please try again.';
          this.snackBar.open(msg, 'Close', {
            duration: 6000,
            panelClass: ['error-snackbar'],
          });
          this.submitting = false;
        },
      })
    );
  }

  cancel(): void {
    this.router.navigate(['/dashboard']);
  }
}
