import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e/ux-cycle', testMatch: ['navbar-consistency.ux.spec.ts', 'blackjack.ux.spec.ts'],
  outputDir: '../output/maintenance-2026-11-01/navbar-consistency/browser-results',
  fullyParallel: false, workers: 1, retries: 0, timeout: 120_000,
  expect: { timeout: 15_000 }, reporter: 'list',
  use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:4202', colorScheme: 'dark',
    contextOptions: { reducedMotion: 'reduce' }, serviceWorkers: 'block', locale: 'en-ZA',
    trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: { command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4202 --strictPort',
    url: 'http://127.0.0.1:4202', reuseExistingServer: true, timeout: 120_000 },
  projects: [{ name: 'chromium' }],
})
