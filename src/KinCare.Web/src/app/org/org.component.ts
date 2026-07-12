import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatChipsModule } from '@angular/material/chips';
import { environment } from '../../environments/environment';

interface OrgUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  facilityName: string | null;
  isActive: boolean;
}

interface OrgMetrics {
  facilityCount: number;
  ridesThisMonth: number;
  completionRate: number;
  avgResponseMinutes: number;
  topVendor: string | null;
}

@Component({
  selector: 'app-org',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSnackBarModule,
    MatTabsModule,
    MatChipsModule,
  ],
  templateUrl: './org.component.html',
  styleUrl: './org.component.scss',
})
export class OrgComponent implements OnInit {
  users: OrgUser[] = [];
  metrics: OrgMetrics | null = null;
  loadingUsers = true;
  loadingMetrics = true;
  showInviteForm = false;
  inviting = false;
  deactivating: string | null = null;
  inviteForm: FormGroup;
  userColumns = ['name', 'email', 'role', 'facility', 'status', 'actions'];

  private readonly apiUrl = `${environment.apiUrl}/api`;

  constructor(private http: HttpClient, private fb: FormBuilder, private snackBar: MatSnackBar) {
    this.inviteForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      role: ['Coordinator', Validators.required],
      facilityId: [''],
    });
  }

  ngOnInit(): void {
    this.loadUsers();
    this.loadMetrics();
  }

  loadUsers(): void {
    this.loadingUsers = true;
    this.http.get<OrgUser[]>(`${this.apiUrl}/org/users`).subscribe({
      next: (u) => { this.users = u; this.loadingUsers = false; },
      error: () => { this.loadingUsers = false; },
    });
  }

  loadMetrics(): void {
    this.http.get<OrgMetrics>(`${this.apiUrl}/org/metrics`).subscribe({
      next: (m) => { this.metrics = m; this.loadingMetrics = false; },
      error: () => { this.loadingMetrics = false; },
    });
  }

  invite(): void {
    if (this.inviteForm.invalid) return;
    this.inviting = true;
    this.http.post(`${this.apiUrl}/org/invite`, this.inviteForm.value).subscribe({
      next: () => {
        this.inviting = false;
        this.showInviteForm = false;
        this.inviteForm.reset({ role: 'Coordinator' });
        this.snackBar.open('Invitation sent successfully', 'Close', { duration: 3000 });
        this.loadUsers();
      },
      error: (err) => {
        this.inviting = false;
        this.snackBar.open(err?.error?.error || 'Failed to send invitation', 'Close', { duration: 4000 });
      },
    });
  }

  deactivateUser(userId: string): void {
    if (!confirm('Deactivate this user? They will lose access immediately.')) return;
    this.deactivating = userId;
    this.http.delete(`${this.apiUrl}/org/users/${userId}`).subscribe({
      next: () => {
        this.deactivating = null;
        this.snackBar.open('User deactivated', 'Close', { duration: 3000 });
        this.loadUsers();
      },
      error: (err) => {
        this.deactivating = null;
        this.snackBar.open(err?.error?.error || 'Failed to deactivate user', 'Close', { duration: 4000 });
      },
    });
  }
}
