import { defineConfig } from 'vite'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  root: fileURLToPath(new URL('./', import.meta.url)),
  server: {
    host: '127.0.0.1', port: 5187, strictPort: true,
    fs: { allow: [fileURLToPath(new URL('../../../../../', import.meta.url))] },
    proxy: { '/api': { target: 'http://127.0.0.1:5279', changeOrigin: false } },
  },
})
