import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: [
    ['html', { open: 'never' }],
    ['junit', { outputFile: 'test-results/results.xml' }],
    ['list'],
  ],
  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 15000,
    navigationTimeout: 30000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 5'] },
      testMatch: ['**/auth.spec.ts', '**/health.spec.ts'],
    },
  ],
  webServer: process.env.CI ? undefined : [
    {
      command: 'cd ../src/KinCare.Web && npm start -- --port 4200',
      url: 'http://localhost:4200',
      reuseExistingServer: true,
      timeout: 120000,
    },
    {
      command: 'cd ../src/KinCare.API && dotnet run',
      url: 'http://localhost:8080/health',
      reuseExistingServer: true,
      timeout: 60000,
      env: { PORT: '8080' },
    },
  ],
});
