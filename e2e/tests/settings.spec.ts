import { test, expect } from '@playwright/test';
import path from 'path';
import { loginViaAPI, useAuthToken, getTestCredentials } from './helpers/auth.helper';

const TEST_PHOTO = path.join(__dirname, '..', 'fixtures', 'test-photo.png');

test.describe('Settings — profile photo', () => {
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

  test('@smoke settings page loads', async ({ page }) => {
    await page.goto('/settings');
    await expect(page.locator('text=Account & Settings')).toBeVisible({ timeout: 10000 });
  });

  test('uploaded photo displays immediately and survives a page reload', async ({ page }) => {
    await page.goto('/settings');

    await page.locator('input[type="file"]').setInputFiles(TEST_PHOTO);
    await page.click('button:has-text("Upload Photo")');

    // The bug this guards against: the template only ever bound the transient local file-picker
    // preview, never the persisted photoUrl returned by the upload response — so the photo
    // appeared to vanish the instant upload succeeded (photoPreview gets nulled on success).
    const photoImg = page.locator('img.preview-image');
    await expect(photoImg).toBeVisible({ timeout: 10000 });
    const srcAfterUpload = await photoImg.getAttribute('src');
    expect(srcAfterUpload).toMatch(/^data:image/);

    await page.reload();
    await expect(page.locator('img.preview-image')).toBeVisible({ timeout: 10000 });
    const srcAfterReload = await page.locator('img.preview-image').getAttribute('src');
    expect(srcAfterReload).toMatch(/^data:image/);
  });

  test('Remove Photo button is available once a photo is saved (not just mid-upload)', async ({ page }) => {
    await page.goto('/settings');

    await page.locator('input[type="file"]').setInputFiles(TEST_PHOTO);
    await page.click('button:has-text("Upload Photo")');
    await expect(page.locator('img.preview-image')).toBeVisible({ timeout: 10000 });

    // Reload so the only source of the photo is the persisted currentPhotoUrl fetched on
    // init — no local file-picker state exists at this point.
    await page.reload();
    await expect(page.locator('img.preview-image')).toBeVisible({ timeout: 10000 });
    await expect(page.locator('button:has-text("Remove Photo")')).toBeVisible({ timeout: 10000 });
  });
});
