import { Component, OnInit, OnDestroy, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSelectModule } from '@angular/material/select';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Router, RouterLink } from '@angular/router';
import { ResidentService } from '../shared/services/resident.service';
import { AuthService } from '../shared/auth/auth.service';
import { Resident } from '../shared/models/resident.model';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-residents',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatTableModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    MatPaginatorModule
  ],
  templateUrl: './residents.component.html',
  styleUrl: './residents.component.scss',
})
export class ResidentsComponent implements OnInit, OnDestroy {
  residents: Resident[] = [];
  loading = true;
  displayedColumns: string[] = ['name', 'specialNeeds', 'notes', 'actions'];
  private subscriptions = new Subscription();

  pageSize = 12;
  pageIndex = 0;
  readonly pageSizeOptions = [12, 24, 48];

  get pagedResidents(): Resident[] {
    const start = this.pageIndex * this.pageSize;
    return this.residents.slice(start, start + this.pageSize);
  }

  onPageChange(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
  }

  constructor(
    private residentService: ResidentService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadResidents();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadResidents(): void {
    this.loading = true;
    this.subscriptions.add(
      this.residentService.getAll().subscribe({
        next: (residents) => {
          this.residents = residents;
          this.loading = false;
        },
        error: (error) => {
          console.error('Error loading residents:', error);
          this.snackBar.open('Failed to load residents. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar']
          });
          this.loading = false;
        }
      })
    );
  }

  getSpecialNeeds(resident: Resident): string[] {
    const needs: string[] = [];
    if (resident.needsWheelchair) needs.push('Wheelchair');
    if (resident.needsOxygen) needs.push('Oxygen');
    if (resident.needsStretcher) needs.push('Stretcher');
    if (resident.needsWalker) needs.push('Walker');
    if (needs.length === 0) needs.push('Ambulatory');
    return needs;
  }

  addResident(): void {
    const dialogRef = this.dialog.open(ResidentDialogComponent, {
      width: '600px',
      data: { resident: null, isEdit: false }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.loadResidents();
      }
    });
  }

  editResident(resident: Resident): void {
    const dialogRef = this.dialog.open(ResidentDialogComponent, {
      width: '600px',
      data: { resident, isEdit: true }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.loadResidents();
      }
    });
  }

  viewPastRides(resident: Resident): void {
    this.router.navigate(['/history'], { queryParams: { residentId: resident.id, residentName: resident.firstName + ' ' + resident.lastName } });
  }

  deleteResident(resident: Resident): void {
    if (confirm(`Are you sure you want to deactivate ${resident.firstName} ${resident.lastName}?`)) {
      this.subscriptions.add(
        this.residentService.delete(resident.id).subscribe({
          next: () => {
            this.snackBar.open('Resident deactivated successfully', 'Close', { duration: 3000 });
            this.loadResidents();
          },
          error: (error) => {
            console.error('Error deleting resident:', error);
            this.snackBar.open('Failed to deactivate resident', 'Close', { duration: 5000 });
          }
        })
      );
    }
  }
}

