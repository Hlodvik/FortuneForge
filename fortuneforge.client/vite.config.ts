import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const buildAssetVersion = 'payment-bank-fields-v2'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        entryFileNames: `assets/[name]-${buildAssetVersion}-[hash].js`,
        chunkFileNames: `assets/[name]-${buildAssetVersion}-[hash].js`,
        assetFileNames: `assets/[name]-${buildAssetVersion}-[hash][extname]`,
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: process.env.FORTUNEFORGE_API_URL ?? 'http://localhost:5150',
        changeOrigin: true,
      },
    },
  },
})
