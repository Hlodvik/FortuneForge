import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  testMatch: 'crooked-road.paid.spec.ts',
  outputDir: '../output/artwork/irish-crooked-road-2026-11-22/browser/playwright',
  reporter: [['list'], ['json', { outputFile: '../output/artwork/irish-crooked-road-2026-11-22/browser/results.json' }]],
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 360_000,
  use: { baseURL: process.env.CROOKED_ROAD_TEST_URL ?? 'http://127.0.0.1:5174', viewport: { width: 1600, height: 1000 },
    reducedMotion: 'reduce', trace: 'retain-on-failure' },
})

