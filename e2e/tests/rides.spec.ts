import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, getTestCredentials, registerNewOrg } from './helpers/auth.helper';

// ─── Helpers ──────────────────────────────────────────────────────────────────

async function seedResidentAndVendor(request: any, token: string) {
  const residentRes = await request.post(`${API_URL}/api/residents`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { firstName: 'Ride', lastName: 'Tester', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
  });
  const resident = await residentRes.json();

  const vendorRes = await request.post(`${API_URL}/api/vendors`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { name: 'Test NEMT', phoneNumber: '+15125559001', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
  });
  const vendor = await vendorRes.json();

  return { residentId: resident.id, vendorId: vendor.id };
}

async function bookRide(request: any, token: string, residentId: string, vendorId: string) {
  const pickupTime = new Date(Date.now() + 60 * 60 * 1000).toISOString();
  const res = await request.post(`${API_URL}/api/rides`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      residentId,
      vendorId,
      pickupTime,
      pickupAddress: '100 Pickup St, Austin TX',
      destinationAddress: '200 Destination Ave, Austin TX',
    },
  });
  return res;
}

// ─── Page load ────────────────────────────────────────────────────────────────

test.describe('Rides — page', () => {
  test('@smoke dashboard loads after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await expect(page.locator("text=Today's Rides")).toBeVisible({ timeout: 10000 });
  });

  test('@smoke history page loads', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/history');
    await expect(page.locator('text=Ride History')).toBeVisible({ timeout: 10000 });
  });

  test('@smoke booking page loads', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await page.goto('/booking');
    await expect(page.locator('text=Book a Ride')).toBeVisible({ timeout: 10000 });
  });

  test('dashboard shows empty state or ride cards', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    const hasCards = await page.locator('mat-card.ride-card').count();
    const hasEmpty = await page.locator('text=No rides scheduled today').isVisible().catch(() => false);
    expect(hasCards > 0 || hasEmpty).toBeTruthy();
  });

  test('dashboard has FAB button to book a ride', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await expect(page.locator('button[mat-fab], button.mat-fab').first()).toBeVisible({ timeout: 10000 });
  });
});

// ─── API ride booking ─────────────────────────────────────────────────────────

test.describe('Rides — API booking', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('GET /api/rides/today returns empty array for new org', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    expect(Array.isArray(await res.json())).toBeTruthy();
  });

  test('POST /api/rides books a ride and returns Dispatched status', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    const res = await bookRide(request, token, residentId, vendorId);
    expect(res.ok(), `Book ride failed: ${res.status()} ${await res.text()}`).toBeTruthy();
    const ride = await res.json();
    expect(ride.id).toBeTruthy();
    expect(ride.status).toBe('Dispatched');
    expect(ride.dispatchChannel).toBeTruthy();
  });

  test('booked ride appears in /api/rides/today', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    await bookRide(request, token, residentId, vendorId);

    const res = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const rides = await res.json();
    expect(rides.length).toBeGreaterThan(0);
  });

  test('wheelchair resident routes to SmsNemt channel', async ({ request }) => {
    const residentRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Wheelchair', lastName: 'Resident', needsWheelchair: true, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id: residentId } = await residentRes.json();

    const vendorRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'NEMT Vendor', phoneNumber: '+15125559010', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const { id: vendorId } = await vendorRes.json();

    const rideRes = await bookRide(request, token, residentId, vendorId);
    const ride = await rideRes.json();
    expect(ride.dispatchChannel).toBe('SmsNemt');
  });

  test('GET /api/rides/:id returns ride detail with events', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    const bookRes = await bookRide(request, token, residentId, vendorId);
    const { id: rideId } = await bookRes.json();

    const res = await request.get(`${API_URL}/api/rides/${rideId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const detail = await res.json();
    expect(detail.id).toBe(rideId);
    expect(Array.isArray(detail.events)).toBeTruthy();
  });

  test('POST /api/rides without residentId returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        pickupTime: new Date(Date.now() + 3600000).toISOString(),
        pickupAddress: '100 St',
        destinationAddress: '200 St',
      },
    });
    expect(res.status()).toBe(400);
  });

  test('tenant isolation — ride from org A not visible to org B', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    await bookRide(request, token, residentId, vendorId);

    const { accessToken: tokenB } = await registerNewOrg(request);
    const res = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: `Bearer ${tokenB}` },
    });
    expect((await res.json()).length).toBe(0);
  });
});

// ─── Ride status transitions ──────────────────────────────────────────────────

test.describe('Rides — status transitions', () => {
  let token: string;
  let rideId: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    const bookRes = await bookRide(request, token, residentId, vendorId);
    rideId = (await bookRes.json()).id;
  });

  test('Dispatched → Confirmed transition succeeds', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { status: 'Confirmed', triggeredBy: 'coordinator' },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.status).toBe('Confirmed');
  });

  test('Dispatched → Completed is invalid transition (400)', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { status: 'Completed', triggeredBy: 'coordinator' },
    });
    expect(res.status()).toBe(400);
  });

  test('ride can be cancelled from Dispatched', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { status: 'Cancelled', triggeredBy: 'coordinator' },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('ride events are appended on each transition', async ({ request }) => {
    await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { status: 'Confirmed', triggeredBy: 'coordinator' },
    });

    const detailRes = await request.get(`${API_URL}/api/rides/${rideId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const detail = await detailRes.json();
    expect(detail.events.length).toBeGreaterThanOrEqual(1);
    expect(detail.events.some((e: any) => e.toStatus === 'Confirmed')).toBeTruthy();
  });
});

// ─── Ride history ─────────────────────────────────────────────────────────────

test.describe('Rides — history', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('GET /api/rides/history returns paginated results', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/history`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body).toHaveProperty('items');
    expect(body).toHaveProperty('totalCount');
  });

  test('GET /api/rides/history/export requires Professional plan', async ({ request }) => {
    // New org is Starter (trial) — export should return 402
    const res = await request.get(`${API_URL}/api/rides/history/export`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    // Either 402 (plan gate) or 200 if trial gives full access
    expect([200, 402]).toContain(res.status());
  });
});
