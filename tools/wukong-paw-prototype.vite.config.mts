import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const root = fileURLToPath(new URL('../fortuneforge.client/', import.meta.url))
const repository = fileURLToPath(new URL('../', import.meta.url))

// Standalone local entry: no /api proxy, account, wallet or publication configuration.
export default defineConfig({
  root,
  plugins: [react()],
  resolve: { dedupe: ['react', 'react-dom'] },
  cacheDir: fileURLToPath(new URL('../node_modules/.vite-wukong-paw-prototype/', import.meta.url)),
  server: {
    host: '127.0.0.1', port: 4196, strictPort: true,
    fs: { allow: [repository] },
    proxy: { '/__prototype/wukong-paws': { target: 'http://127.0.0.1:4197', changeOrigin: false } },
  },
  build: {
    outDir: fileURLToPath(new URL('../output/integration-2026-11-02/wukong-paw-prototype/client/', import.meta.url)),
    emptyOutDir: false,
    rollupOptions: { input: fileURLToPath(new URL('wukong-paw-prototype.html', new URL('../fortuneforge.client/', import.meta.url))) },
  },
})
