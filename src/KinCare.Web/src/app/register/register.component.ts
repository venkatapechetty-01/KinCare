import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatDividerModule } from '@angular/material/divider';
import { AuthService } from '../shared/auth/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatDividerModule,
  ],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class RegisterComponent {
  form: FormGroup;
  error = '';
  hidePassword = true;
  loading = false;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private router: Router
  ) {
    this.form = this.fb.group({
      role: ['OrgAdmin', Validators.required],
      organizationName: ['', [Validators.required, Validators.maxLength(200)]],
      facilityName: ['', [Validators.required, Validators.maxLength(200)]],
      facilityAddress: ['', [Validators.maxLength(500)]],
      firstName: ['', [Validators.required, Validators.minLength(2)]],
      lastName: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [
        Validators.required,
        Validators.minLength(12),
        Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]).*$/)
      ]],
    });

    // When role changes, adjust facilityAddress requirement
    this.form.get('role')!.valueChanges.subscribe(role => {
      const addrCtrl = this.form.get('facilityAddress')!;
      if (role === 'OrgAdmin') {
        addrCtrl.setValidators([Validators.required, Validators.maxLength(500)]);
      } else {
        addrCtrl.clearValidators();
      }
      addrCtrl.updateValueAndValidity();
    });

    // Trigger initial state
    this.form.get('facilityAddress')!.setValidators([Validators.required, Validators.maxLength(500)]);
    this.form.get('facilityAddress')!.updateValueAndValidity();
  }

  private friendlyIdentityError(msg: string): string {
    if (msg.includes('PasswordRequiresUpper') || msg.toLowerCase().includes('uppercase'))
      return 'Password needs an uppercase letter.';
    if (msg.includes('PasswordRequiresNonAlphanumeric') || msg.toLowerCase().includes('non-alphanumeric'))
      return 'Password needs a special character (!@#$%).';
    if (msg.includes('PasswordRequiresDigit') || msg.toLowerCase().includes('digit'))
      return 'Password needs a number.';
    if (msg.includes('PasswordTooShort') || msg.toLowerCase().includes('least'))
      return 'Password must be at least 12 characters.';
    if (msg.includes('DuplicateEmail') || msg.includes('DuplicateUserName') || msg.toLowerCase().includes('already taken') || msg.toLowerCase().includes('already registered'))
      return 'That email is already registered. Try logging in instead.';
    return msg;
  }

  get isOrgAdmin(): boolean {
    return this.form.get('role')?.value === 'OrgAdmin';
  }

  get isFacilityAdmin(): boolean {
    return this.form.get('role')?.value === 'FacilityAdmin';
  }

  submit(): void {
    if (this.form.invalid) return;
    this.error = '';
    this.loading = true;

    const val = this.form.value;
    const payload: any = {
      role: val.role,
      organizationName: val.organizationName,
      facilityName: val.facilityName,
      firstName: val.firstName,
      lastName: val.lastName,
      email: val.email,
      password: val.password,
    };

    if (this.isOrgAdmin) {
      payload.facilityAddress = val.facilityAddress;
    }

    this.auth.register(payload).subscribe({
      next: (response) => {
        this.loading = false;
        console.log('[Register] success, user role:', this.auth.currentUser?.role, 'response:', response);
        if (!this.auth.currentUser) {
          this.error = 'Login after registration failed. Please log in manually.';
          this.router.navigate(['/login']);
          return;
        }
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading = false;
        console.error('[Register] error:', err);
        const detail = err?.error?.errors;
        if (detail) {
          const msgs = (Object.values(detail).flat() as string[])
            .map(m => this.friendlyIdentityError(m));
          this.error = msgs.join(' ');
        } else if (err?.error?.error) {
          this.error = err.error.error;
        } else if (err?.error?.title) {
          this.error = err.error.title;
        } else if (typeof err?.error === 'string') {
          this.error = err.error;
        } else {
          this.error = `Registration failed (${err?.status ?? 'unknown error'}). Please try again.`;
        }
      },
    });
  }
}
