import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AcceptInviteComponent } from './accept-invite.component';
import { AuthService } from '../shared/auth/auth.service';

describe('AcceptInviteComponent', () => {
  let component: AcceptInviteComponent;
  let fixture: ComponentFixture<AcceptInviteComponent>;
  let httpMock: HttpTestingController;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  function createComponent(token: string = 'valid-token') {
    TestBed.configureTestingModule({
      imports: [AcceptInviteComponent, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => token } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AcceptInviteComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  }

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['acceptInvite']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
  });

  afterEach(() => {
    if (httpMock) httpMock.verify();
  });

  it('should create', () => {
    createComponent();
    fixture.detectChanges();
    const req = httpMock.expectOne('http://localhost:8080/api/onboarding/invite/valid-token');
    req.flush({ email: 'user@test.com', role: 'Coordinator', organizationName: 'Org', facilityName: 'Fac' });
    expect(component).toBeTruthy();
  });

  it('should set error when no token in route', () => {
    createComponent('');
    fixture.detectChanges();
    expect(component.error).toBe('Invalid invitation link.');
    expect(component.loading).toBeFalse();
  });

  it('should fetch invite details on init', () => {
    createComponent('test-token');
    fixture.detectChanges();

    const req = httpMock.expectOne('http://localhost:8080/api/onboarding/invite/test-token');
    expect(req.request.method).toBe('GET');
    req.flush({
      email: 'invited@test.com',
      role: 'Coordinator',
      organizationName: 'Test Org',
      facilityName: 'Test Facility',
    });

    expect(component.invite).toBeTruthy();
    expect(component.invite!.email).toBe('invited@test.com');
    expect(component.loading).toBeFalse();
  });

  it('should set error when invite fetch fails', () => {
    createComponent('bad-token');
    fixture.detectChanges();

    const req = httpMock.expectOne('http://localhost:8080/api/onboarding/invite/bad-token');
    req.flush({ error: 'Invitation not found.' }, { status: 404, statusText: 'Not Found' });

    expect(component.error).toBe('Invitation not found.');
    expect(component.loading).toBeFalse();
  });

  describe('form validation', () => {
    beforeEach(() => {
      createComponent();
      fixture.detectChanges();
      const req = httpMock.expectOne('http://localhost:8080/api/onboarding/invite/valid-token');
      req.flush({ email: 'u@t.com', role: 'Coordinator', organizationName: 'O', facilityName: 'F' });
    });

    it('should require firstName', () => {
      component.form.controls['firstName'].setValue('');
      expect(component.form.controls['firstName'].valid).toBeFalse();
    });

    it('should require lastName', () => {
      component.form.controls['lastName'].setValue('');
      expect(component.form.controls['lastName'].valid).toBeFalse();
    });

    it('should require password min 8 chars', () => {
      component.form.controls['password'].setValue('short');
      expect(component.form.controls['password'].valid).toBeFalse();
    });

    it('should accept valid form', () => {
      component.form.controls['firstName'].setValue('John');
      component.form.controls['lastName'].setValue('Doe');
      component.form.controls['password'].setValue('Password1!');
      expect(component.form.valid).toBeTrue();
    });
  });

  describe('submit', () => {
    beforeEach(() => {
      createComponent('my-token');
      fixture.detectChanges();
      const req = httpMock.expectOne('http://localhost:8080/api/onboarding/invite/my-token');
      req.flush({ email: 'u@t.com', role: 'Coordinator', organizationName: 'O', facilityName: 'F' });
    });

    it('should not call service when form is invalid', () => {
      component.submit();
      expect(authServiceSpy.acceptInvite).not.toHaveBeenCalled();
    });

    it('should call acceptInvite with token and form values', () => {
      authServiceSpy.acceptInvite.and.returnValue(of({
        accessToken: 'tok',
        refreshToken: 'ref',
        userId: 'u-1',
      }));
      component.form.controls['firstName'].setValue('Jane');
      component.form.controls['lastName'].setValue('Doe');
      component.form.controls['password'].setValue('SecurePass1');

      component.submit();

      expect(authServiceSpy.acceptInvite).toHaveBeenCalledWith({
        token: 'my-token',
        firstName: 'Jane',
        lastName: 'Doe',
        password: 'SecurePass1',
      });
    });

    it('should navigate to /dashboard on success', () => {
      authServiceSpy.acceptInvite.and.returnValue(of({
        accessToken: 'tok',
        refreshToken: 'ref',
        userId: 'u-1',
      }));
      component.form.controls['firstName'].setValue('Jane');
      component.form.controls['lastName'].setValue('Doe');
      component.form.controls['password'].setValue('SecurePass1');

      component.submit();

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/dashboard']);
    });

    it('should set error on failure', () => {
      authServiceSpy.acceptInvite.and.returnValue(
        throwError(() => ({ error: { errors: ['Token expired'] } }))
      );
      component.form.controls['firstName'].setValue('Jane');
      component.form.controls['lastName'].setValue('Doe');
      component.form.controls['password'].setValue('SecurePass1');

      component.submit();

      expect(component.error).toBe('Token expired');
    });
  });
});
