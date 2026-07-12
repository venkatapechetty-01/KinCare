import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, loginViaAPI, getTestCredentials, registerNewOrg } from './helpers/auth.helper';

// ─── Page load ────────────────────────────────────────────────────────────────

test.describe('Residents — page', () => {
  test('@smoke residents page loads after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/residents');
    await expect(page.locator('text=Facility Residents')).toBeVisible({ timeout: 10000 });
  });

  test('residents page shows empty state or cards', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/residents');
    // Either empty state or at least one resident card must be visible
    const hasCards = await page.locator('mat-card.resident-card').count();
    const hasEmpty = await page.locator('text=No residents found').isVisible().catch(() => false);
    expect(hasCards > 0 || hasEmpty).toBeTruthy();
  });

  test('residents page has FAB add button', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/residents');
    await expect(page.locator('button[mat-fab], button.mat-fab, button:has(mat-icon:text("add"))').first()).toBeVisible({ timeout: 10000 });
  });
});

// ─── API CRUD ─────────────────────────────────────────────────────────────────

test.describe('Residents — API CRUD', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('GET /api/residents returns empty array for new org', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBeTruthy();
    expect(body.length).toBe(0);
  });

  test('POST /api/residents creates a resident', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        firstName: 'Alice',
        lastName: 'Resident',
        needsWheelchair: false,
        needsOxygen: false,
        needsStretcher: false,
        needsWalker: false,
        driverNotes: 'E2E test resident',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.id).toBeTruthy();
    expect(body.firstName).toBe('Alice');
  });

  test('POST /api/residents with special needs flags', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        firstName: 'Bob',
        lastName: 'WheelchairUser',
        needsWheelchair: true,
        needsOxygen: true,
        needsStretcher: false,
        needsWalker: false,
        driverNotes: 'Needs wheelchair and oxygen',
      },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.needsWheelchair).toBe(true);
    expect(body.needsOxygen).toBe(true);
  });

  test('GET /api/residents returns created resident', async ({ request }) => {
    await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Carol', lastName: 'ListTest', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });

    const res = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const body = await res.json();
    expect(body.some((r: any) => r.firstName === 'Carol')).toBeTruthy();
  });

  test('PUT /api/residents/:id updates resident', async ({ request }) => {
    const createRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Dave', lastName: 'Before', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id } = await createRes.json();

    const updateRes = await request.put(`${API_URL}/api/residents/${id}`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Dave', lastName: 'After', needsWheelchair: true, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    expect(updateRes.ok()).toBeTruthy();

    const listRes = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const residents = await listRes.json();
    const updated = residents.find((r: any) => r.id === id);
    expect(updated.lastName).toBe('After');
    expect(updated.needsWheelchair).toBe(true);
  });

  test('DELETE /api/residents/:id soft-deletes resident', async ({ request }) => {
    const createRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Eve', lastName: 'ToDelete', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id } = await createRes.json();

    const deleteRes = await request.delete(`${API_URL}/api/residents/${id}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(deleteRes.ok()).toBeTruthy();

    const listRes = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const residents = await listRes.json();
    expect(residents.some((r: any) => r.id === id)).toBeFalsy();
  });

  test('POST /api/residents with missing firstName returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { lastName: 'NoFirst', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    expect(res.status()).toBe(400);
  });

  test('tenant isolation — resident from org A not visible to org B', async ({ request }) => {
    // Org B token
    const { accessToken: tokenB } = await registerNewOrg(request);

    // Create resident in org A
    await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'OrgA', lastName: 'Private', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });

    // Org B should see empty list
    const res = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${tokenB}` },
    });
    const residents = await res.json();
    expect(residents.some((r: any) => r.firstName === 'OrgA')).toBeFalsy();
  });
});
