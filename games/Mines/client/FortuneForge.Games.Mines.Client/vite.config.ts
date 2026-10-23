import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins:[react()],
  server:{host:'127.0.0.1',port:5181,strictPort:true,proxy:{'/api':'http://127.0.0.1:5201'}},
  build:{lib:{entry:'src/index.ts',formats:['es'],fileName:'index'},rollupOptions:{external:['react','react-dom','react/jsx-runtime']}},
  test:{environment:'node'},
})
