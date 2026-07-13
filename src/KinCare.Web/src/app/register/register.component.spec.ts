import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RegisterComponent } from './register.component';
import { AuthService } from '../shared/auth/auth.service';

describe('RegisterComponent', () => {
  let component: RegisterComponent;
  let fixture: ComponentFixture<RegisterComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let navigateSpy: jasmine.Spy;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['register'], {
      currentUser: { role: 'OrgAdmin', organizationId: 'org-1' },
    });

    await TestBed.configureTestingModule({
      imports: [RegisterComponent, NoopAnimationsModule],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate');
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('form controls', () => {
    it('should have organizationName, facilityName, facilityAddress controls', () => {
      expect(component.form.contains('organizationName')).toBeTrue();
      expect(component.form.contains('facilityName')).toBeTrue();
      expect(component.form.contains('facilityAddress')).toBeTrue();
    });

    it('should require organizationName', () => {
      component.form.controls['organizationName'].setValue('');
      expect(component.form.controls['organizationName'].valid).toBeFalse();
    });

    it('should enforce max length of 200 for organizationName', () => {
      component.form.controls['organizationName'].setValue('A'.repeat(201));
      expect(component.form.controls['organizationName'].valid).toBeFalse();
    });

    it('should require facilityName', () => {
      component.form.controls['facilityName'].setValue('');
      expect(component.form.controls['facilityName'].valid).toBeFalse();
    });

    it('should require facilityAddress', () => {
      component.form.controls['facilityAddress'].setValue('');
      expect(component.form.controls['facilityAddress'].valid).toBeFalse();
    });

    it('should have firstName, lastName, email, password controls', () => {
      expect(component.form.contains('firstName')).toBeTrue();
      expect(component.form.contains('lastName')).toBeTrue();
      expect(component.form.contains('email')).toBeTrue();
      expect(component.form.contains('password')).toBeTrue();
    });

    it('should require firstName', () => {
      component.form.controls['firstName'].setValue('');
      expect(component.form.controls['firstName'].valid).toBeFalse();
    });

    it('should require lastName', () => {
      component.form.controls['lastName'].setValue('');
      expect(component.form.controls['lastName'].valid).toBeFalse();
    });

    it('should require valid email', () => {
      component.form.controls['email'].setValue('bad');
      expect(component.form.controls['email'].valid).toBeFalse();
    });

    it('should require password minimum 12 characters', () => {
      component.form.controls['password'].setValue('short');
      expect(component.form.controls['password'].valid).toBeFalse();
    });

    it('should accept valid password of 12+ characters', () => {
      component.form.controls['password'].setValue('ValidPass123!');
      expect(component.form.controls['password'].valid).toBeTrue();
    });
  });

  describe('submit', () => {
    function fillValidForms(): void {
      component.form.controls['organizationName'].setValue('My Org');
      component.form.controls['facilityName'].setValue('Main');
      component.form.controls['facilityAddress'].setValue('123 St');
      component.form.controls['firstName'].setValue('John');
      component.form.controls['lastName'].setValue('Doe');
      component.form.controls['email'].setValue('john@test.com');
      component.form.controls['password'].setValue('Password123!');
    }

    it('should not call service if form is invalid', () => {
      component.form.controls['firstName'].setValue('J');
      component.form.controls['lastName'].setValue('D');
      component.form.controls['email'].setValue('j@t.com');
      component.form.controls['password'].setValue('Pass1234');

      component.submit();

      expect(authServiceSpy.register).not.toHaveBeenCalled();
    });

    it('should call service with merged form values', () => {
      authServiceSpy.register.and.returnValue(of({
        accessToken: 'tok',
        refreshToken: 'ref',
        organizationId: 'org-1',
        facilityId: 'fac-1',
        userId: 'u-1',
      }));
      fillValidForms();

      component.submit();

      expect(authServiceSpy.register).toHaveBeenCalledWith({
        role: 'OrgAdmin',
        organizationName: 'My Org',
        facilityName: 'Main',
        facilityAddress: '123 St',
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        password: 'Password123!',
      });
    });

    it('should navigate to /dashboard on success', () => {
      authServiceSpy.register.and.returnValue(of({
        accessToken: 'tok',
        refreshToken: 'ref',
        organizationId: 'org-1',
        facilityId: 'fac-1',
        userId: 'u-1',
      }));
      fillValidForms();

      component.submit();

      expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
    });

    it('should set error on failure', () => {
      authServiceSpy.register.and.returnValue(
        throwError(() => ({ error: { errors: ['Email already in use'] } }))
      );
      fillValidForms();

      component.submit();

      expect(component.error).toBe('Email already in use');
    });

    it('should set generic error when no specific error', () => {
      authServiceSpy.register.and.returnValue(throwError(() => ({})));
      fillValidForms();

      component.submit();

      expect(component.error).toBe('Registration failed (unknown error). Please try again.');
    });
  });
});
