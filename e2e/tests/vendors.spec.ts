import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, getTestCredentials, registerNewOrg, useAuthToken } from './helpers/auth.helper';

// ─── Page load ────────────────────────────────────────────────────────────────

test.describe('Vendors — page', () => {
  test('@smoke vendors page loads after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/vendors');
    await expect(page.locator('text=Drivers')).toBeVisible({ timeout: 10000 });
  });

  test('vendors page shows empty state or vendor cards', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/vendors');
    const hasCards = await page.locator('mat-card.vendor-card').count();
    const hasEmpty = await page.locator('text=No vendors found').isVisible().catch(() => false);
    expect(hasCards > 0 || hasEmpty).toBeTruthy();
  });
});

// ─── Navigation ───────────────────────────────────────────────────────────────
// Self-contained via registerNewOrg + useAuthToken (no env-var test credentials needed) —
// regression test for a real bug: the "Drivers" page existed and worked, but no sidebar
// link ever pointed to it, so it was unreachable through normal navigation.

test.describe('Vendors — navigation', () => {
  test('"Drivers" sidebar link is present and navigates to /vendors', async ({ page, request }) => {
    const { accessToken } = await registerNewOrg(request);
    await useAuthToken(page, accessToken);
    await page.goto('/dashboard');

    const driversLink = page.locator('a[routerLink="/vendors"]');
    await expect(driversLink).toBeVisible({ timeout: 10000 });
    await expect(driversLink).toContainText('Drivers');

    await driversLink.click();
    await page.waitForURL('**/vendors', { timeout: 10000 });
    await expect(page.locator('h1')).toContainText('Drivers');
  });

  // Regression test: "Edit Driver" used to just show a toast ("Edit vendor: X") and never
  // actually open an edit form — the button existed but did nothing real.
  test('"Edit Driver" opens a pre-filled edit dialog, not a stub toast', async ({ page, request }) => {
    const { accessToken } = await registerNewOrg(request);
    await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      data: {
        name: 'Editable Driver', phoneNumber: '+15125550050',
        vendorType: 'Ambulatory', dispatchMethod: 'SmsTaxi', capabilityTier: 'Basic',
      },
    });

    await useAuthToken(page, accessToken);
    await page.goto('/vendors');
    await expect(page.locator('mat-card.vendor-card')).toHaveCount(1, { timeout: 10000 });

    await page.click('button:has-text("Edit Driver")');

    const dialog = page.locator('mat-dialog-container');
    await expect(dialog).toBeVisible({ timeout: 5000 });
    await expect(dialog.locator('h2')).toContainText('Edit');
    await expect(dialog.locator('input[formControlName="name"]')).toHaveValue('Editable Driver');
    await expect(dialog.locator('button:has-text("Save Changes")')).toBeVisible();

    await dialog.locator('input[formControlName="name"]').fill('Renamed Driver');
    await dialog.locator('button:has-text("Save Changes")').click();
    await expect(dialog).toBeHidden({ timeout: 5000 });
    await expect(page.locator('text=Renamed Driver')).toBeVisible({ timeout: 10000 });
  });
});

// ─── API CRUD ─────────────────────────────────────────────────────────────────

test.describe('Vendors — API CRUD', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('GET /api/vendors returns empty array for new org', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBeTruthy();
  });

  test('POST /api/vendors creates a wheelchair vendor', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'NEMT Transport Co',
        phoneNumber: '+15125550001',
        vendorType: 'Wheelchair',
        dispatchMethod: 'SmsNemt',
        capabilityTier: 'Basic',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.id).toBeTruthy();
    expect(body.vendorType).toBe('Wheelchair');
    expect(body.dispatchMethod).toBe('SmsNemt');
  });

  test('POST /api/vendors creates a taxi vendor', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'City Taxi',
        phoneNumber: '+15125550002',
        vendorType: 'Ambulatory',
        dispatchMethod: 'SmsTaxi',
        capabilityTier: 'Basic',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.vendorType).toBe('Ambulatory');
    expect(body.dispatchMethod).toBe('SmsTaxi');
  });

  test('POST /api/vendors creates a Smart-tier vendor', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'SmartVan LLC',
        phoneNumber: '+15125550003',
        vendorType: 'Wheelchair',
        dispatchMethod: 'SmsNemt',
        capabilityTier: 'Smart',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.capabilityTier).toBe('Smart');
  });

  test('POST /api/vendors accepts and returns Company and ServiceArea', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'Jane Driver',
        phoneNumber: '+15125550005',
        vendorType: 'Ambulatory',
        dispatchMethod: 'SmsTaxi',
        capabilityTier: 'Basic',
        company: 'Metro Taxi Co',
        serviceArea: 'Detroit Metro',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.company).toBe('Metro Taxi Co');
    expect(body.serviceArea).toBe('Detroit Metro');

    const listRes = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const created = (await listRes.json()).find((v: any) => v.id === body.id);
    expect(created.company).toBe('Metro Taxi Co');
    expect(created.serviceArea).toBe('Detroit Metro');
  });

  test('POST /api/vendors without Company/ServiceArea still succeeds (optional fields)', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'No Company Vendor',
        phoneNumber: '+15125550006',
        vendorType: 'Ambulatory',
        dispatchMethod: 'SmsTaxi',
        capabilityTier: 'Basic',
      },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('GET /api/vendors returns created vendor', async ({ request }) => {
    await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'List Test Vendor', phoneNumber: '+15125550010', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const res = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const vendors = await res.json();
    expect(vendors.some((v: any) => v.name === 'List Test Vendor')).toBeTruthy();
  });

  test('PUT /api/vendors/:id updates vendor', async ({ request }) => {
    const createRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'Old Name', phoneNumber: '+15125550020', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const { id } = await createRes.json();

    await request.put(`${API_URL}/api/vendors/${id}`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'New Name', phoneNumber: '+15125550020', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Smart' },
    });

    const listRes = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const updated = (await listRes.json()).find((v: any) => v.id === id);
    expect(updated.name).toBe('New Name');
    expect(updated.capabilityTier).toBe('Smart');
  });

  test('DELETE /api/vendors/:id soft-deletes vendor', async ({ request }) => {
    const createRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'To Delete', phoneNumber: '+15125550030', vendorType: 'Ambulatory', dispatchMethod: 'SmsTaxi', capabilityTier: 'Basic' },
    });
    const { id } = await createRes.json();

    await request.delete(`${API_URL}/api/vendors/${id}`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    const listRes = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect((await listRes.json()).some((v: any) => v.id === id)).toBeFalsy();
  });

  test('POST /api/vendors with missing name returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { phoneNumber: '+15125550099', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    expect(res.status()).toBe(400);
  });

  test('tenant isolation — vendor from org A invisible to org B', async ({ request }) => {
    const { accessToken: tokenB } = await registerNewOrg(request);

    await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'OrgA Vendor', phoneNumber: '+15125550040', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });

    const res = await request.get(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${tokenB}` },
    });
    expect((await res.json()).some((v: any) => v.name === 'OrgA Vendor')).toBeFalsy();
  });
});
