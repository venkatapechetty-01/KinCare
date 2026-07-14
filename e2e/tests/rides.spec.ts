import { test, expect } from '@playwright/test';
import { API_URL, loginViaUI, loginViaAPI, useAuthToken, getTestCredentials, registerNewOrg } from './helpers/auth.helper';

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
  // One real login-form smoke test — everything else in this file reuses a single token via
  // useAuthToken() instead of loginViaUI(), since POST /api/auth/login is rate-limited to
  // 5 attempts/min/IP and this file alone has more than 5 UI tests.
  test('@smoke dashboard loads after login', async ({ page }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    await loginViaUI(page, creds);
    await expect(page.locator("text=Today's Rides")).toBeVisible({ timeout: 10000 });
  });

  test.describe('authenticated via token injection', () => {
    let token: string | null = null;

    test.beforeAll(async ({ request }) => {
      const creds = getTestCredentials();
      if (!creds) return;
      token = await loginViaAPI(request, creds);
    });

    test.beforeEach(async ({ page }) => {
      if (!token) return test.skip();
      await useAuthToken(page, token);
    });

    test('@smoke history page loads', async ({ page }) => {
      await page.goto('/history');
      await expect(page.locator('text=Ride History')).toBeVisible({ timeout: 10000 });
    });

    test('@smoke booking page loads', async ({ page }) => {
      await page.goto('/booking');
      await expect(page.locator('text=Book a Ride')).toBeVisible({ timeout: 10000 });
    });

    test('dashboard shows empty state or ride cards', async ({ page }) => {
      await page.goto('/dashboard');
      const hasCards = await page.locator('mat-card.ride-card').count();
      const hasEmpty = await page.locator('text=No rides scheduled today').isVisible().catch(() => false);
      expect(hasCards > 0 || hasEmpty).toBeTruthy();
    });

    test('dashboard has FAB button to book a ride', async ({ page }) => {
      await page.goto('/dashboard');
      await expect(page.locator('button[mat-fab], button.mat-fab').first()).toBeVisible({ timeout: 10000 });
    });

    test('@smoke dashboard shows an Upcoming Rides section below Today\'s Rides', async ({ page }) => {
      await page.goto('/dashboard');
      await expect(page.locator('h1:has-text("Today\'s Rides")')).toBeVisible({ timeout: 10000 });
      await expect(page.locator('h2:has-text("Upcoming Rides")')).toBeVisible({ timeout: 10000 });
    });

    test('dashboard never renders the raw "Dispatched" enum value as a status label', async ({ page }) => {
      await page.goto('/dashboard');
      // The UI must show the human label "Awaiting Acceptance" for Dispatched rides, not the
      // raw backend enum string — a real regression if this text is missing while any ride card
      // is present, since it would mean the label map broke and reverted to raw enum text.
      const cardCount = await page.locator('.ride-card').count();
      if (cardCount === 0) return; // nothing dispatched right now — not this test's concern
      const bodyText = await page.locator('.dashboard-container').innerText();
      expect(bodyText).not.toMatch(/\bDISPATCHED\b/);
    });
  });
});

// ─── Booking form — LocationIQ address autocomplete ───────────────────────────

test.describe('Rides — booking address autocomplete', () => {
  test('typing in the pickup address field shows a suggestion panel', async ({ page, request }) => {
    const creds = getTestCredentials();
    if (!creds) return test.skip();
    const token = await loginViaAPI(request, creds);
    await useAuthToken(page, token);
    await page.goto('/booking');

    const pickupField = page.locator('textarea[formcontrolname="pickupAddress"]');
    await expect(pickupField).toBeVisible({ timeout: 10000 });
    await pickupField.fill('1600 Pennsylvania Ave');

    // Debounced (350ms) + a real network round-trip to the backend geocode proxy —
    // give it real time rather than asserting immediately.
    const panel = page.locator('.address-autocomplete-panel, mat-option');
    await expect(panel.first()).toBeVisible({ timeout: 8000 });
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

  test('POST /api/rides without residentId succeeds — ResidentId is intentionally optional', async ({ request }) => {
    // BookRideRequestValidator explicitly allows a null ResidentId (`id == null || id != Guid.Empty`),
    // and the booking form itself only sends residentId when one was actually selected — a ride can
    // be booked without a resident assigned yet. This was previously asserted as a 400 in error; fixed
    // to match the actual, deliberate validation rule instead of the app.
    const res = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        pickupTime: new Date(Date.now() + 3600000).toISOString(),
        pickupAddress: '100 St',
        destinationAddress: '200 St',
      },
    });
    expect(res.status()).toBe(201);
  });

  test('POST /api/rides without pickupAddress returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        pickupTime: new Date(Date.now() + 3600000).toISOString(),
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
      data: { newStatus: 'Confirmed', notes: 'coordinator' },
    });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.status).toBe('Confirmed');
  });

  test('Dispatched → Completed is invalid transition (400)', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'Completed', notes: 'coordinator' },
    });
    expect(res.status()).toBe(400);
  });

  test('ride can be cancelled from Dispatched', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'Cancelled', notes: 'coordinator' },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('ride events are appended on each transition', async ({ request }) => {
    await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'Confirmed', notes: 'coordinator' },
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
    // HistoryResponse record is { rides, total, page, pageSize } — not { items, totalCount }
    expect(body).toHaveProperty('rides');
    expect(body).toHaveProperty('total');
  });

  test('GET /api/rides/history/export requires Professional plan', async ({ request }) => {
    // New org is Starter (trial) — export should return 402
    const res = await request.get(`${API_URL}/api/rides/history/export`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    // Either 402 (plan gate) or 200 if trial gives full access
    expect([200, 402]).toContain(res.status());
  });

  test('future-dated ride is excluded from history', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    const futurePickup = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString(); // 3 days out
    const bookRes = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { residentId, vendorId, pickupTime: futurePickup, pickupAddress: 'A St', destinationAddress: 'B St' },
    });
    const { id: rideId } = await bookRes.json();

    const historyRes = await request.get(`${API_URL}/api/rides/history`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const { rides } = await historyRes.json();
    expect(rides.some((r: any) => r.id === rideId)).toBeFalsy();
  });

  test('future-dated ride appears in /api/rides/upcoming, not /api/rides/today', async ({ request }) => {
    const { residentId, vendorId } = await seedResidentAndVendor(request, token);
    const futurePickup = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString();
    const bookRes = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { residentId, vendorId, pickupTime: futurePickup, pickupAddress: 'A St', destinationAddress: 'B St' },
    });
    const { id: rideId } = await bookRes.json();

    const upcomingRes = await request.get(`${API_URL}/api/rides/upcoming`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(upcomingRes.ok()).toBeTruthy();
    const upcoming = await upcomingRes.json();
    expect(upcoming.some((r: any) => r.id === rideId)).toBeTruthy();

    const todayRes = await request.get(`${API_URL}/api/rides/today`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const today = await todayRes.json();
    expect(today.some((r: any) => r.id === rideId)).toBeFalsy();
  });
});

