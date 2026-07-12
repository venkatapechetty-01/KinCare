# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: health.spec.ts >> Health & Infrastructure >> API returns JSON for error responses (not HTML)
- Location: tests/health.spec.ts:32:7

# Error details

```
Error: expect(received).toContain(expected) // indexOf

Expected substring: "application/json"
Received string:    ""
```

# Test source

```ts
  1  | import { test, expect } from '@playwright/test';
  2  | 
  3  | const API_URL = process.env.API_URL || 'http://localhost:5000';
  4  | 
  5  | test.describe('Health & Infrastructure', () => {
  6  |   test('@smoke API health endpoint returns 200', async ({ request }) => {
  7  |     const res = await request.get(`${API_URL}/health`);
  8  |     expect(res.ok()).toBeTruthy();
  9  |   });
  10 | 
  11 |   test('@smoke Angular app loads at /', async ({ page }) => {
  12 |     await page.goto('/');
  13 |     await expect(page).not.toHaveURL(/error/);
  14 |   });
  15 | 
  16 |   test('@smoke / redirects to /login', async ({ page }) => {
  17 |     await page.goto('/');
  18 |     await expect(page).toHaveURL(/\/login/);
  19 |   });
  20 | 
  21 |   test('@smoke unknown route redirects to /login', async ({ page }) => {
  22 |     await page.goto('/this-route-does-not-exist');
  23 |     await expect(page).toHaveURL(/\/login/);
  24 |   });
  25 | 
  26 |   test('API swagger docs accessible in development', async ({ request }) => {
  27 |     const res = await request.get(`${API_URL}/swagger/index.html`);
  28 |     // Only available in dev — accept 200 or 404
  29 |     expect([200, 404]).toContain(res.status());
  30 |   });
  31 | 
  32 |   test('API returns JSON for error responses (not HTML)', async ({ request }) => {
  33 |     const res = await request.get(`${API_URL}/api/rides/today`);
  34 |     const contentType = res.headers()['content-type'] ?? '';
> 35 |     expect(contentType).toContain('application/json');
     |                         ^ Error: expect(received).toContain(expected) // indexOf
  36 |   });
  37 | });
  38 | 
```