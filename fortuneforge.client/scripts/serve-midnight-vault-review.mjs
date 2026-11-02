import { createServer } from 'node:http'
import { createReadStream } from 'node:fs'
import { readFile, stat } from 'node:fs/promises'
import { resolve, sep, extname } from 'node:path'

// Local review fixture only. No request is proxied to an account or game server.
const root = resolve(process.argv[2])
const port = Number(process.argv[3] ?? 4188)
const fixtureRoot = resolve(process.argv[4] ?? '../tools/fixtures/midnight-vault')
const scenarios = new Set(['base-loss', 'base-win', 'scatter', 'collection-sync', 'collection-rows', 'collection-paw', 'collection-rand'])
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.webp': 'image/webp', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.wav': 'audio/wav', '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg' }
async function spinFixture(input, scenario) {
  if (!scenarios.has(scenario)) throw new Error('Unknown local review scenario')
  // These are serialized real DemoSlotsController responses. Collection
  // scenarios intentionally start from a seeded, nearly complete access track.
  const fixture = JSON.parse(await readFile(resolve(fixtureRoot, `${scenario}.json`), 'utf8'))
  const step = input.useFreeSpin
    ? fixture.steps.find(item => item.request.useFreeSpin && item.request.freeSpinsRemaining === input.freeSpinsRemaining)
    : fixture.steps[0]
  if (!step || input.wagerPoints !== fixture.wagerPoints) throw new Error('Request does not match the local engine fixture')
  return step.response
}
createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname)
    const referringPage = new URL(request.headers.referer ?? '/', 'http://127.0.0.1')
    if (pathname === '/__local-review-health') { response.writeHead(200); response.end('local mocked review'); return }
    if (pathname === '/api/catalog/visibility') {
      response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
      response.end(JSON.stringify({ version: 1, games: [{ id: 'dragon-hoard', route: '/slots/dragon-hoard', visible: true }] })); return
    }
    if (pathname === '/api/slots/demo/status') { response.writeHead(204); response.end(); return }
    if (pathname === '/api/slots/demo/spins' && request.method === 'POST') {
      let body = ''
      for await (const chunk of request) { body += chunk; if (body.length > 8192) throw new Error('Oversized fixture request') }
      const input = JSON.parse(body)
      if (input.gameId !== 'dragon-hoard-v1') { response.writeHead(400); response.end(); return }
      const scenario = referringPage.searchParams.get('review') ?? 'collection-rows'
      const result = await spinFixture(input, scenario)
      response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
      response.end(JSON.stringify(result)); return
    }
    if (pathname.startsWith('/api/')) { response.writeHead(401); response.end(); return }
    const delay = Math.min(2_000, Math.max(0, Number(referringPage.searchParams.get('reviewDelayMs') ?? 0)))
    if (delay && /\/manifest-[^/]+\.js$/.test(pathname)) await new Promise(resolveDelay => setTimeout(resolveDelay, delay))
    let file = resolve(root, `.${pathname}`)
    if (!file.startsWith(root + sep)) { response.writeHead(403); response.end(); return }
    let info
    try { info = await stat(file); if (!info.isFile()) throw new Error('not a file') }
    catch { file = resolve(root, 'index.html'); info = await stat(file) }
    const tag = `"${info.size.toString(16)}-${Math.trunc(info.mtimeMs).toString(16)}"`
    const headers = {
      'Content-Type': mime[extname(file)] ?? 'application/octet-stream',
      // Same asset policy as the repository's firebase.json; no hosting changes.
      'Cache-Control': pathname.startsWith('/assets/') ? 'public,max-age=31536000,immutable' : 'no-cache,max-age=0,must-revalidate',
      ETag: tag,
    }
    if (request.headers['if-none-match'] === tag) { response.writeHead(304, headers); response.end(); return }
    response.writeHead(200, { ...headers, 'Content-Length': info.size })
    if (request.method === 'HEAD') response.end()
    else createReadStream(file).pipe(response)
  } catch { response.writeHead(500); response.end('Local review fixture error') }
}).listen(port, '127.0.0.1', () => console.log(`Local mocked review: http://127.0.0.1:${port}`))
