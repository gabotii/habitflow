import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against a running stack (API + web). Start them with `npm run dev` + the API, or Docker Compose,
 * then run `npm run e2e`. E2E_BASE_URL points at the web app; E2E_CHANNEL can select an installed browser (msedge).
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  // One browser at a time: each test drives a whole user journey against one shared API and database. Two
  // parallel browsers on a low-memory machine made a cold API stall for minutes (see README, "Pruebas").
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:4173',
    locale: 'es-ES',
    timezoneId: 'Europe/Madrid',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], channel: process.env.E2E_CHANNEL || undefined },
      testIgnore: /responsive.spec.ts/,
    },
    {
      name: 'mobile',
      use: { ...devices['Pixel 7'], channel: process.env.E2E_CHANNEL || undefined },
      testMatch: /responsive\.spec\.ts/,
    },
  ],
})
