import { test, expect } from '@playwright/test';
import { loginViaAPI, useAuthToken, getTestCredentials } from './helpers/auth.helper';

test.describe('Live Map — Leaflet + LocationIQ tiles', () => {
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

  test('@smoke live map page loads and mounts the map canvas', async ({ page }) => {
    await page.goto('/live-map');
    await expect(page.locator('text=Live Operations Map')).toBeVisible({ timeout: 10000 });

    // The canvas must be present regardless of whether any ride currently has live GPS —
    // this was a real bug: the canvas div only existed in the DOM behind an @if gated on
    // rides.length > 0, so Angular's @ViewChild('mapCanvas') was undefined at init and the
    // map silently never rendered on the (common) empty-state case.
    await expect(page.locator('.leaflet-map-canvas')).toBeVisible({ timeout: 10000 });
  });

  test('map tiles actually render (Leaflet + LocationIQ, not a blank/broken canvas)', async ({ page }) => {
    await page.goto('/live-map');
    await expect(page.locator('.leaflet-map-canvas')).toBeVisible({ timeout: 10000 });

    // Leaflet renders tiles as <img class="leaflet-tile"> elements inside the canvas once
    // the tile layer loads — this is the real signal that LocationIQ tiles are being served,
    // not just that the container div exists.
    await expect(page.locator('.leaflet-tile-loaded').first()).toBeVisible({ timeout: 15000 });
  });

  test('no Google Maps JS SDK errors and no Leaflet console errors on load', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });
    page.on('pageerror', err => errors.push(err.message));

    await page.goto('/live-map');
    await page.waitForTimeout(3000);

    const mapErrors = errors.filter(e =>
      e.includes('Google Maps') || e.includes('ApiProjectMapError') ||
      e.includes('nativeElement') || e.includes('[LiveMap]')
    );
    expect(mapErrors, `Unexpected map errors: ${JSON.stringify(mapErrors)}`).toHaveLength(0);
  });
});
