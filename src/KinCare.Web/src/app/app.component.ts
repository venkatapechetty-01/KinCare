import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet, RouterLink, NavigationEnd } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { MatBadgeModule } from '@angular/material/badge';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { AuthService } from './shared/auth/auth.service';
import { RideService } from './shared/services/ride.service';
import { filter } from 'rxjs/operators';
import { Subscription, interval } from 'rxjs';

const ANON_ROUTES = ['/login', '/register', '/invite', '/terms', '/privacy', '/sms-consent'];

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatSidenavModule,
    MatListModule,
    MatDividerModule,
    MatBadgeModule,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'KinCare.Web';
  showShell = false;
  sidenavOpen = true;
  isMobile = false;
  todayRideCount: number | null = null;
  private subs = new Subscription();

  constructor(
    public auth: AuthService,
    private router: Router,
    private rideService: RideService,
    private breakpointObserver: BreakpointObserver
  ) {}

  ngOnInit(): void {
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe((e) => {
        const url = (e as NavigationEnd).urlAfterRedirects;
        const wasAnon = !this.showShell;
        this.showShell = !ANON_ROUTES.some((r) => url.startsWith(r));
        if (this.showShell && wasAnon) this.startCountPolling();
      });

    this.subs.add(
      this.breakpointObserver.observe([Breakpoints.HandsetPortrait, Breakpoints.HandsetLandscape, Breakpoints.TabletPortrait])
        .subscribe(result => {
          this.isMobile = result.matches;
          this.sidenavOpen = !this.isMobile;
        })
    );
  }

  closeSidenavOnMobile(): void {
    if (this.isMobile) this.sidenavOpen = false;
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  private startCountPolling(): void {
    this.refreshCount();
    this.subs.add(interval(60_000).subscribe(() => this.refreshCount()));
  }

  private refreshCount(): void {
    if (!this.auth.isAuthenticated()) return;
    this.rideService.getTodayCount().subscribe({
      next: (res) => { this.todayRideCount = res.count; },
      error: () => {},
    });
  }

  logout(): void {
    this.auth.logout();
  }

  get userDisplayName(): string {
    const u = this.auth.currentUser;
    if (!u) return '';
    return u.firstName ? `${u.firstName} ${u.lastName}` : u.email;
  }

  get isOrgAdmin(): boolean {
    return this.auth.currentUser?.role === 'OrgAdmin';
  }

  get userInitials(): string {
    const u = this.auth.currentUser;
    if (!u) return '?';
    if (u.firstName) return `${u.firstName[0]}${u.lastName?.[0] ?? ''}`.toUpperCase();
    return u.email?.[0]?.toUpperCase() ?? '?';
  }
}
