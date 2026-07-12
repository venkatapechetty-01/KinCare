import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { environment } from '../../environments/environment';

export interface FacilityDto {
  id: string;
  name: string;
  address: string;
  timezone: string;
  uberHealthEnabled: boolean;
  activeRides: number;
}

@Component({
  selector: 'app-branches',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSnackBarModule,
    ReactiveFormsModule,
  ],
  templateUrl: './branches.component.html',
  styleUrl: './branches.component.scss',
})
export class BranchesComponent implements OnInit {
  facilities: FacilityDto[] = [];
  loading = true;
  showAddForm = false;
  addForm: FormGroup;
  saving = false;

  private readonly apiUrl = `${environment.apiUrl}/api`;

  constructor(
    private http: HttpClient,
    private router: Router,
    private fb: FormBuilder,
    private snackBar: MatSnackBar
  ) {
    this.addForm = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      address: ['', [Validators.required, Validators.maxLength(500)]],
      timezone: ['America/New_York'],
      uberHealthEnabled: [false],
    });
  }

  ngOnInit(): void {
    this.loadFacilities();
  }

  loadFacilities(): void {
    this.loading = true;
    this.http.get<FacilityDto[]>(`${this.apiUrl}/org/facilities`).subscribe({
      next: (f) => { this.facilities = f; this.loading = false; },
      error: () => { this.loading = false; },
    });
  }

  viewBranch(facilityId: string): void {
    this.router.navigate(['/branches', facilityId]);
  }

  addBranch(): void {
    if (this.addForm.invalid) return;
    this.saving = true;
    this.http.post<{ id: string }>(`${this.apiUrl}/org/facilities`, this.addForm.value).subscribe({
      next: () => {
        this.saving = false;
        this.showAddForm = false;
        this.addForm.reset({ timezone: 'America/New_York', uberHealthEnabled: false });
        this.snackBar.open('Branch added successfully', 'Close', { duration: 3000 });
        this.loadFacilities();
      },
      error: (err) => {
        this.saving = false;
        this.snackBar.open(err?.error?.error || 'Failed to add branch', 'Close', { duration: 4000 });
      },
    });
  }
}
