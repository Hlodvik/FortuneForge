import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// A distinct asset namespace prevents browsers from reusing legacy catalogue
// chunks after a Hosting release. The content hash still changes per build.
const buildAssetVersion = 'game-catalog-v1'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Game clients are linked source packages. Resolve their peer React imports
  // from this host app so every game shares the same React runtime.
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  build: {
    emptyOutDir: true,
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
