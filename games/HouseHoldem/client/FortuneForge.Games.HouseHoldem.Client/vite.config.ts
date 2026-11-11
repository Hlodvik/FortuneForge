import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: 'index', cssFileName: 'index' },
    rolldownOptions: { external: ['react', 'react-dom', 'react/jsx-runtime'] },
  },
})
