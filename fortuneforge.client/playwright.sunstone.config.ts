import { defineConfig } from '@playwright/test'
import base from './playwright.config'

export default defineConfig({
  ...base,
  testMatch: 'sunstone-eclipse.spec.ts',
  outputDir: '../artifacts/sunstone-eclipse/playwright',
  reporter: [['list'], ['json', { outputFile: '../artifacts/sunstone-eclipse/browser-results.json' }]],
  retries: 0,
  maxFailures: 1,
  timeout: 45_000,
  use: { ...base.use, baseURL: 'http://127.0.0.1:4189', trace: 'retain-on-failure' },
  webServer: {
    command: 'npx vite preview --host 127.0.0.1 --port 4189 --strictPort',
    url: 'http://127.0.0.1:4189',
    reuseExistingServer: false,
    timeout: 30_000,
  },
})
