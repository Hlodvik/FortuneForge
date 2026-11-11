import { defineConfig } from 'vite'

export default defineConfig({
  build: {
    lib: {
      entry: { index: 'src/index.ts', tableApi: 'src/tableApi.ts' },
      formats: ['es'],
      cssFileName: 'index',
    },
    rolldownOptions: { external: ['react', 'react/jsx-runtime'] },
  },
})
