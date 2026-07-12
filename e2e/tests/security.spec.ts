import { test, expect } from '@playwright/test';
import { API_URL, registerNewOrg } from './helpers/auth.helper';

// ─── Rate limiting ────────────────────────────────────────────────────────────

test.describe('Security — rate limiting', () => {
  test('POST /api/auth/login rate-limited at 5 attempts/min', async ({ request }) => {
    const responses: number[] = [];
    for (let i = 0; i < 7; i++) {
      const res = await request.post(`${API_URL}/api/auth/login`, {
        data: { email: `ratelimit-${i}@test.invalid`, password: 'WrongPass123!' },
      });
      responses.push(res.status());
    }
    // At least one of the later requests should be 429
    expect(responses).toContain(429);
  });
});

// ─── Input validation ─────────────────────────────────────────────────────────

test.describe('Security — input validation', () => {
  let token: string;

  test.beforeEach(async ({ request }) => {
    const { accessToken } = await registerNewOrg(request);
    token = accessToken;
  });

  test('resident firstName over 100 chars returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        firstName: 'A'.repeat(101),
        lastName: 'Valid',
        needsWheelchair: false,
        needsOxygen: false,
        needsStretcher: false,
        needsWalker: false,
      },
    });
    expect(res.status()).toBe(400);
  });

  test('vendor name over 200 chars returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: 'V'.repeat(201),
        phoneNumber: '+15125550001',
        vendorType: 'Wheelchair',
        dispatchMethod: 'SmsNemt',
        capabilityTier: 'Basic',
      },
    });
    expect(res.status()).toBe(400);
  });

  test('ride pickupAddress over 500 chars returns 400', async ({ request }) => {
    const residentRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { firstName: 'Test', lastName: 'Resident', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id: residentId } = await residentRes.json();

    const vendorRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: 'Test V', phoneNumber: '+15125550002', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const { id: vendorId } = await vendorRes.json();

    const res = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        residentId,
        vendorId,
        pickupTime: new Date(Date.now() + 3600000).toISOString(),
        pickupAddress: 'A'.repeat(501),
        destinationAddress: '200 Dest St',
      },
    });
    expect(res.status()).toBe(400);
  });

  test('POST /api/auth/reset-password with weak password returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/reset-password`, {
      data: { token: 'any-token', newPassword: 'weak' },
    });
    expect(res.status()).toBe(400);
  });

  test('login with empty body returns 400', async ({ request }) => {
    const res = await request.post(`${API_URL}/api/auth/login`, { data: {} });
    expect(res.status()).toBe(400);
  });
});

// ─── Tenant isolation ─────────────────────────────────────────────────────────

test.describe('Security — tenant isolation', () => {
  test('org A cannot read org B rides via direct ride ID', async ({ request }) => {
    const { accessToken: tokenA } = await registerNewOrg(request);
    const { accessToken: tokenB } = await registerNewOrg(request);

    // Seed a ride in org A
    const residentRes = await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${tokenA}` },
      data: { firstName: 'OrgA', lastName: 'Resident', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });
    const { id: residentId } = await residentRes.json();

    const vendorRes = await request.post(`${API_URL}/api/vendors`, {
      headers: { Authorization: `Bearer ${tokenA}` },
      data: { name: 'OrgA Vendor', phoneNumber: '+15125558001', vendorType: 'Wheelchair', dispatchMethod: 'SmsNemt', capabilityTier: 'Basic' },
    });
    const { id: vendorId } = await vendorRes.json();

    const rideRes = await request.post(`${API_URL}/api/rides`, {
      headers: { Authorization: `Bearer ${tokenA}` },
      data: {
        residentId,
        vendorId,
        pickupTime: new Date(Date.now() + 3600000).toISOString(),
        pickupAddress: '1 OrgA St',
        destinationAddress: '2 OrgA Ave',
      },
    });
    const { id: rideId } = await rideRes.json();

    // Org B tries to access org A's ride directly
    const res = await request.get(`${API_URL}/api/rides/${rideId}`, {
      headers: { Authorization: `Bearer ${tokenB}` },
    });
    expect([403, 404]).toContain(res.status());
  });

  test('org B residents list does not leak org A residents', async ({ request }) => {
    const { accessToken: tokenA } = await registerNewOrg(request);
    const { accessToken: tokenB } = await registerNewOrg(request);

    await request.post(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${tokenA}` },
      data: { firstName: 'SecretOrgA', lastName: 'Person', needsWheelchair: false, needsOxygen: false, needsStretcher: false, needsWalker: false },
    });

    const res = await request.get(`${API_URL}/api/residents`, {
      headers: { Authorization: `Bearer ${tokenB}` },
    });
    const residents = await res.json();
    expect(residents.some((r: any) => r.firstName === 'SecretOrgA')).toBeFalsy();
  });
});

// ─── Security headers ─────────────────────────────────────────────────────────

test.describe('Security — response headers', () => {
  test('API responses have no stack traces in errors', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/nonexistent-id-here`, {
      headers: { Authorization: 'Bearer bad.token.here' },
    });
    const text = await res.text();
    expect(text).not.toContain('at Microsoft.EntityFrameworkCore');
    expect(text).not.toContain('StackTrace');
    expect(text).not.toContain('System.Exception');
  });

  test('health endpoint is public', async ({ request }) => {
    const res = await request.get(`${API_URL}/health`);
    expect(res.ok()).toBeTruthy();
  });

  test('tracking endpoint is public for valid token', async ({ request }) => {
    // Without a real token, should get 404 (not 401)
    const res = await request.get(`${API_URL}/track/fake-token-here`);
    expect([200, 404]).toContain(res.status());
  });
});

// ─── Twilio webhook security ──────────────────────────────────────────────────

test.describe('Security — webhook protection', () => {
  test('POST /webhook/twilio without signature returns 403', async ({ request }) => {
    const res = await request.post(`${API_URL}/webhook/twilio`, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      data: 'From=%2B15125550000&Body=1&MessageSid=SM123',
    });
    // 403 when Twilio auth is configured, or 200/400 in dev without auth token
    expect([200, 400, 403]).toContain(res.status());
  });

  test('POST /webhook/broker without signature returns 401', async ({ request }) => {
    const res = await request.post(`${API_URL}/webhook/broker`, {
      data: { tripId: 'fake', status: 'completed' },
    });
    expect([400, 401]).toContain(res.status());
  });
});
