import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../shared/auth/auth.service';
import { CurrentUser } from '../shared/models/auth.model';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDividerModule
  ],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
})
export class SettingsComponent implements OnInit {
  currentUser: CurrentUser | null = null;
  editProfileForm: FormGroup;
  changePasswordForm: FormGroup;
  isEditingProfile = false;
  isChangingPassword = false;
  savingProfile = false;
  savingPassword = false;
  uploadingPhoto = false;
  selectedFile: File | null = null;
  photoPreview: string | null = null;
  currentPhotoUrl: string | null = null;

  private readonly api = `${environment.apiUrl}/api/users`;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private authService: AuthService,
    private snackBar: MatSnackBar
  ) {
    this.editProfileForm = this.fb.group({
      firstName: ['', [Validators.required, Validators.minLength(2)]],
      lastName: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]]
    });

    this.changePasswordForm = this.fb.group({
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]]
    }, { validators: this.passwordMatchValidator });
  }

  ngOnInit(): void {
    this.authService.currentUser$.subscribe(user => {
      this.currentUser = user;
      if (user) {
        this.editProfileForm.patchValue({
          firstName: user.firstName,
          lastName: user.lastName,
          email: user.email
        });
      }
    });

    this.http.get<{ photoUrl: string | null }>(`${this.api}/me`).subscribe({
      next: (profile) => { this.currentPhotoUrl = profile.photoUrl ?? null; },
      error: () => {}
    });
  }

  passwordMatchValidator(form: FormGroup) {
    const newPassword = form.get('newPassword');
    const confirmPassword = form.get('confirmPassword');

    if (newPassword && confirmPassword && newPassword.value !== confirmPassword.value) {
      confirmPassword.setErrors({ passwordMismatch: true });
      return { passwordMismatch: true };
    }

    return null;
  }

  toggleEditProfile(): void {
    this.isEditingProfile = !this.isEditingProfile;
    if (!this.isEditingProfile && this.currentUser) {
      // Reset form to current values if cancelled
      this.editProfileForm.patchValue({
        firstName: this.currentUser.firstName,
        lastName: this.currentUser.lastName,
        email: this.currentUser.email
      });
    }
  }

  saveProfile(): void {
    if (this.editProfileForm.valid) {
      this.savingProfile = true;
      const { firstName, lastName } = this.editProfileForm.value;
      this.http.put(`${this.api}/me`, { firstName, lastName }).subscribe({
        next: () => {
          this.snackBar.open('Profile updated successfully!', 'Close', { duration: 3000, panelClass: ['success-snackbar'] });
          this.isEditingProfile = false;
          this.savingProfile = false;
        },
        error: () => {
          this.snackBar.open('Failed to update profile. Please try again.', 'Close', { duration: 4000, panelClass: ['error-snackbar'] });
          this.savingProfile = false;
        }
      });
    }
  }

  toggleChangePassword(): void {
    this.isChangingPassword = !this.isChangingPassword;
    if (!this.isChangingPassword) {
      this.changePasswordForm.reset();
    }
  }

  changePassword(): void {
    if (this.changePasswordForm.valid) {
      this.savingPassword = true;
      const { currentPassword, newPassword } = this.changePasswordForm.value;
      this.http.post(`${this.api}/me/change-password`, { currentPassword, newPassword }).subscribe({
        next: () => {
          this.snackBar.open('Password changed successfully!', 'Close', { duration: 3000, panelClass: ['success-snackbar'] });
          this.changePasswordForm.reset();
          this.isChangingPassword = false;
          this.savingPassword = false;
        },
        error: (err) => {
          const msg = err?.error?.errors ? 'Password does not meet requirements.' : 'Failed to change password. Check your current password.';
          this.snackBar.open(msg, 'Close', { duration: 5000, panelClass: ['error-snackbar'] });
          this.savingPassword = false;
        }
      });
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files[0]) {
      this.selectedFile = input.files[0];

      // Create preview
      const reader = new FileReader();
      reader.onload = (e) => {
        this.photoPreview = e.target?.result as string;
      };
      reader.readAsDataURL(this.selectedFile);
    }
  }

  uploadPhoto(): void {
    if (!this.selectedFile) return;
    this.uploadingPhoto = true;
    const formData = new FormData();
    formData.append('file', this.selectedFile);
    this.http.post<{ photoUrl: string }>(`${this.api}/me/photo`, formData).subscribe({
      next: (res) => {
        this.currentPhotoUrl = res.photoUrl;
        this.snackBar.open('Profile photo updated!', 'Close', { duration: 3000, panelClass: ['success-snackbar'] });
        this.selectedFile = null;
        this.photoPreview = null;
        this.uploadingPhoto = false;
      },
      error: () => {
        this.snackBar.open('Failed to upload photo. Max 5 MB, JPEG/PNG/WebP only.', 'Close', { duration: 5000, panelClass: ['error-snackbar'] });
        this.uploadingPhoto = false;
      }
    });
  }

  removePhoto(): void {
    this.http.delete(`${this.api}/me/photo`).subscribe({
      next: () => {
        this.currentPhotoUrl = null;
        this.snackBar.open('Profile photo removed.', 'Close', { duration: 3000, panelClass: ['success-snackbar'] });
        this.selectedFile = null;
        this.photoPreview = null;
      },
      error: () => {
        this.snackBar.open('Failed to remove photo.', 'Close', { duration: 4000, panelClass: ['error-snackbar'] });
      }
    });
  }

  logout(): void {
    this.authService.logout();
  }

  getRoleDisplay(role: string): string {
    switch (role) {
      case 'SuperAdmin': return 'Super Administrator';
      case 'OrgAdmin': return 'Organization Administrator';
      case 'Coordinator': return 'Facility Coordinator';
      default: return role;
    }
  }
}
