import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const packageExternals = new Set(['react', 'react-dom', 'react/jsx-runtime'])
const diceSprite = /(?:^|[/\\])assets[/\\]dice[/\\]dice-throw-[1-4]\.png$/

export default defineConfig({
  plugins: [react()],
  server: { port: 5180, strictPort: true, proxy: { '/api': 'http://127.0.0.1:5190' } },
  build: { lib: { entry: 'src/index.ts', formats: ['es'], fileName: 'index' }, rollupOptions: { external: (id) => packageExternals.has(id) || diceSprite.test(id) }, cssCodeSplit: false },
  test: { environment: 'node' },
})
