import { Component, OnInit, OnDestroy, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { VendorService } from '../shared/services/vendor.service';
import { Vendor, CreateVendorRequest } from '../shared/models/vendor.model';
import { Subscription, map } from 'rxjs';

@Component({
  selector: 'app-vendors',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatDialogModule
  ],
  templateUrl: './vendors.component.html',
  styleUrl: './vendors.component.scss',
})
export class VendorsComponent implements OnInit, OnDestroy {
  vendors: Vendor[] = [];
  loading = true;
  uploadingPhotoVendorId: string | null = null;
  private subscriptions = new Subscription();

  constructor(
    private vendorService: VendorService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadVendors();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadVendors(): void {
    this.loading = true;
    this.subscriptions.add(
      this.vendorService.getAll().subscribe({
        next: (vendors) => {
          this.vendors = vendors;
          this.loading = false;
        },
        error: (error) => {
          console.error('Error loading vendors:', error);
          this.snackBar.open('Failed to load vendors. Please try again.', 'Close', {
            duration: 5000,
            panelClass: ['error-snackbar']
          });
          this.loading = false;
        }
      })
    );
  }

  addVendor(): void {
    const dialogRef = this.dialog.open(VendorDialogComponent, { width: '560px' });
    dialogRef.afterClosed().subscribe(result => {
      if (result) this.loadVendors();
    });
  }

  editVendor(vendor: Vendor): void {
    const dialogRef = this.dialog.open(VendorDialogComponent, { width: '560px', data: { vendor } });
    dialogRef.afterClosed().subscribe(result => {
      if (result) this.loadVendors();
    });
  }

  deleteVendor(vendor: Vendor): void {
    if (confirm(`Are you sure you want to deactivate ${vendor.name}?`)) {
      this.subscriptions.add(
        this.vendorService.delete(vendor.id).subscribe({
          next: () => {
            this.snackBar.open('Vendor deactivated successfully', 'Close', { duration: 3000 });
            this.loadVendors();
          },
          error: (error) => {
            console.error('Error deleting vendor:', error);
            this.snackBar.open('Failed to deactivate vendor', 'Close', { duration: 5000 });
          }
        })
      );
    }
  }

  getVendorTypeIcon(type: string): string {
    return type === 'Wheelchair' ? 'accessible' : 'local_taxi';
  }

  onPhotoSelected(vendor: Vendor, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file later
    if (!file) return;

    this.uploadingPhotoVendorId = vendor.id;
    // Indefinite duration — replaced by the success/error snackbar below the moment the
    // upload settles, rather than timing out mid-upload.
    const uploadingRef = this.snackBar.open('Uploading the photo…', undefined, { panelClass: ['info-snackbar'] });

    this.subscriptions.add(
      this.vendorService.uploadPhoto(vendor.id, file).subscribe({
        next: (res) => {
          vendor.photoUrl = res.photoUrl;
          this.uploadingPhotoVendorId = null;
          uploadingRef.dismiss();
          this.snackBar.open('Photo updated!', 'Close', { duration: 2500, panelClass: ['success-snackbar'] });
        },
        error: (error) => {
          this.uploadingPhotoVendorId = null;
          uploadingRef.dismiss();
          const message = error?.error?.error || 'Failed to upload photo. Max 5 MB, JPEG/PNG/WebP only.';
          this.snackBar.open(message, 'Close', { duration: 5000, panelClass: ['error-snackbar'] });
        }
      })
    );
  }

  removePhoto(vendor: Vendor, event: Event): void {
    event.stopPropagation();
    this.subscriptions.add(
      this.vendorService.removePhoto(vendor.id).subscribe({
        next: () => { vendor.photoUrl = undefined; },
        error: () => this.snackBar.open('Failed to remove photo.', 'Close', { duration: 4000, panelClass: ['error-snackbar'] })
      })
    );
  }
}

// Dialog Component
@Component({
  selector: 'app-vendor-dialog',
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
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit ? 'Edit' : 'Add' }} Driver / Vendor</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="vendor-form">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Driver / Vendor Name</mat-label>
          <input matInput formControlName="name" placeholder="Enter driver or vendor name" />
          <mat-icon matIconPrefix>local_shipping</mat-icon>
          @if (form.get('name')?.hasError('required') && form.get('name')?.touched) {
            <mat-error>Name is required</mat-error>
          }
          @if (form.get('name')?.hasError('minlength') && form.get('name')?.touched) {
            <mat-error>Name must be at least 2 characters</mat-error>
          }
          @if (form.get('name')?.hasError('maxlength') && form.get('name')?.touched) {
            <mat-error>Name cannot exceed 200 characters</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Phone Number</mat-label>
          <input matInput formControlName="phoneNumber" placeholder="+1 555 000 0000" />
          <mat-icon matIconPrefix>phone</mat-icon>
          @if (form.get('phoneNumber')?.hasError('required') && form.get('phoneNumber')?.touched) {
            <mat-error>Phone number is required</mat-error>
          }
          @if (form.get('phoneNumber')?.hasError('pattern') && form.get('phoneNumber')?.touched) {
            <mat-error>Enter a valid phone number</mat-error>
          }
          @if (form.get('phoneNumber')?.hasError('duplicate')) {
            <mat-error>A driver with this phone number already exists.</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Company (optional)</mat-label>
          <input matInput formControlName="company" placeholder="e.g. Metro Taxi Co" />
          <mat-icon matIconPrefix>business</mat-icon>
          @if (form.get('company')?.hasError('maxlength')) {
            <mat-error>Company cannot exceed 200 characters</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Service Area (optional)</mat-label>
          <input matInput formControlName="serviceArea" placeholder="e.g. Detroit Metro" />
          <mat-icon matIconPrefix>place</mat-icon>
          @if (form.get('serviceArea')?.hasError('maxlength')) {
            <mat-error>Service area cannot exceed 200 characters</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Vendor Type</mat-label>
          <mat-select formControlName="vendorType" placeholder="Select vendor type">
            <mat-option value="Wheelchair">Wheelchair / NEMT</mat-option>
            <mat-option value="Ambulatory">Ambulatory / Taxi</mat-option>
          </mat-select>
          <mat-icon matIconPrefix>accessible</mat-icon>
          @if (form.get('vendorType')?.hasError('required') && form.get('vendorType')?.touched) {
            <mat-error>Vendor type is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Dispatch Method</mat-label>
          <mat-select formControlName="dispatchMethod" placeholder="Select dispatch method">
            <mat-option value="SmsNemt">SMS NEMT</mat-option>
            <mat-option value="SmsTaxi">SMS Taxi</mat-option>
          </mat-select>
          <mat-icon matIconPrefix>sms</mat-icon>
          @if (form.get('dispatchMethod')?.hasError('required') && form.get('dispatchMethod')?.touched) {
            <mat-error>Dispatch method is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Capability Tier</mat-label>
          <mat-select formControlName="capabilityTier" placeholder="Select capability tier">
            <mat-option value="Basic">Basic (SMS only)</mat-option>
            <mat-option value="Smart">Smart (GPS tracking)</mat-option>
          </mat-select>
          <mat-icon matIconPrefix>gps_fixed</mat-icon>
          @if (form.get('capabilityTier')?.hasError('required') && form.get('capabilityTier')?.touched) {
            <mat-error>Capability tier is required</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="onCancel()">Cancel</button>
      <button mat-raised-button color="primary" (click)="onSave()" [disabled]="form.invalid || submitting">
        @if (submitting) {
          <mat-spinner diameter="20" style="display: inline-block; margin-right: 8px;"></mat-spinner>
          Saving...
        } @else {
          <mat-icon>{{ isEdit ? 'check' : 'add' }}</mat-icon>
          {{ isEdit ? 'Save Changes' : 'Add Driver' }}
        }
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .vendor-form {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      padding: 1rem 0;
      min-width: 480px;
    }

    .full-width {
      width: 100%;
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

        input {
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
      }
    }
  `]
})
export class VendorDialogComponent implements OnDestroy {
  form: FormGroup;
  submitting = false;
  isEdit: boolean;
  private subscriptions = new Subscription();

  constructor(
    private fb: FormBuilder,
    private dialogRef: MatDialogRef<VendorDialogComponent>,
    private vendorService: VendorService,
    private snackBar: MatSnackBar,
    @Inject(MAT_DIALOG_DATA) private data: { vendor?: Vendor }
  ) {
    this.isEdit = !!this.data?.vendor;
    const v = this.data?.vendor;
    this.form = this.fb.group({
      name: [v?.name ?? '', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
      phoneNumber: [v?.phoneNumber ?? '', [
        Validators.required,
        Validators.pattern(/^\+?[\d\s\-\(\)]{7,20}$/)
      ]],
      vendorType: [v?.vendorType ?? '', [Validators.required]],
      dispatchMethod: [v?.dispatchMethod ?? '', [Validators.required]],
      capabilityTier: [v?.capabilityTier ?? '', [Validators.required]],
      company: [v?.company ?? '', [Validators.maxLength(200)]],
      serviceArea: [v?.serviceArea ?? '', [Validators.maxLength(200)]]
    });
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  onCancel(): void {
    this.dialogRef.close(false);
  }

  onSave(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting = true;
    const request: CreateVendorRequest = this.form.value;

    const save$ = this.isEdit
      ? this.vendorService.update(this.data.vendor!.id, request)
      : this.vendorService.create(request).pipe(map(() => undefined));

    this.subscriptions.add(
      save$.subscribe({
        next: () => {
          this.snackBar.open(this.isEdit ? 'Driver updated successfully!' : 'Driver added successfully!', 'Close', {
            duration: 3000,
            panelClass: ['success-snackbar']
          });
          this.dialogRef.close(true);
        },
        error: (error) => {
          this.submitting = false;
          if (error.status === 409) {
            this.form.get('phoneNumber')?.setErrors({ duplicate: true });
            this.snackBar.open('A driver with this phone number already exists.', 'Close', {
              duration: 5000,
              panelClass: ['error-snackbar']
            });
          } else {
            console.error('Error saving vendor:', error);
            this.snackBar.open(
              this.isEdit ? 'Failed to update driver. Please try again.' : 'Failed to add driver. Please try again.',
              'Close', { duration: 5000, panelClass: ['error-snackbar'] });
          }
        }
      })
    );
  }
}
