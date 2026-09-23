import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  // This library is hosted inside the main Fortune Forge bundle. Keep image
  // URLs relative to this package's module instead of treating them as site
  // root assets, which leaves the playfield without its artwork after deploy.
  base: './',
  plugins: [react()],
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'] },
    rollupOptions: {
      external: ['react', 'react-dom', 'react/jsx-runtime', 'react/jsx-dev-runtime'],
    },
  },
})
