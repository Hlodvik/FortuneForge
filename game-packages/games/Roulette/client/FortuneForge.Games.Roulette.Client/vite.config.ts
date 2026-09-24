import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  base: './',
  plugins: [react()],
  server: { port: 5175, strictPort: true, proxy: { '/api': 'http://127.0.0.1:5185' } },
  build: { lib: { entry: 'src/index.ts', formats: ['es'], fileName: 'index' }, rollupOptions: { external: ['react', 'react-dom', 'react/jsx-runtime'] }, cssCodeSplit: false },
  test: { environment: 'node' },
})
