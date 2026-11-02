import { defineConfig, devices } from '@playwright/test'

// Deliberately separate from the stopped usability-preview host on 4191.
export default defineConfig({
  testDir: './e2e/ux-cycle', testMatch: ['craps-continuation.ux.spec.ts', 'craps-betting.ux.spec.ts'],
  outputDir: '../output/maintenance-2026-11-01/craps-continuation/browser-results',
  fullyParallel: false, workers: 1, retries: 0, timeout: 40_000,
  reporter: 'list', expect: { timeout: 10_000 },
  use: { ...devices['Desktop Chrome'], baseURL: 'http://127.0.0.1:4198',
    colorScheme: 'dark', locale: 'en-ZA', reducedMotion: 'reduce',
    serviceWorkers: 'block', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: [
    { command: 'dotnet run --project ../tools/FortuneForge.CrapsBrowserFixture/FortuneForge.CrapsBrowserFixture.csproj --configuration Release',
      url: 'http://127.0.0.1:5298/__craps-fixture-health', reuseExistingServer: false, timeout: 180_000 },
    { command: 'node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 4198 --strictPort',
      url: 'http://127.0.0.1:4198', reuseExistingServer: false, timeout: 120_000 },
  ],
  projects: [{ name: 'chromium' }],
})
