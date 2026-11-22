import { defineConfig } from '@playwright/test'
import base from './playwright.config'

export default defineConfig({
  ...base,
  testMatch: 'fifth-sun-review.spec.ts',
  outputDir: '../artifacts/fifth-sun-redesign/browser/playwright',
  reporter: [['list'], ['json', { outputFile: '../artifacts/fifth-sun-redesign/browser/results.json' }]],
  retries: 0,
  maxFailures: 1,
  timeout: 60_000,
  use: { ...base.use, baseURL: 'http://127.0.0.1:5173', trace: 'retain-on-failure' },
  // Root owns the already running development server. This focused review never starts or stops it.
  webServer: undefined,
})
