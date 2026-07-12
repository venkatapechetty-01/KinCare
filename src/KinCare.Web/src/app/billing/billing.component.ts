import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { environment } from '../../environments/environment';

export interface BillingPlan {
  planTier: string;
  isActive: boolean;
  trialEndsAt: string | null;
  currentPeriodEnd: string | null;
  ridesThisMonth: number;
  completionRate: number;
}

@Component({
  selector: 'app-billing',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    MatDividerModule,
    MatSnackBarModule,
  ],
  templateUrl: './billing.component.html',
  styleUrl: './billing.component.scss',
})
export class BillingComponent implements OnInit {
  plan: BillingPlan | null = null;
  loading = true;
  portalLoading = false;
  error = '';

  private readonly apiUrl = `${environment.apiUrl}/api`;

  readonly planFeatures: Record<string, string[]> = {
    Starter: [
      'SMS dispatch (NEMT & Taxi)',
      'Escalation alerts',
      'Ride history (30 days)',
      'Up to 1 facility',
    ],
    Professional: [
      'Everything in Starter',
      'Broker dispatch (Roundtrip Health)',
      'Smart vendor GPS tracking',
      'CSV export',
      'Ride history (unlimited)',
      'Multi-facility dashboard',
    ],
    Enterprise: [
      'Everything in Professional',
      'API access',
      'SSO / SAML',
      'Dedicated support',
      'Custom SLA',
    ],
  };

  constructor(private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.loadPlan();
  }

  loadPlan(): void {
    this.loading = true;
    this.http.get<BillingPlan>(`${this.apiUrl}/billing/plan`).subscribe({
      next: (p) => { this.plan = p; this.loading = false; },
      error: () => { this.error = 'Could not load billing information.'; this.loading = false; },
    });
  }

  openPortal(): void {
    this.portalLoading = true;
    this.http.post<{ url: string }>(`${this.apiUrl}/billing/portal`, {}).subscribe({
      next: (res) => {
        this.portalLoading = false;
        window.open(res.url, '_blank', 'noopener');
      },
      error: () => {
        this.portalLoading = false;
        this.snackBar.open('Could not open billing portal. Please try again.', 'Close', { duration: 4000 });
      },
    });
  }

  subscribe(planTier: string): void {
    this.http.post<{ clientSecret?: string; url?: string }>(`${this.apiUrl}/billing/subscribe`, { planTier }).subscribe({
      next: (res) => {
        if (res.url) window.location.href = res.url;
        else this.loadPlan();
      },
      error: (err) => {
        this.snackBar.open(err?.error?.error || 'Subscription failed. Please try again.', 'Close', { duration: 4000 });
      },
    });
  }

  tierLabel(tier: string): string {
    return tier === 'Starter' ? 'Free Trial / Starter' : tier;
  }
}
