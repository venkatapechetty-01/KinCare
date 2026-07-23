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
import { GeocodeService, AddressSuggestion } from '../shared/services/geocode.service';
import { AuthService } from '../shared/auth/auth.service';
import { Resident } from '../shared/models/resident.model';
import { HttpClient } from '@angular/common/http';
import { offsetNow, formatPickupTime, formatDateForInput } from '../shared/utils/date.utils';
import { Subscription, Observable, of } from 'rxjs';
import { map, startWith, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
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
  quickAdding = false;
  facilityAddress: string | null = null;
  pickupSuggestions: AddressSuggestion[] = [];
  destinationSuggestions: AddressSuggestion[] = [];
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
    private geocodeService: GeocodeService,
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
    this.watchAddressField('pickupAddress', suggestions => this.pickupSuggestions = suggestions);
    this.watchAddressField('destinationAddress', suggestions => this.destinationSuggestions = suggestions);

    this.subscriptions.add(
      this.bookingForm.get('residentName')!.valueChanges.subscribe((value: string) => {
        if (this.selectedResident && this.getResidentDisplay(this.selectedResident) !== value) {
          this.selectedResident = null;
        }
      })
    );
  }

  private watchAddressField(controlName: string, onResults: (suggestions: AddressSuggestion[]) => void): void {
    this.subscriptions.add(
      this.bookingForm.get(controlName)!.valueChanges.pipe(
        debounceTime(350),
        distinctUntilChanged(),
        switchMap(value => (value?.trim()?.length ?? 0) >= 3 ? this.geocodeService.autocomplete(value) : of([]))
      ).subscribe(suggestions => onResults(suggestions))
    );
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
    this.bookingForm.patchValue({ pickupDate: date, pickupTime: time });
  }

  getFormattedDateTime(): string {
    const date = this.bookingForm.get('pickupDate')?.value;
    const time = this.bookingForm.get('pickupTime')?.value as string;
    if (!date || !time) return '';
    const iso = `${formatDateForInput(new Date(date))}T${time}:00`;
    return formatPickupTime(iso);
  }

  addressPrimary(displayName: string): string {
    return displayName.split(',')[0].trim();
  }

  addressSecondary(displayName: string): string {
    return displayName.split(',').slice(1).join(',').trim();
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

  get typedResidentName(): string {
    return ((this.bookingForm.get('residentName')?.value as string) || '').trim();
  }

  get canQuickAdd(): boolean {
    const name = this.typedResidentName;
    if (!name || !/\s/.test(name) || this.selectedResident) return false;
    const lower = name.toLowerCase();
    return !this.residents.some(r =>
      this.getResidentDisplay(r).toLowerCase() === lower ||
      `${r.firstName} ${r.lastName}`.toLowerCase() === lower
    );
  }

  quickAddResident(): void {
    const name = this.typedResidentName;
    const spaceIdx = name.indexOf(' ');
    const firstName = name.slice(0, spaceIdx).trim();
    const lastName = name.slice(spaceIdx + 1).trim();
    if (!firstName || !lastName || this.quickAdding) return;

    this.quickAdding = true;
    this.subscriptions.add(
      this.residentService.create({
        firstName,
        lastName,
        needsWheelchair: false,
        needsOxygen: false,
        needsStretcher: false,
        needsWalker: false,
      }).subscribe({
        next: (res) => {
          const newResident: Resident = {
            id: res.id,
            facilityId: '',
            firstName,
            lastName,
            needsWheelchair: false,
            needsOxygen: false,
            needsStretcher: false,
            needsWalker: false,
          };
          this.residents = [...this.residents, newResident];
          this.selectedResident = newResident;
          this.bookingForm.patchValue({ residentName: this.getResidentDisplay(newResident) });
          this.quickAdding = false;
          this.snackBar.open(`${firstName} ${lastName} added as a new resident.`, 'Close', { duration: 3000 });
        },
        error: () => {
          this.quickAdding = false;
          this.snackBar.open('Failed to add new resident. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar'],
          });
        },
      })
    );
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
        `No resident named "${residentName}" found. Tap "Add as a new resident" below, or add them on the Residents page first.`,
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
