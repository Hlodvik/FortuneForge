import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e/ux-cycle', testMatch: 'game-leave-guard.ux.spec.ts',
  outputDir: '../output/integration-2026-11-02/leave-navigation/browser-results',
  workers: 1, fullyParallel: false, retries: 0, timeout: 90_000, reporter: 'list',
  expect: { timeout: 12_000 },
  use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:4204', locale: 'en-ZA', colorScheme: 'dark',
    contextOptions: { reducedMotion: 'reduce' }, serviceWorkers: 'block', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: { command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4204 --strictPort',
    url: 'http://127.0.0.1:4204', reuseExistingServer: false, timeout: 120_000 },
})
