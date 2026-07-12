# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: security.spec.ts >> Security — tenant isolation >> org A cannot read org B rides via direct ride ID
- Location: tests/security.spec.ts:101:7

# Error details

```
Error: Register failed: 429 API calls quota exceeded! maximum admitted 3 per 1m.

expect(received).toBeTruthy()

Received: false
```

# Test source

```ts
  1  | import { Page, APIRequestContext, expect } from '@playwright/test';
  2  | 
  3  | export const API_URL = process.env.API_URL || 'http://localhost:5000';
  4  | export const BASE_URL = process.env.BASE_URL || 'http://localhost:4200';
  5  | 
  6  | export interface TestCredentials {
  7  |   email: string;
  8  |   password: string;
  9  | }
  10 | 
  11 | /** Returns test credentials from env vars, or null if not set (tests should skip). */
  12 | export function getTestCredentials(): TestCredentials | null {
  13 |   const email = process.env.E2E_TEST_EMAIL;
  14 |   const password = process.env.E2E_TEST_PASSWORD;
  15 |   if (!email || !password) return null;
  16 |   return { email, password };
  17 | }
  18 | 
  19 | /** Returns OrgAdmin credentials if set. */
  20 | export function getOrgAdminCredentials(): TestCredentials | null {
  21 |   const email = process.env.E2E_ORG_ADMIN_EMAIL || process.env.E2E_TEST_EMAIL;
  22 |   const password = process.env.E2E_ORG_ADMIN_PASSWORD || process.env.E2E_TEST_PASSWORD;
  23 |   if (!email || !password) return null;
  24 |   return { email, password };
  25 | }
  26 | 
  27 | /** Log in via the UI and return once redirected to /dashboard. */
  28 | export async function loginViaUI(page: Page, creds: TestCredentials): Promise<void> {
  29 |   await page.goto('/login');
  30 |   await page.fill('input[formControlName="email"]', creds.email);
  31 |   await page.fill('input[formControlName="password"]', creds.password);
  32 |   await page.click('button[type="submit"]');
  33 |   await page.waitForURL('**/dashboard', { timeout: 15000 });
  34 | }
  35 | 
  36 | /** Log in via API and return the access token. */
  37 | export async function loginViaAPI(request: APIRequestContext, creds: TestCredentials): Promise<string> {
  38 |   const res = await request.post(`${API_URL}/api/auth/login`, {
  39 |     data: { email: creds.email, password: creds.password },
  40 |   });
  41 |   expect(res.ok(), `Login failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  42 |   const body = await res.json();
  43 |   return body.accessToken;
  44 | }
  45 | 
  46 | /** Register a brand-new OrgAdmin account and return its access token. */
  47 | export async function registerNewOrg(
  48 |   request: APIRequestContext,
  49 |   overrides: Partial<{
  50 |     organizationName: string;
  51 |     facilityName: string;
  52 |     facilityAddress: string;
  53 |     firstName: string;
  54 |     lastName: string;
  55 |     email: string;
  56 |     password: string;
  57 |   }> = {}
  58 | ): Promise<{ accessToken: string; email: string }> {
  59 |   const ts = Date.now();
  60 |   const email = overrides.email ?? `e2e-${ts}@kincare-test.invalid`;
  61 |   const payload = {
  62 |     organizationName: overrides.organizationName ?? `E2E Org ${ts}`,
  63 |     facilityName: overrides.facilityName ?? `E2E Facility ${ts}`,
  64 |     facilityAddress: overrides.facilityAddress ?? '1 Test Ave, Austin TX',
  65 |     firstName: overrides.firstName ?? 'E2E',
  66 |     lastName: overrides.lastName ?? 'Tester',
  67 |     email,
  68 |     password: overrides.password ?? 'E2ePassw0rd!',
  69 |     role: 'OrgAdmin',
  70 |     timezone: 'America/Chicago',
  71 |     billingEmail: email,
  72 |   };
  73 | 
  74 |   const res = await request.post(`${API_URL}/api/onboarding/register`, { data: payload });
> 75 |   expect(res.ok(), `Register failed: ${res.status()} ${await res.text()}`).toBeTruthy();
     |                                                                            ^ Error: Register failed: 429 API calls quota exceeded! maximum admitted 3 per 1m.
  76 |   const body = await res.json();
  77 |   return { accessToken: body.accessToken, email };
  78 | }
  79 | 
```