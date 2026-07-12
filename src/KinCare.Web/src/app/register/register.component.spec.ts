import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RegisterComponent } from './register.component';
import { AuthService } from '../shared/auth/auth.service';

describe('RegisterComponent', () => {
  let component: RegisterComponent;
  let fixture: ComponentFixture<RegisterComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['register']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    await TestBed.configureTestingModule({
      imports: [RegisterComponent, NoopAnimationsModule],
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('orgForm', () => {
    it('should have organizationName, facilityName, facilityAddress controls', () => {
      expect(component.orgForm.contains('organizationName')).toBeTrue();
      expect(component.orgForm.contains('facilityName')).toBeTrue();
      expect(component.orgForm.contains('facilityAddress')).toBeTrue();
    });

    it('should require organizationName', () => {
      component.orgForm.controls['organizationName'].setValue('');
      expect(component.orgForm.controls['organizationName'].valid).toBeFalse();
    });

    it('should enforce max length of 200 for organizationName', () => {
      component.orgForm.controls['organizationName'].setValue('A'.repeat(201));
      expect(component.orgForm.controls['organizationName'].valid).toBeFalse();
    });

    it('should require facilityName', () => {
      component.orgForm.controls['facilityName'].setValue('');
      expect(component.orgForm.controls['facilityName'].valid).toBeFalse();
    });

    it('should require facilityAddress', () => {
      component.orgForm.controls['facilityAddress'].setValue('');
      expect(component.orgForm.controls['facilityAddress'].valid).toBeFalse();
    });
  });

  describe('accountForm', () => {
    it('should have firstName, lastName, email, password controls', () => {
      expect(component.accountForm.contains('firstName')).toBeTrue();
      expect(component.accountForm.contains('lastName')).toBeTrue();
      expect(component.accountForm.contains('email')).toBeTrue();
      expect(component.accountForm.contains('password')).toBeTrue();
    });

    it('should require firstName', () => {
      component.accountForm.controls['firstName'].setValue('');
      expect(component.accountForm.controls['firstName'].valid).toBeFalse();
    });

    it('should require lastName', () => {
      component.accountForm.controls['lastName'].setValue('');
      expect(component.accountForm.controls['lastName'].valid).toBeFalse();
    });

    it('should require valid email', () => {
      component.accountForm.controls['email'].setValue('bad');
      expect(component.accountForm.controls['email'].valid).toBeFalse();
    });

    it('should require password minimum 8 characters', () => {
      component.accountForm.controls['password'].setValue('short');
      expect(component.accountForm.controls['password'].valid).toBeFalse();
    });

    it('should accept valid password of 8+ characters', () => {
      component.accountForm.controls['password'].setValue('ValidPass1');
      expect(component.accountForm.controls['password'].valid).toBeTrue();
    });
  });

  describe('submit', () => {
    function fillValidForms(): void {
      component.orgForm.controls['organizationName'].setValue('My Org');
      component.orgForm.controls['facilityName'].setValue('Main');
      component.orgForm.controls['facilityAddress'].setValue('123 St');
      component.accountForm.controls['firstName'].setValue('John');
      component.accountForm.controls['lastName'].setValue('Doe');
      component.accountForm.controls['email'].setValue('john@test.com');
      component.accountForm.controls['password'].setValue('Password1!');
    }

    it('should not call service if orgForm is invalid', () => {
      component.accountForm.controls['firstName'].setValue('J');
      component.accountForm.controls['lastName'].setValue('D');
      component.accountForm.controls['email'].setValue('j@t.com');
      component.accountForm.controls['password'].setValue('Pass1234');
      // orgForm left empty

      component.submit();

      expect(authServiceSpy.register).not.toHaveBeenCalled();
    });

    it('should not call service if accountForm is invalid', () => {
      component.orgForm.controls['organizationName'].setValue('Org');
      component.orgForm.controls['facilityName'].setValue('Fac');
      component.orgForm.controls['facilityAddress'].setValue('Addr');
      // accountForm left empty

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
        organizationName: 'My Org',
        facilityName: 'Main',
        facilityAddress: '123 St',
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        password: 'Password1!',
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

      expect(routerSpy.navigate).toHaveBeenCalledWith(['/dashboard']);
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

      expect(component.error).toBe('Registration failed.');
    });
  });
});