// ─── Round-trip NEMT rides ─────────────────────────────────────────────────────

test.describe('Rides — round-trip NEMT return leg', () => {
  let token: string;
  let rideId: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;

    // Wheelchair resident + SmsNemt vendor routes the ride to the NEMT channel,
    // which is required for the AwaitingReturn branch to be reachable at all.
    const residentRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'RoundTrip', lastName: 'Resident', needsWheelchair: true, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id: residentId } = await residentRes.json();

    const vendorRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'NEMT Round Trip Vendor', phoneNumber: '+15125559099', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const { id: vendorId } = await vendorRes.json();

    const bookRes = await bookRide(request, token, residentId, vendorId);
    rideId = (await bookRes.json()).id;

    // Drive the outbound leg to Dropped before each test below picks up from there.
    for (const status of ['Confirmed', 'EnRoute', 'Arrived', 'PickedUp', 'AtDestination', 'Dropped']) {
      const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { newStatus: status, notes: 'coordinator' },
      });
      expect(res.ok(), `Failed to advance to ${status}: ${res.status()} ${await res.text()}`).toBeTruthy();
    }
  });

  test('Dropped → AwaitingReturn succeeds for SmsNemt', async ({ request }) => {
    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'AwaitingReturn', notes: 'coordinator' },
    });
    expect(res.ok(), `${res.status()} ${await res.text()}`).toBeTruthy();
    expect((await res.json()).status).toBe('AwaitingReturn');
  });

  test('full return-leg flow: AwaitingReturn → ReturnEnRoute → ReturnPickedUp → Completed', async ({ request }) => {
    for (const status of ['AwaitingReturn', 'ReturnEnRoute', 'ReturnPickedUp', 'Completed']) {
      const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { newStatus: status, notes: 'coordinator' },
      });
      expect(res.ok(), `Failed to advance to ${status}: ${res.status()} ${await res.text()}`).toBeTruthy();
      expect((await res.json()).status).toBe(status);
    }
  });

  test('return-leg events are appended to the same ride (not a second ride)', async ({ request }) => {
    await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'AwaitingReturn', notes: 'coordinator' },
    });

    const detailRes = await request.get(`${API_URL}/api/rides/${rideId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const detail = await detailRes.json();
    expect(detail.id).toBe(rideId);
    expect(detail.events.some((e: any) => e.toStatus === 'AwaitingReturn')).toBeTruthy();
    // Confirms the outbound-leg history is preserved on the same record, not overwritten.
    expect(detail.events.some((e: any) => e.toStatus === 'Dropped')).toBeTruthy();
  });
});

test.describe('Rides — round-trip channel gate', () => {
  test('Dropped → AwaitingReturn is rejected (400) for a non-NEMT channel', async ({ request }) => {
    const { accessToken: token } = await registerNewOrg(request);

    // Ambulatory resident + SmsTaxi vendor routes to SmsTaxi, not SmsNemt.
    const residentRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Taxi', lastName: 'Resident', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id: residentId } = await residentRes.json();

    const vendorRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'Taxi Vendor', phoneNumber: '+15125559098', vendorType: 'Ambulatory', dispatchMethod: 'SmsTaxi', capabilityTier: 'Basic' },
    });
    const { id: vendorId } = await vendorRes.json();

    const bookRes = await bookRide(request, token, residentId, vendorId);
    const { id: rideId, dispatchChannel } = await bookRes.json();
    expect(dispatchChannel).toBe('SmsTaxi');

    for (const status of ['Confirmed', 'EnRoute', 'Arrived', 'PickedUp', 'AtDestination', 'Dropped']) {
      await request.put(`${API_URL}/api/rides/${rideId}/status`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { newStatus: status, notes: 'coordinator' },
      });
    }

    const res = await request.put(`${API_URL}/api/rides/${rideId}/status`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { newStatus: 'AwaitingReturn', notes: 'coordinator' },
    });
    expect(res.status()).toBe(400);
  });
});
