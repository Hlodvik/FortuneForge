import { defineConfig } from '@playwright/test'
import base from './playwright.config'

// This suite renders synthetic markup and needs no API fixture.
export default defineConfig({ ...base, testMatch: 'game-style-isolation.spec.ts' })
