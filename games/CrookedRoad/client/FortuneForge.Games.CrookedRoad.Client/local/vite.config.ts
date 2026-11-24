import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

const localRoot = fileURLToPath(new URL('.', import.meta.url))
const packageRoot = fileURLToPath(new URL('..', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('../../../../..', import.meta.url))

export default defineConfig({
  root: localRoot,
  plugins: [react()],
  cacheDir: fileURLToPath(new URL('../node_modules/.vite-local', import.meta.url)),
  resolve: { dedupe: ['react', 'react-dom'] },
  optimizeDeps: { noDiscovery: true, include: ['react', 'react-dom/client', 'react/jsx-runtime'] },
  server: {
    host: '127.0.0.1',
    port: 5175,
    strictPort: true,
    fs: { allow: [packageRoot, repositoryRoot] },
    watch: { ignored: ['**/fortuneforge.client/**', '**/workhistory.md'] },
    proxy: {
      '/api': { target: process.env.FORTUNEFORGE_API_URL ?? 'http://127.0.0.1:5299', changeOrigin: true },
    },
  },
})
