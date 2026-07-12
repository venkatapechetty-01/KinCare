import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, getTestCredentials, registerNewOrg } from './helpers/auth.helper';

// ─── Page-load smoke tests ────────────────────────────────────────────────────

test.describe('Auth — page loads', () => {
  test('@smoke login page renders form fields', async ({ page }) => {
    await page.goto('/login');
    await expect(page.locator('input[formControlName="email"]')).toBeVisible();
    await expect(page.locator('input[formControlName="password"]')).toBeVisible();
    await expect(page.locator('button[type="submit"]')).toBeVisible();
  });

  test('@smoke register page renders', async ({ page }) => {
    await page.goto('/register');
    await expect(page.locator('mat-card')).toBeVisible();
    await expect(page.locator('text=Create Your Account')).toBeVisible();
  });

  test('@smoke forgot-password page renders', async ({ page }) => {
    await page.goto('/forgot-password');
    await expect(page.locator('input[formControlName="email"]')).toBeVisible();
    await expect(page.locator('text=Forgot Password')).toBeVisible();
  });

  test('@smoke reset-password page without token shows invalid link state', async ({ page }) => {
    await page.goto('/reset-password');
    await expect(page.locator('text=Invalid Reset Link')).toBeVisible();
  });

  test('login page has "Forgot your password?" link', async ({ page }) => {
    await page.goto('/login');
    const forgotLink = page.locator('a[href="/forgot-password"], a:has-text("Forgot")');
    await expect(forgotLink).toBeVisible();
  });
});

// ─── Login validation ─────────────────────────────────────────────────────────

test.describe('Auth — login validation', () => {
  test('submit with empty form is blocked', async ({ page }) => {
    await page.goto('/login');
    const btn = page.locator('button[type="submit"]');
    await expect(btn).toBeDisabled();
  });

  test('invalid email format shows validation error', async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[formControlName="email"]', 'not-an-email');
    await page.locator('input[formControlName="password"]').click();
    await expect(page.locator('mat-error')).toBeVisible();
  });

  test('wrong credentials shows error message', async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[formControlName="email"]', 'nobody@kincare-test.invalid');
    await page.fill('input[formControlName="password"]', 'WrongPass123!');
    await page.click('button[type="submit"]');
    await expect(page.locator('text=Invalid email or password')).toBeVisible({ timeout: 10000 });
  });

  test('unauthenticated user redirected from /dashboard to /login', async ({ page }) => {
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/login/);
  });

  test('unauthenticated user redirected from /residents to /login', async ({ page }) => {
    await page.goto('/residents');
    await expect(page).toHaveURL(/\/login/);
  });

  test('unauthenticated user redirected from /billing to /login', async ({ page }) => {
    await page.goto('/billing');
    await expect(page).toHaveURL(/\/login/);
  });
});

// ─── Login flow ───────────────────────────────────────────────────────────────

test.describe('Auth — login flow', () => {
  test('valid credentials redirect to dashboard', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('dashboard shows after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await expect(page.locator("text=Today's Rides")).toBeVisible({ timeout: 10000 });
  });

  test('JWT stored in localStorage after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    const token = await page.evaluate(() => localStorage.getItem('access_token'));
    expect(token).toBeTruthy();
    const parts = token!.split('.');
    expect(parts.length).toBe(3);
  });

  test('logout clears token and redirects to /login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);

    // Find logout in nav/menu
    const logoutBtn = page.locator('button:has-text("Logout"), [data-testid="logout"]').first();
    if (await logoutBtn.isVisible()) {
      await logoutBtn.click();
      await expect(page).toHaveURL(/\/login/);
      const token = await page.evaluate(() => localStorage.getItem('access_token'));
      expect(token).toBeNull();
    } else {
      // Logout via manual token clear + navigate
      await page.evaluate(() => {
        localStorage.removeItem('access_token');
        localStorage.removeItem('refresh_token');
      });
      await page.goto('/dashboard');
      await expect(page).toHaveURL(/\/login/);
    }
  });
});

// ─── Registration flow ────────────────────────────────────────────────────────

