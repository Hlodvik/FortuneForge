import assert from 'node:assert/strict'
import { fileURLToPath } from 'node:url'
import { build } from 'vite'
import { readFileSync } from 'node:fs'
import { resolve, extname, sep } from 'node:path'
import { createServer, request as forward } from 'node:http'

const root = fileURLToPath(new URL('../../', import.meta.url))
const project = fileURLToPath(new URL('./', import.meta.url))
const bundle = resolve(root, 'artifacts/client-verification/review-bundle')
const ports = { pirates: 5302, olympus: 5303, jade: 5304 }
for (const port of Object.values(ports)) {
  const health = await fetch(`http://127.0.0.1:${port}/__fixture/health`).then(reply => reply.json())
  assert.ok(health.localOnly && health.productionEngine && health.testFundsOnly)
}
await build({ configFile: false, root: project, base: '/', resolve: { dedupe: ['react', 'react-dom'] },
  build: { outDir: bundle, emptyOutDir: false, rollupOptions: { input: resolve(project, 'review.html') } } })
const jade = resolve(root, 'games/JadeDragon/client/jade-dragon/dist')
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.webp': 'image/webp', '.png': 'image/png', '.mp3': 'audio/mpeg', '.ogg':'audio/ogg', '.wav':'audio/wav', '.woff': 'font/woff', '.woff2': 'font/woff2' }
const server = createServer((incoming, outgoing) => {
  const url = new URL(incoming.url, 'http://127.0.0.1:5180')
  const proxy = /^\/fixture\/(pirates|olympus|jade)(\/.*)$/.exec(url.pathname)
  if (proxy) {
    const destination = forward({ hostname: '127.0.0.1', port: ports[proxy[1]], path: proxy[2] + url.search,
      method: incoming.method, headers: { ...incoming.headers, host: `127.0.0.1:${ports[proxy[1]]}` } }, response => {
      outgoing.writeHead(response.statusCode, response.headers); response.pipe(outgoing)
    })
    destination.on('error', () => { outgoing.statusCode = 503; outgoing.end('Isolated test API unavailable') })
    incoming.pipe(destination); return
  }
  const isJade = url.pathname.startsWith('/games/jade-dragon/')
  const directory = isJade ? jade : bundle
  const file = isJade ? '.' + url.pathname.slice('/games/jade-dragon'.length)
    : url.pathname.endsWith('/review.html') || url.pathname === '/' ? 'review.html' : '.' + url.pathname
  const path = resolve(directory, file)
  if (!path.startsWith(directory + sep)) { outgoing.statusCode = 403; outgoing.end(); return }
  try { outgoing.setHeader('Content-Type', mime[extname(path)] ?? 'application/octet-stream'); outgoing.setHeader('Cache-Control','no-store'); outgoing.end(readFileSync(path)) }
  catch { outgoing.statusCode = 404; outgoing.end() }
})
server.listen(5180, '127.0.0.1', () => console.log('Production paid clients with isolated test funds: http://127.0.0.1:5180/review.html?game=pirates (or olympus, jade)'))
