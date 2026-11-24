import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'
import { realpathSync } from 'node:fs'

export default defineConfig({
  root: realpathSync(fileURLToPath(new URL('.', import.meta.url))),
  test: { environment: 'jsdom', include: ['tests/**/*.test.ts', 'tests/**/*.test.tsx'], maxWorkers: 2 },
})