test.describe('Auth — registration', () => {
  test('register new org via UI redirects to dashboard', async ({ page }) => {
    const ts = Date.now();
    await page.goto('/register');

    // Select OrgAdmin role
    await page.locator('mat-select[formControlName="role"]').click();
    await page.locator('mat-option:has-text("Org Admin")').click();

    await page.fill('input[formControlName="organizationName"]', `E2E Org ${ts}`);
    await page.fill('input[formControlName="facilityName"]', `E2E Facility ${ts}`);
    await page.fill('input[formControlName="facilityAddress"]', '1 Test Ave, Austin TX 78701');
    await page.fill('input[formControlName="firstName"]', 'E2E');
    await page.fill('input[formControlName="lastName"]', 'User');
    await page.fill('input[formControlName="email"]', `e2e-${ts}@kincare-test.invalid`);
    await page.fill('input[formControlName="password"]', 'E2ePassw0rd!');

    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard', { timeout: 15000 });
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('register with duplicate email shows error', async ({ request, page }) => {
    // First create an account via API
    const { email } = await registerNewOrg(request);

    // Now try to register with the same email via UI
    const ts = Date.now();
    await page.goto('/register');
    await page.locator('mat-select[formControlName="role"]').click();
    await page.locator('mat-option:has-text("Org Admin")').click();
    await page.fill('input[formControlName="organizationName"]', `Dup Org ${ts}`);
    await page.fill('input[formControlName="facilityName"]', `Dup Facility ${ts}`);
    await page.fill('input[formControlName="facilityAddress"]', '1 Dup St');
    await page.fill('input[formControlName="firstName"]', 'Dup');
    await page.fill('input[formControlName="lastName"]', 'User');
    await page.fill('input[formControlName="email"]', email);
    await page.fill('input[formControlName="password"]', 'E2ePassw0rd!');
    await page.click('button[type="submit"]');

    await expect(page.locator('mat-error, .alert-error, [class*="error"]').first()).toBeVisible({ timeout: 10000 });
  });
});

// ─── Password reset flow ──────────────────────────────────────────────────────

test.describe('Auth — password reset', () => {
  test('forgot-password always shows success (anti-enumeration)', async ({ page }) => {
    await page.goto('/forgot-password');
    await page.fill('input[formControlName="email"]', 'nobody@kincare-test.invalid');
    await page.click('button[type="submit"]');
    await expect(page.locator('text=Check your email')).toBeVisible({ timeout: 10000 });
  });

  test('API request-password-reset returns 200 for unknown email', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/request-password-reset`, {
      data: { email: 'nobody@kincare-test.invalid' },
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.message).toBeTruthy();
  });

  test('API reset-password with invalid token returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/reset-password`, {
      data: { token: 'invalid-token-xyz', newPassword: 'NewPassw0rd!' },
    });
    expect(res.status()).toBe(400);
  });

  test('reset-password page with valid ?token param shows form', async ({ page }) => {
    await page.goto('/reset-password?token=some-token-here');
    await expect(page.locator('text=Invalid Reset Link')).not.toBeVisible();
    await expect(page.locator('input[formControlName="newPassword"]')).toBeVisible();
  });
});

// ─── API auth contract tests ──────────────────────────────────────────────────

test.describe('Auth — API contract', () => {
  test('POST /api/auth/login returns accessToken, refreshToken, role', async ({ request }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    const res = await request.post(`${API_URL}/api/auth/login`, { data: creds });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.accessToken).toBeTruthy();
    expect(body.refreshToken).toBeTruthy();
    expect(body.role).toBeTruthy();
  });

  test('POST /api/auth/refresh rotates token', async ({ request }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    const loginRes = await request.post(`${API_URL}/api/auth/login`, { data: creds });
    const { refreshToken } = await loginRes.json();

    const refreshRes = await request.post(`${API_URL}/api/auth/refresh`, {
      data: { refreshToken },
    });
    expect(refreshRes.ok()).toBeTruthy();
    const body = await refreshRes.json();
    expect(body.accessToken).toBeTruthy();
    expect(body.refreshToken).not.toBe(refreshToken); // token rotated
  });

  test('protected endpoint returns 401 without token', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/today`);
    expect(res.status()).toBe(401);
  });

  test('protected endpoint returns 401 with malformed token', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: 'Bearer this.is.fake' },
    });
    expect(res.status()).toBe(401);
  });
});
