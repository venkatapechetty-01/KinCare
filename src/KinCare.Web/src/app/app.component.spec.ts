import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { AppComponent } from './app.component';
import { AuthService } from './shared/auth/auth.service';
import { RideService } from './shared/services/ride.service';

describe('AppComponent', () => {
  beforeEach(async () => {
    const authServiceStub = {
      isAuthenticated: () => false,
      logout: () => undefined,
      currentUser: null,
    } as Partial<AuthService> as AuthService;

    const rideServiceStub = {
      getTodayCount: () => of({ count: 0 }),
    } as Partial<RideService> as RideService;

    await TestBed.configureTestingModule({
      imports: [AppComponent, RouterTestingModule],
      providers: [
        { provide: AuthService, useValue: authServiceStub },
        { provide: RideService, useValue: rideServiceStub },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have the 'KinCare.Web' title`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.componentInstance;
    expect(app.title).toEqual('KinCare.Web');
  });

  it('should contain a router-outlet', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });
});
