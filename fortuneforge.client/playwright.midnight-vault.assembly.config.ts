import { defineConfig } from '@playwright/test'
import base from './playwright.config'

process.env.MIDNIGHT_LOCAL_API = '1'

export default defineConfig({
  ...base,
  testMatch: ['midnight-vault.loading.spec.ts', 'midnight-vault.assembly.spec.ts', 'midnight-vault.review-v3.spec.ts'],
  use: { ...base.use, baseURL: 'http://127.0.0.1:4189', trace: 'off' },
  webServer: {
    command: 'node scripts/serve-midnight-vault-review.mjs dist 4189 ../tools/fixtures/midnight-vault',
    url: 'http://127.0.0.1:4189/__local-review-health',
    reuseExistingServer: false,
    timeout: 120_000,
  },
})
