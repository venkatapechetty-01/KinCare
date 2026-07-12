import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../shared/auth/auth.service';
import { InviteDetails } from '../shared/models/invitation.model';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './accept-invite.component.html',
  styleUrl: './accept-invite.component.scss',
})
export class AcceptInviteComponent implements OnInit {
  form: FormGroup;
  invite: InviteDetails | null = null;
  loading = true;
  error = '';
  token = '';

  private readonly apiUrl = `${environment.apiUrl}/api`;

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private auth: AuthService
  ) {
    this.form = this.fb.group({
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      password: ['', [Validators.required, Validators.minLength(8)]],
    });
  }

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') || '';
    if (!this.token) {
      this.error = 'Invalid invitation link.';
      this.loading = false;
      return;
    }

    this.http.get<InviteDetails>(`${this.apiUrl}/onboarding/invite/${this.token}`).subscribe({
      next: (invite) => {
        this.invite = invite;
        this.loading = false;
      },
      error: (err) => {
        this.error = err?.error?.error || 'Invitation not found or expired.';
        this.loading = false;
      },
    });
  }

  submit(): void {
    if (this.form.invalid) return;
    this.error = '';

    this.auth.acceptInvite({ token: this.token, ...this.form.value }).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (err) => (this.error = err?.error?.errors?.[0] || 'Failed to accept invitation.'),
    });
  }
}
