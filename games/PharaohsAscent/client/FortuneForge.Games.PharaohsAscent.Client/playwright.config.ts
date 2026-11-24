import { defineConfig } from '@playwright/test'
import { resolve } from 'node:path'

export default defineConfig({
  testDir: './tests', testMatch: 'paid-browser.spec.ts', fullyParallel: false, workers: 1, retries: 0,
  timeout: 300_000, reporter: 'list',
  outputDir: resolve(import.meta.dirname, '../../../../output/artwork/pharaohs-ascent/browser/playwright'),
  use: { baseURL: 'http://127.0.0.1:5176', viewport: { width: 1500, height: 1000 }, reducedMotion: 'reduce', trace: 'retain-on-failure' },
})
