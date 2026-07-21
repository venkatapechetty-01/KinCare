import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../shared/auth/auth.service';

@Component({
  selector: 'app-sms-consent',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './sms-consent.component.html',
  styleUrl: './sms-consent.component.scss',
})
export class SmsConsentComponent {
  lastUpdated = 'July 21, 2026';

  constructor(private auth: AuthService) {}

  get backLink(): string {
    return this.auth.isAuthenticated() ? '/dashboard' : '/login';
  }
}
