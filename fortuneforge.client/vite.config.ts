import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { neonIsolation } from './build/neonIsolation.js'

// A distinct asset namespace prevents browsers from reusing legacy catalogue
// chunks after a Hosting release. The content hash still changes per build.
const buildAssetVersion = 'game-catalog-v1'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), neonIsolation(fileURLToPath(new URL('..', import.meta.url)))],
  // Game clients are linked source packages. Resolve their peer React imports
  // from this host app so every game shares the same React runtime.
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  build: {
    emptyOutDir: true,
    rollupOptions: {
      output: {
        // Keep reusable runtime code out of the application entry chunk. A
        // game importing React or a neutral widget must not import the lobby.
        codeSplitting: {
          includeDependenciesRecursively: false,
          groups: [
            { name: 'react-runtime', test: /[/\\]node_modules[/\\](react|react-dom|scheduler)([/\\]|$)|\0rolldown\//, priority: 30 },
            { name: 'slot-client-core', test: /[/\\]packages[/\\]slot-client-core[/\\]/, priority: 20 },
            { name: 'client-presentation', test: /[/\\]packages[/\\]client-presentation[/\\]/, priority: 20 },
            { name: 'neon-nights', test: /[/\\]games[/\\]NeonNights[/\\]client[/\\]/, priority: 10 },
          ],
        },
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
