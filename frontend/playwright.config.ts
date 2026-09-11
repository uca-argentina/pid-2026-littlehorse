import { defineConfig, devices } from '@playwright/test';
import { seededAdminPassword } from './e2e/seeded-data';

const frontendUrl = 'http://localhost:4200';

// Both schemes on purpose. UseHttpsRedirection sends http to https, and the dev
// server's proxy (proxy.conf.json) talks to the https one.
const apiUrls = 'https://localhost:7176;http://localhost:5127';

// Readiness is checked over https, the same scheme the proxy uses. Asking the
// http port instead only gets the redirect, which Playwright follows straight
// back to this address anyway.
const apiReadyUrl = 'https://localhost:7176/openapi/v1.json';

const isContinuousIntegration = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  fullyParallel: true,

  // A test left focused with .only passes the suite locally and hides every
  // other test from the branch. On CI that is a failure, not a warning.
  forbidOnly: isContinuousIntegration,
  retries: isContinuousIntegration ? 2 : 0,
  workers: isContinuousIntegration ? 1 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: frontendUrl,

    // A failed run has to be diagnosable from the report, without anyone
    // reproducing it on their own machine first.
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    {
      // Staff screens are used on the tablet behind the bar, not on a phone.
      name: 'tablet',
      use: { ...devices['Desktop Chrome'], viewport: { width: 820, height: 1180 } },
    },
  ],

  // Both servers start on their own, and an already running one is reused: the
  // usual case is someone with the app open who wants to check a flow.
  webServer: [
    {
      // --no-launch-profile because launchSettings.json opens the API reference
      // in a browser on startup. That is useful when running the API by hand
      // and pure noise in the middle of a test run, so the environment it would
      // have set is set here instead.
      command: 'dotnet run --project ../backend/src/DrinkIt.Api --no-launch-profile',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: apiUrls,
        DevelopmentSeed__AdminPassword: seededAdminPassword(),
      },
      url: apiReadyUrl,

      // The dev certificate is self-signed, which is also why proxy.conf.json
      // sets "secure": false. Without this the readiness check never passes and
      // a perfectly healthy API looks like one that failed to start.
      ignoreHTTPSErrors: true,
      reuseExistingServer: !isContinuousIntegration,

      // First run restores, builds, migrates and seeds.
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'pnpm start',
      url: frontendUrl,
      reuseExistingServer: !isContinuousIntegration,
      timeout: 180_000,
    },
  ],
});
