import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDividerModule } from '@angular/material/divider';
import { environment } from '../../environments/environment';
import { FacilityDto } from './branches.component';

export interface ResidentSummary {
  id: string;
  facilityId: string;
  firstName: string;
  lastName: string;
  needsWheelchair: boolean;
  needsOxygen: boolean;
  needsStretcher: boolean;
  needsWalker: boolean;
  driverNotes?: string;
}

@Component({
  selector: 'app-branch-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    MatSnackBarModule,
    MatDividerModule,
  ],
  templateUrl: './branch-detail.component.html',
  styleUrl: './branch-detail.component.scss',
})
export class BranchDetailComponent implements OnInit {
  facilityId = '';
  facility: FacilityDto | null = null;
  residents: ResidentSummary[] = [];
  loading = true;
  residentsLoading = true;

  showResidentForm = false;
  editingResident: ResidentSummary | null = null;
  residentForm: FormGroup;
  saving = false;

  private readonly apiUrl = `${environment.apiUrl}/api`;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private fb: FormBuilder,
    private snackBar: MatSnackBar
  ) {
    this.residentForm = this.fb.group({
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      needsWheelchair: [false],
      needsOxygen: [false],
      needsStretcher: [false],
      needsWalker: [false],
      driverNotes: ['', Validators.maxLength(1000)],
    });
  }

  ngOnInit(): void {
    this.facilityId = this.route.snapshot.paramMap.get('id') ?? '';
    this.loadFacility();
    this.loadResidents();
  }

  loadFacility(): void {
    this.http.get<FacilityDto>(`${this.apiUrl}/org/facilities/${this.facilityId}`).subscribe({
      next: (f) => { this.facility = f; this.loading = false; },
      error: () => { this.loading = false; },
    });
  }

  loadResidents(): void {
    this.residentsLoading = true;
    this.http.get<ResidentSummary[]>(`${this.apiUrl}/org/facilities/${this.facilityId}/residents`).subscribe({
      next: (r) => { this.residents = r; this.residentsLoading = false; },
      error: () => { this.residentsLoading = false; },
    });
  }

  openAddForm(): void {
    this.editingResident = null;
    this.residentForm.reset({ needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false });
    this.showResidentForm = true;
  }

  openEditForm(r: ResidentSummary): void {
    this.editingResident = r;
    this.residentForm.setValue({
      firstName: r.firstName,
      lastName: r.lastName,
      needsWheelchair: r.needsWheelchair,
      needsOxygen: r.needsOxygen,
      needsStretcher: r.needsStretcher,
      needsWalker: r.needsWalker,
      driverNotes: r.driverNotes ?? '',
    });
    this.showResidentForm = true;
  }

  cancelForm(): void {
    this.showResidentForm = false;
    this.editingResident = null;
    this.residentForm.reset();
  }

  saveResident(): void {
    if (this.residentForm.invalid) return;
    this.saving = true;

    const payload = { ...this.residentForm.value, facilityId: this.facilityId };

    if (this.editingResident) {
      this.http.put(`${this.apiUrl}/residents/${this.editingResident.id}`, this.residentForm.value).subscribe({
        next: () => {
          this.saving = false;
          this.showResidentForm = false;
          this.snackBar.open('Resident updated', 'Close', { duration: 3000 });
          this.loadResidents();
        },
        error: (err) => {
          this.saving = false;
          this.snackBar.open(err?.error?.error || 'Failed to update resident', 'Close', { duration: 4000 });
        },
      });
    } else {
      this.http.post(`${this.apiUrl}/residents`, payload).subscribe({
        next: () => {
          this.saving = false;
          this.showResidentForm = false;
          this.snackBar.open('Resident added', 'Close', { duration: 3000 });
          this.loadResidents();
        },
        error: (err) => {
          this.saving = false;
          this.snackBar.open(err?.error?.error || 'Failed to add resident', 'Close', { duration: 4000 });
        },
      });
    }
  }

  deleteResident(r: ResidentSummary): void {
    if (!confirm(`Remove ${r.firstName} ${r.lastName} from this branch?`)) return;
    this.http.delete(`${this.apiUrl}/residents/${r.id}`).subscribe({
      next: () => {
        this.snackBar.open('Resident removed', 'Close', { duration: 3000 });
        this.loadResidents();
      },
      error: () => this.snackBar.open('Failed to remove resident', 'Close', { duration: 4000 }),
    });
  }

  getNeeds(r: ResidentSummary): string[] {
    const needs: string[] = [];
    if (r.needsWheelchair) needs.push('Wheelchair');
    if (r.needsOxygen) needs.push('Oxygen');
    if (r.needsStretcher) needs.push('Stretcher');
    if (r.needsWalker) needs.push('Walker');
    if (needs.length === 0) needs.push('Ambulatory');
    return needs;
  }

  bookRide(): void {
    this.router.navigate(['/booking']);
  }
}
