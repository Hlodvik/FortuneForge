import { defineConfig } from 'vitest/config'
export default defineConfig({ test: { include: ['tests/**/*.test.ts', 'tests/**/*.test.tsx', 'src/**/*.test.tsx'], environment: 'jsdom' } })
