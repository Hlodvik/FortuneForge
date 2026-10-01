import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e/ux-cycle',
  outputDir: './test-results/ux-cycle',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: 'list',
  timeout: 30_000,
  expect: { timeout: 8_000 },
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://127.0.0.1:4191',
    colorScheme: 'dark',
    locale: 'en-ZA',
    timezoneId: 'Africa/Johannesburg',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4191 --strictPort',
    url: 'http://127.0.0.1:4191',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [{ name: 'chromium' }],
})
