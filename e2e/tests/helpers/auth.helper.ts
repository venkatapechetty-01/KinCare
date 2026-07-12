import { Page, APIRequestContext, expect } from '@playwright/test';

export const API_URL = process.env.API_URL || 'http://localhost:5000';
export const BASE_URL = process.env.BASE_URL || 'http://localhost:4200';

export interface TestCredentials {
  email: string;
  password: string;
}

/** Returns test credentials from env vars, or null if not set (tests should skip). */
export function getTestCredentials(): TestCredentials | null {
  const email = process.env.E2E_TEST_EMAIL;
  const password = process.env.E2E_TEST_PASSWORD;
  if (!email || !password) return null;
  return { email, password };
}

/** Returns OrgAdmin credentials if set. */
export function getOrgAdminCredentials(): TestCredentials | null {
  const email = process.env.E2E_ORG_ADMIN_EMAIL || process.env.E2E_TEST_EMAIL;
  const password = process.env.E2E_ORG_ADMIN_PASSWORD || process.env.E2E_TEST_PASSWORD;
  if (!email || !password) return null;
  return { email, password };
}

/** Log in via the UI and return once redirected to /dashboard. */
export async function loginViaUI(page: Page, creds: TestCredentials): Promise<void> {
  await page.goto('/login');
  await page.fill('input[formControlName="email"]', creds.email);
  await page.fill('input[formControlName="password"]', creds.password);
  await page.click('button[type="submit"]');
  await page.waitForURL('**/dashboard', { timeout: 15000 });
}

/** Log in via API and return the access token. */
export async function loginViaAPI(request: APIRequestContext, creds: TestCredentials): Promise<string> {
  const res = await request.post(`${API_URL}/api/auth/login`, {
    data: { email: creds.email, password: creds.password },
  });
  expect(res.ok(), `Login failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  const body = await res.json();
  return body.accessToken;
}

/** Register a brand-new OrgAdmin account and return its access token. */
export async function registerNewOrg(
  request: APIRequestContext,
  overrides: Partial<{
    organizationName: string;
    facilityName: string;
    facilityAddress: string;
    firstName: string;
    lastName: string;
    email: string;
    password: string;
  }> = {}
): Promise<{ accessToken: string; email: string }> {
  const ts = Date.now();
  const email = overrides.email ?? `e2e-${ts}@kincare-test.invalid`;
  const payload = {
    organizationName: overrides.organizationName ?? `E2E Org ${ts}`,
    facilityName: overrides.facilityName ?? `E2E Facility ${ts}`,
    facilityAddress: overrides.facilityAddress ?? '1 Test Ave, Austin TX',
    firstName: overrides.firstName ?? 'E2E',
    lastName: overrides.lastName ?? 'Tester',
    email,
    password: overrides.password ?? 'E2ePassw0rd!',
    role: 'OrgAdmin',
    timezone: 'America/Chicago',
    billingEmail: email,
  };

  // Retry up to 4 times with backoff to handle rate-limit (3/min/IP) across parallel workers
  let res = await request.post(`${API_URL}/api/onboarding/register`, { data: payload });
  for (let attempt = 1; attempt < 4 && res.status() === 429; attempt++) {
    await new Promise(r => setTimeout(r, attempt * 5000));
    const retryPayload = { ...payload, email: `e2e-${Date.now()}@kincare-test.invalid` };
    res = await request.post(`${API_URL}/api/onboarding/register`, { data: retryPayload });
  }
  expect(res.ok(), `Register failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  const body = await res.json();
  return { accessToken: body.accessToken, email: body.email ?? payload.email };
}
