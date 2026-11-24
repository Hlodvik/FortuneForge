import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const target = new URL(process.env.FORTUNEFORGE_PHARAOH_LOCAL_API ?? 'http://127.0.0.1:5301')
assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(target.hostname), 'Local review requires a loopback test API.')
const health = await fetch(new URL('/__fixture/health', target)).then(response => response.json())
assert.ok(health.localOnly && health.productionEngine && health.testFundsOnly && health.gameId === 'pharaohs-ascent-v1',
  'Start the isolated paid harness before the review client. Real accounts and funds are not accepted.')
const server = await createServer({
  configFile: false, root: fileURLToPath(new URL('../', import.meta.url)), appType: 'mpa',
  resolve: { dedupe: ['react', 'react-dom'] },
  plugins: [{ name: 'pharaoh-local-review-entry', configureServer(server) {
    server.middlewares.use((request, _response, next) => {
      if (request.url === '/' || request.url?.startsWith('/slots/pharaohs-ascent')) request.url = '/review/index.html'
      next()
    })
  } }],
  server: { host: '127.0.0.1', port: 5176, strictPort: true,
    proxy: { '/api': target.origin, '/__fixture': target.origin } },
})
await server.listen()
console.log('Pharaoh’s Ascent local review: http://127.0.0.1:5176/slots/pharaohs-ascent')
console.log('Production game implementation; isolated test wallet and database. No public demo or deployment.')
