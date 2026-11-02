import { defineConfig } from '@playwright/test'
import base from './playwright.config'

process.env.MIDNIGHT_LOCAL_API = '1'

// Exercise the locally built app, without touching the shared dist directory.
// API traffic is mocked in the spec; no production or paid play is involved.
export default defineConfig({
  ...base,
  testMatch: 'midnight-vault.loading.spec.ts',
  use: { ...base.use, baseURL: 'http://127.0.0.1:4188', trace: 'off' },
  webServer: {
    command: 'node scripts/serve-midnight-vault-review.mjs dist 4188 ../tools/fixtures/midnight-vault',
    url: 'http://127.0.0.1:4188/__local-review-health',
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
