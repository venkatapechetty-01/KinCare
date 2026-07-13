import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { LoginComponent } from './login.component';
import { AuthService } from '../shared/auth/auth.service';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let navigateSpy: jasmine.Spy;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['login'], {
      currentUser: { role: 'Coordinator', organizationId: 'org-1' },
    });

    await TestBed.configureTestingModule({
      imports: [LoginComponent, NoopAnimationsModule],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate');
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should have a form with email and password controls', () => {
    expect(component.form.contains('email')).toBeTrue();
    expect(component.form.contains('password')).toBeTrue();
  });

  it('should mark email as invalid when empty', () => {
    component.form.controls['email'].setValue('');
    expect(component.form.controls['email'].valid).toBeFalse();
  });

  it('should mark email as invalid for bad format', () => {
    component.form.controls['email'].setValue('not-an-email');
    expect(component.form.controls['email'].valid).toBeFalse();
  });

  it('should mark email as valid for proper email', () => {
    component.form.controls['email'].setValue('user@test.com');
    expect(component.form.controls['email'].valid).toBeTrue();
  });

  it('should mark password as invalid when empty', () => {
    component.form.controls['password'].setValue('');
    expect(component.form.controls['password'].valid).toBeFalse();
  });

  it('should mark password as valid when non-empty', () => {
    component.form.controls['password'].setValue('anypass');
    expect(component.form.controls['password'].valid).toBeTrue();
  });

  it('should not call auth service when form is invalid', () => {
    component.form.controls['email'].setValue('');
    component.form.controls['password'].setValue('');

    component.submit();

    expect(authServiceSpy.login).not.toHaveBeenCalled();
  });

  it('should call auth service with form values on valid submit', () => {
    authServiceSpy.login.and.returnValue(of({
      accessToken: 'tok',
      refreshToken: 'ref',
      role: 'Coordinator',
      organizationId: 'org-1',
    }));
    component.form.controls['email'].setValue('user@test.com');
    component.form.controls['password'].setValue('password123');

    component.submit();

    expect(authServiceSpy.login).toHaveBeenCalledWith({
      email: 'user@test.com',
      password: 'password123',
    });
  });

  it('should navigate to /dashboard on successful login', () => {
    authServiceSpy.login.and.returnValue(of({
      accessToken: 'tok',
      refreshToken: 'ref',
      role: 'Coordinator',
      organizationId: 'org-1',
    }));
    component.form.controls['email'].setValue('user@test.com');
    component.form.controls['password'].setValue('password123');

    component.submit();

    expect(navigateSpy).toHaveBeenCalledWith(['/dashboard']);
  });

  it('should set error message on failed login', () => {
    authServiceSpy.login.and.returnValue(throwError(() => new Error('Unauthorized')));
    component.form.controls['email'].setValue('user@test.com');
    component.form.controls['password'].setValue('wrong');

    component.submit();

    expect(component.error).toBe('Invalid email or password.');
  });

  it('should clear error before submitting', () => {
    component.error = 'Previous error';
    authServiceSpy.login.and.returnValue(of({
      accessToken: 'tok',
      refreshToken: 'ref',
      role: 'Coordinator',
      organizationId: 'org-1',
    }));
    component.form.controls['email'].setValue('user@test.com');
    component.form.controls['password'].setValue('pass');

    component.submit();

    expect(component.error).toBe('');
  });
});
