import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, getOrgAdminCredentials, registerNewOrg } from './helpers/auth.helper';

// ─── Page load ────────────────────────────────────────────────────────────────

test.describe('Org — page', () => {
  test('@smoke org page loads for OrgAdmin', async ({ page }) => {
    const creds = getOrgAdminCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/org');
    await expect(page.locator('text=Organization Management')).toBeVisible({ timeout: 10000 });
  });

  test('org page shows team table', async ({ page }) => {
    const creds = getOrgAdminCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/org');
    await expect(page.locator('text=Team')).toBeVisible({ timeout: 10000 });
  });

  test('org page has Invite User button', async ({ page }) => {
    const creds = getOrgAdminCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/org');
    await expect(page.locator('button:has-text("Invite User")')).toBeVisible({ timeout: 10000 });
  });

  test('org page has Manage Branches link', async ({ page }) => {
    const creds = getOrgAdminCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/org');
    await expect(page.locator('a[href="/branches"], a:has-text("Manage Branches")')).toBeVisible({ timeout: 10000 });
  });

  test('unauthenticated user redirected from /org to /login', async ({ page }) => {
    await page.goto('/org');
    await expect(page).toHaveURL(/\/login/);
  });
});

// ─── API org admin ────────────────────────────────────────────────────────────

test.describe('Org — API', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('GET /api/org/facilities returns the org\'s facilities', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/org/facilities`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBeTruthy();
    expect(body.length).toBeGreaterThanOrEqual(1); // org created with 1 facility
  });

  test('POST /api/org/facilities creates a new facility', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/org/facilities`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'Second Branch',
        address: '500 Branch Ave, Austin TX',
        timezone: 'America/Chicago',
        uberHealthEnabled: false,
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.id).toBeTruthy();
    expect(body.name).toBe('Second Branch');
  });

  test('GET /api/org/users returns org users', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/org/users`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBeTruthy();
    expect(body.length).toBeGreaterThanOrEqual(1); // at least the OrgAdmin
  });

  test('GET /api/org/metrics returns metrics object', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/org/metrics`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(typeof body.facilityCount).toBe('number');
    expect(typeof body.ridesThisMonth).toBe('number');
    expect(typeof body.completionRate).toBe('number');
  });

  test('POST /api/org/invite sends an invitation', async ({ request }) => {
    // Get facility ID first
    const facilitiesRes = await request.get(`${API_URL}/api/org/facilities`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const facilities = await facilitiesRes.json();
    const facilityId = facilities[0]?.id;

    const res = await request.post(`${API_URL}/api/org/invite`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        email: `invite-${Date.now()}@kincare-test.invalid`,
        role: 'Coordinator',
        facilityId,
      },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('org admin endpoints require auth', async ({ request }) => {
    const endpoints = [
      `${API_URL}/api/org/facilities`,
      `${API_URL}/api/org/users`,
      `${API_URL}/api/org/metrics`,
    ];
    for (const url of endpoints) {
      const res = await request.get(url);
      expect(res.status(), `Expected 401 for ${url}`).toBe(401);
    }
  });
});

// ─── Branch (facility) management ────────────────────────────────────────────

test.describe('Branches — page', () => {
  test('@smoke branches page loads for OrgAdmin', async ({ page }) => {
    const creds = getOrgAdminCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/branches');
    await expect(page.locator('text=Branches, text=Facilities').first()).toBeVisible({ timeout: 10000 });
  });

  test('non-OrgAdmin cannot access /branches', async ({ page }) => {
    // Without login (unauthenticated), should go to /login
    await page.goto('/branches');
    await expect(page).toHaveURL(/\/(login|dashboard)/);
  });
});
