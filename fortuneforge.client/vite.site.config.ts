// Edited: 2026-11-18T22:49:50-06:00 | Computer: Vinnabok
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { siteBuildBoundary } from './build/siteBuildBoundary.mjs'
import { SITE_ASSET_DIRECTORY, SITE_OUTPUT_DIRECTORY } from '../scripts/site-release-contract.mjs'

const clientRoot = fileURLToPath(new URL('.', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

export default defineConfig({
  root: process.env.VITEST ? clientRoot : fileURLToPath(new URL('./site', import.meta.url)),
  publicDir: false,
  plugins: [react(), siteBuildBoundary(repositoryRoot)],
  resolve: {
    dedupe: ['react', 'react-dom'],
    alias: {
      '@fortuneforge/slot-client-core/components/ResponsiveSlotImage': fileURLToPath(new URL('../packages/slot-client-core/src/components/ResponsiveSlotImage.tsx', import.meta.url)),
    },
  },
  build: {
    outDir: resolve(repositoryRoot, SITE_OUTPUT_DIRECTORY),
    emptyOutDir: true,
    assetsDir: SITE_ASSET_DIRECTORY,
    rollupOptions: {
      output: {
        entryFileNames: `${SITE_ASSET_DIRECTORY}/[name]-[hash].js`,
        chunkFileNames: `${SITE_ASSET_DIRECTORY}/[name]-[hash].js`,
        assetFileNames: `${SITE_ASSET_DIRECTORY}/[name]-[hash][extname]`,
      },
    },
  },
})
