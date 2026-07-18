import { test, expect } from '@playwright/test';

const API_URL = process.env.API_URL || 'http://localhost:8080';
const TEST_EMAIL = process.env.E2E_TEST_EMAIL || 'debug@debugtest.com';
const TEST_PASSWORD = process.env.E2E_TEST_PASSWORD || 'DebugPass123!';

test.describe('Auth flow debug', () => {
  test('login → dashboard → no redirect loop @smoke', async ({ page }) => {
    const requests: { url: string; status: number }[] = [];

    page.on('response', (response) => {
      const url = response.url();
      if (url.includes('localhost:8080')) {
        requests.push({ url, status: response.status() });
      }
    });

    await page.goto('/login');
    await expect(page.locator('input[formControlName="email"]')).toBeVisible();

    await page.fill('input[formControlName="email"]', TEST_EMAIL);
    await page.fill('input[formControlName="password"]', TEST_PASSWORD);
    await page.click('button[type="submit"]');

    // Should land on dashboard within 5s
    await page.waitForURL('**/dashboard', { timeout: 5000 });
    await expect(page).toHaveURL(/dashboard/);

    // Allow any post-load API calls to settle
    await page.waitForTimeout(2000);

    console.log('API calls made:');
    requests.forEach((r) => console.log(`  ${r.status} ${r.url}`));

    // No 401s after login (login itself is fine to have 401 for bad creds, but not after success)
    const postLoginRequests = requests.slice(
      requests.findIndex((r) => r.url.includes('/auth/login') && r.status === 200) + 1
    );
    const unauthorized = postLoginRequests.filter((r) => r.status === 401);
    expect(unauthorized, `Unexpected 401s after login: ${JSON.stringify(unauthorized)}`).toHaveLength(0);
  });

  test('no login loop — login does not redirect back to /login', async ({ page }) => {
    const urlsVisited: string[] = [];
    page.on('framenavigated', (frame) => {
      if (frame === page.mainFrame()) urlsVisited.push(frame.url());
    });

    await page.goto('/login');
    await page.fill('input[formControlName="email"]', TEST_EMAIL);
    await page.fill('input[formControlName="password"]', TEST_PASSWORD);
    await page.click('button[type="submit"]');

    await page.waitForURL('**/dashboard', { timeout: 5000 });
    await page.waitForTimeout(1000);

    console.log('URLs visited:', urlsVisited);

    // Should not have bounced back to login after going to dashboard
    const loginAfterDashboard = urlsVisited
      .slice(urlsVisited.findIndex((u) => u.includes('dashboard')) + 1)
      .some((u) => u.includes('login'));

    expect(loginAfterDashboard, 'Was redirected back to login after reaching dashboard').toBe(false);
  });

  test('token persists — refresh page stays on dashboard', async ({ page }) => {
    await page.goto('/login');
    await page.fill('input[formControlName="email"]', TEST_EMAIL);
    await page.fill('input[formControlName="password"]', TEST_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL('**/dashboard', { timeout: 5000 });

    // Reload — should stay on dashboard, not bounce to login
    await page.reload();
    await page.waitForTimeout(2000);
    await expect(page).toHaveURL(/dashboard/);
  });

  test('unauthenticated access to dashboard redirects to login', async ({ page }) => {
    // A fresh Playwright context already has no cookies/localStorage, but navigate to the
    // app's origin first — calling localStorage.clear() before any navigation throws a
    // SecurityError in Chromium (localStorage is inaccessible on the opaque about:blank origin).
    await page.goto('/login');
    await page.context().clearCookies();
    await page.evaluate(() => localStorage.clear());

    await page.goto('/dashboard');
    await expect(page).toHaveURL(/login/);
  });

  test('API auth/login returns accessToken with correct claims', async ({ request }) => {
    const response = await request.post(`${API_URL}/api/auth/login`, {
      data: { email: TEST_EMAIL, password: TEST_PASSWORD },
    });

    expect(response.status()).toBe(200);
    const body = await response.json();

    expect(body.accessToken).toBeTruthy();
    expect(body.refreshToken).toBeTruthy();

    // Decode JWT and verify claims
    const payload = JSON.parse(Buffer.from(body.accessToken.split('.')[1], 'base64url').toString());
    console.log('JWT claims:', payload);

    expect(payload.sub).toBeTruthy();
    expect(payload.email).toBe(TEST_EMAIL);
    expect(payload.organization_id).toBeTruthy();
    expect(payload.role).toBeTruthy();
    expect(payload.exp).toBeGreaterThan(Date.now() / 1000);
  });

  test('authenticated API call succeeds immediately after login', async ({ request }) => {
    const loginResp = await request.post(`${API_URL}/api/auth/login`, {
      data: { email: TEST_EMAIL, password: TEST_PASSWORD },
    });
    expect(loginResp.status()).toBe(200);
    const { accessToken } = await loginResp.json();

    const ridesResp = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    console.log('rides/today status:', ridesResp.status());
    expect(ridesResp.status()).toBe(200);
  });
});
