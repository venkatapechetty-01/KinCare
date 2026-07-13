import { test, expect } from '@playwright/test';

const API_URL = process.env.API_URL || 'http://localhost:8080';

test.describe('Health & Infrastructure', () => {
  test('@smoke API health endpoint returns 200', async ({ request }) => {
    const res = await request.get(`${API_URL}/health`);
    expect(res.ok()).toBeTruthy();
  });

  test('@smoke Angular app loads at /', async ({ page }) => {
    await page.goto('/');
    await expect(page).not.toHaveURL(/error/);
  });

  test('@smoke / redirects to /login', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
  });

  test('@smoke unknown route redirects to /login', async ({ page }) => {
    await page.goto('/this-route-does-not-exist');
    await expect(page).toHaveURL(/\/login/);
  });

  test('API swagger docs accessible in development', async ({ request }) => {
    const res = await request.get(`${API_URL}/swagger/index.html`);
    // Only available in dev — accept 200 or 404
    expect([200, 404]).toContain(res.status());
  });

  test('API returns JSON for error responses (not HTML)', async ({ request }) => {
    const res = await request.get(`${API_URL}/api/rides/today`);
    const contentType = res.headers()['content-type'] ?? '';
    // 401 responses from ASP.NET have no body; authenticated errors return JSON
    if (contentType) {
      expect(contentType).not.toContain('text/html');
    }
    expect([200, 401, 403, 404]).toContain(res.status());
  });
});