// Dialog Component
@Component({
  selector: 'app-resident-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  template: `
    <h2 mat-dialog-title>{{ data.isEdit ? 'Edit Resident' : 'Add New Resident' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="resident-form">
        @if (isOrgAdmin) {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Facility</mat-label>
            <mat-select formControlName="facilityId" placeholder="Select facility">
              @for (facility of facilities; track facility.id) {
                <mat-option [value]="facility.id">
                  {{ facility.name }}
                </mat-option>
              }
            </mat-select>
            <mat-icon matIconPrefix>business</mat-icon>
            @if (form.get('facilityId')?.hasError('required') && form.get('facilityId')?.touched) {
              <mat-error>Facility is required</mat-error>
            }
          </mat-form-field>
        }

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>First Name</mat-label>
          <input matInput formControlName="firstName" placeholder="Enter first name" />
          <mat-icon matIconPrefix>person</mat-icon>
          @if (form.get('firstName')?.hasError('required') && form.get('firstName')?.touched) {
            <mat-error>First name is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Last Name</mat-label>
          <input matInput formControlName="lastName" placeholder="Enter last name" />
          <mat-icon matIconPrefix>person</mat-icon>
          @if (form.get('lastName')?.hasError('required') && form.get('lastName')?.touched) {
            <mat-error>Last name is required</mat-error>
          }
        </mat-form-field>

        <div class="special-needs-section">
          <h3>Special Transportation Needs</h3>
          <div class="checkboxes">
            <mat-checkbox formControlName="needsWheelchair">
              <mat-icon>accessible</mat-icon>
              Wheelchair
            </mat-checkbox>
            <mat-checkbox formControlName="needsOxygen">
              <mat-icon>air</mat-icon>
              Oxygen
            </mat-checkbox>
            <mat-checkbox formControlName="needsStretcher">
              <mat-icon>local_hospital</mat-icon>
              Stretcher
            </mat-checkbox>
            <mat-checkbox formControlName="needsWalker">
              <mat-icon>directions_walk</mat-icon>
              Walker
            </mat-checkbox>
          </div>
        </div>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Driver Notes</mat-label>
          <textarea
            matInput
            formControlName="driverNotes"
            placeholder="Any special instructions for drivers"
            rows="4"
            maxlength="1000"></textarea>
          <mat-icon matIconPrefix>note</mat-icon>
          <mat-hint align="end">{{ form.get('driverNotes')?.value?.length || 0 }}/1000</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="onCancel()" [disabled]="submitting">
        <mat-icon>cancel</mat-icon>
        Cancel
      </button>
      @if (!data.isEdit) {
        <button mat-raised-button color="accent" (click)="onSaveAndAddAnother()" [disabled]="form.invalid || submitting">
          <mat-icon>person_add</mat-icon>
          Save & Add Another
        </button>
      }
      <button mat-raised-button color="primary" (click)="onSave()" [disabled]="form.invalid || submitting">
        @if (submitting) {
          <mat-spinner diameter="20" style="display: inline-block; margin-right: 8px;"></mat-spinner>
          Saving...
        } @else {
          <mat-icon>save</mat-icon>
          {{ data.isEdit ? 'Update' : 'Create' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .resident-form {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      padding: 1rem 0;
      min-width: 500px;
    }

    .full-width {
      width: 100%;
    }

    .special-needs-section {
      h3 {
        font-size: 1.125rem;
        font-weight: 600;
        margin-bottom: 1rem;
        color: #000;
      }

      .checkboxes {
        display: grid;
        grid-template-columns: repeat(2, 1fr);
        gap: 1rem;

        mat-checkbox {
          font-size: 1rem;
          font-weight: 600;

          ::ng-deep {
            .mdc-checkbox {
              padding: 11px;
            }

            .mdc-label {
              display: flex;
              align-items: center;
              gap: 0.5rem;
              font-size: 1rem;
            }
          }

          mat-icon {
            font-size: 1.25rem;
            width: 1.25rem;
            height: 1.25rem;
            color: #3f51b5;
          }
        }
      }
    }

    mat-dialog-content {
      padding: 1.5rem !important;
      overflow-y: auto;
      max-height: 70vh;
    }

    mat-dialog-actions {
      padding: 1rem 1.5rem !important;

      button {
        font-size: 1.125rem !important;
        padding: 0.75rem 1.5rem !important;
        min-height: 48px !important;

        mat-icon {
          margin-right: 0.5rem;
        }
      }
    }

    ::ng-deep {
      h2.mat-mdc-dialog-title {
        font-size: 1.75rem !important;
        font-weight: 700 !important;
        color: #000 !important;
        padding: 1.5rem 1.5rem 1rem !important;
        margin: 0 !important;
      }

      mat-form-field {
        font-size: 1.125rem;

        .mat-mdc-text-field-wrapper {
          background: #f5f5f5;
        }

        mat-label {
          font-size: 1.125rem;
          font-weight: 600;
        }

        input,
        textarea {
          font-size: 1.25rem;
          font-weight: 600;
          color: #000;
        }

        mat-icon {
          font-size: 1.5rem;
          width: 1.5rem;
          height: 1.5rem;
          color: #3f51b5;
        }

        mat-error {
          font-size: 1rem;
          font-weight: 600;
        }

        mat-hint {
          font-size: 0.875rem;
          font-weight: 500;
        }
      }
    }
  `]
})
export class ResidentDialogComponent implements OnDestroy {
  form: FormGroup;
  submitting = false;
  facilities: any[] = [];
  currentUser: any = null;
  isOrgAdmin = false;
  private subscriptions = new Subscription();

  constructor(
    private fb: FormBuilder,
    private dialogRef: MatDialogRef<ResidentDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: { resident: Resident | null; isEdit: boolean },
    private residentService: ResidentService,
    private snackBar: MatSnackBar,
    private http: HttpClient,
    private auth: AuthService
  ) {
    // Check if user is OrgAdmin
    this.subscriptions.add(
      this.auth.currentUser$.subscribe(user => {
        this.currentUser = user;
        this.isOrgAdmin = user?.role === 'OrgAdmin';

        // Load facilities if OrgAdmin
        if (this.isOrgAdmin) {
          this.loadFacilities();
        }
      })
    );

    this.form = this.fb.group({
      facilityId: [data.resident?.facilityId || '', this.isOrgAdmin ? [Validators.required] : []],
      firstName: [data.resident?.firstName || '', [Validators.required, Validators.minLength(2)]],
      lastName: [data.resident?.lastName || '', [Validators.required, Validators.minLength(2)]],
      needsWheelchair: [data.resident?.needsWheelchair || false],
      needsOxygen: [data.resident?.needsOxygen || false],
      needsStretcher: [data.resident?.needsStretcher || false],
      needsWalker: [data.resident?.needsWalker || false],
      driverNotes: [data.resident?.driverNotes || '', [Validators.maxLength(1000)]]
    });
  }

  loadFacilities(): void {
    const apiUrl = `${environment.apiUrl}/api/org/facilities`;
    this.subscriptions.add(
      this.http.get<any[]>(apiUrl).subscribe({
        next: (facilities) => {
          this.facilities = facilities;
          // If only one facility, auto-select it
          if (facilities.length === 1 && !this.data.isEdit) {
            this.form.patchValue({ facilityId: facilities[0].id });
          }
        },
        error: (error) => {
          console.error('Error loading facilities:', error);
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  onCancel(): void {
    this.dialogRef.close(false);
  }

  onSaveAndAddAnother(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting = true;
    const formValue = this.form.value;
    this.residentService.create(formValue).subscribe({
      next: () => {
        this.snackBar.open('Resident added! Fill in the next one.', 'Close', {
          duration: 2500,
          panelClass: ['success-snackbar']
        });
        // Reset form for next entry, keep facilityId
        const facilityId = this.form.get('facilityId')?.value;
        this.form.reset();
        if (facilityId) this.form.patchValue({ facilityId });
        this.submitting = false;
        // Signal parent to refresh list
        this.dialogRef.close('refresh');
      },
      error: () => {
        this.snackBar.open('Failed to add resident. Please try again.', 'Close', { duration: 5000, panelClass: ['error-snackbar'] });
        this.submitting = false;
      }
    });
  }

  onSave(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting = true;
    const formValue = this.form.value;

    if (this.data.isEdit && this.data.resident) {
      // Update existing resident
      this.subscriptions.add(
        this.residentService.update(this.data.resident.id, formValue).subscribe({
          next: () => {
            this.snackBar.open('Resident updated successfully!', 'Close', {
              duration: 3000,
              panelClass: ['success-snackbar']
            });
            this.dialogRef.close(true);
          },
          error: (error) => {
            console.error('Error updating resident:', error);
            this.snackBar.open('Failed to update resident. Please try again.', 'Close', {
              duration: 5000,
              panelClass: ['error-snackbar']
            });
            this.submitting = false;
          }
        })
      );
    } else {
      // Create new resident
      this.subscriptions.add(
        this.residentService.create(formValue).subscribe({
          next: () => {
            this.snackBar.open('Resident added successfully!', 'Close', {
              duration: 3000,
              panelClass: ['success-snackbar']
            });
            this.dialogRef.close(true);
          },
          error: (error) => {
            console.error('Error creating resident:', error);
            this.snackBar.open('Failed to add resident. Please try again.', 'Close', {
              duration: 5000,
              panelClass: ['error-snackbar']
            });
            this.submitting = false;
          }
        })
      );
    }
  }
}
