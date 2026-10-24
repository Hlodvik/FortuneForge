import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  server: { port: 5186, strictPort: true },
  resolve: {
    dedupe: ['react', 'react-dom'],
    alias: [
      { find: /^@fortuneforge\/games-texas-holdem\/styles\.css$/, replacement: fileURLToPath(new URL('./src/holdem.css', import.meta.url)) },
      { find: /^@fortuneforge\/games-texas-holdem$/, replacement: fileURLToPath(new URL('./src/index.ts', import.meta.url)) },
    ],
  },
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: 'index' },
    rollupOptions: { external: ['react', 'react-dom', 'react/jsx-runtime'] },
  },
  test: { environment: 'node' },
})
