import { createServer as createHttpServer } from 'node:http'
import { fileURLToPath } from 'node:url'
import { createServer as createViteServer } from 'vite'
import react from '@vitejs/plugin-react'
import { account, paths, houseRound, texasSession, heartsSession, slotCapture } from './navigation-review-fixtures.mjs'

// This process has no backend proxy. Every API URL is handled below, including
// unknown URLs, so a separate manual review tab cannot reach a real account.
const port = Number(process.argv.find(arg => arg.startsWith('--port='))?.slice(7) ?? 4205)
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid local review port')
const origin = `http://127.0.0.1:${port}`
const states = new Map()
let walletBalance = 1000
const modes = ['active', 'idle', 'result', 'queue', 'uncertain']
const vite = await createViteServer({
  configFile: false, root: fileURLToPath(new URL('../fortuneforge.client', import.meta.url)),
  plugins: [react()], resolve: { dedupe: ['react', 'react-dom'] }, appType: 'spa',
  server: { middlewareMode: true, hmr: false },
})

function context(request) {
  const referer = new URL(request.headers.referer ?? '/', origin)
  const game = Object.keys(paths).find(key => referer.pathname === paths[key]) ?? 'house'
  const selected = referer.searchParams.get('fixture') ?? 'active'
  const mode = modes.includes(selected) ? selected : 'active'
  const phone = referer.searchParams.get('phone') === '1'
  const key = `${game}/${mode}/${phone}`
  if (!states.has(key)) states.set(key, { mode, failed: false, balance: 1000, writes: [], slotRequests: 0, slotCompleted: false })
  return { game, phone, state: states.get(key) }
}
function json(response, value, status = 200) {
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
  response.end(JSON.stringify(value))
}
async function api(request, response, path) {
  const { phone, state } = context(request)
  const mode = state.mode === 'uncertain' ? 'idle' : state.mode
  if (path === '/api/accounts/me') return json(response, { ...account, balances: { ...account.balances, slotsCredits: walletBalance } })
  if (path === '/api/games/experience') return json(response, { error: 'Experience is deliberately unknown in the local review fixture' }, 503)
  if (path.includes('/onboarding')) return json(response, { shouldAskPlayedBefore: false, hasPlayedBefore: true })
  if (path === '/api/catalog/visibility') return json(response, { version: 1, games: Object.values(paths).concat('/slots/arcane-archives').map((route, index) => ({ id: `local-${index}`, route, visible: true })) })
  if (path === '/api/catalog/popularity') return json(response, { metric: 'recorded-plays', trackingStartedAtUtc: null, games: [] })
  if (path === '/api/catalog/recent-games') return json(response, { games: [] })
  if (path === '/api/payments/invoices') return json(response, { invoices: [] })
  if (path.includes('/history')) return json(response, path.includes('/accounts/') ? { spins: [] } : path.includes('/hearts/') ? { items: [] } : [])
  if (path === '/api/slots/state') return json(response, { freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: 0, energyBalance: 0, sealCollections: [], freeSpinFeatureMode: null })
  if (path === '/api/games/activity/presence' || path === '/api/slots/status' || path === '/api/slots/demo/status') {
    response.writeHead(204); response.end(); return
  }
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    state.writes.push(path)
    if (path === '/api/slots/spins' && request.method === 'POST') {
      let body = ''
      for await (const chunk of request) body += chunk
      const input = JSON.parse(body)
      if (input.wagerPoints !== slotCapture.request.wagerPoints) return json(response, { error: 'Use the minimum wager (2 points) for this captured fixture' }, 409)
      state.slotRequests++
      walletBalance -= slotCapture.response.wagerPoints * slotCapture.response.pointValueInCents / 100
      state.balance = walletBalance
      await new Promise(resolve => setTimeout(resolve, 700))
      walletBalance += slotCapture.response.payout.totalPoints * slotCapture.response.pointValueInCents / 100
      state.balance = walletBalance
      state.slotCompleted = true
      return json(response, { ...slotCapture.response, slotsCreditsBalance: state.balance })
    }
    if (state.mode === 'uncertain') { state.failed = true; return json(response, { error: 'Synthetic response lost; no real wager was made' }, 503) }
    return json(response, { error: 'Financial actions are disabled in this local fixture' }, 409)
  }
  if (state.failed && (path.includes('/house-holdem/') || path.includes('/texas-holdem/') || path.includes('/hearts/'))) return json(response, { error: 'Synthetic read unavailable after the uncertain request' }, 503)
  if (path === '/api/games/house-holdem/status') return json(response, { available: true, balance: state.balance, activeRoundId: mode === 'idle' ? null : 'local-open-hand', minimumAnte: 1, maximumAnte: 100, wagerIncrement: 1, maximumTrips: 500 })
  if (path.startsWith('/api/games/house-holdem/rounds/')) return json(response, houseRound(mode))
  if (path === '/api/cards/texas-holdem/credit/status') return json(response, { available: true, minimumStartPlayers: 3, maximumSeats: 5, minimumRealPlayers: 2, smallBlindCredits: .5, bigBlindCredits: 1, actionDeadlineSeconds: 30, matchDeadlineSeconds: 300 })
  if (path === '/api/cards/texas-holdem/credit/session') return json(response, texasSession(mode, phone ? 'all-in' : 'folded'))
  if (path === '/api/games/hearts/status') return json(response, { available: true, defaultTargetScore: 100, mode: 'free-play' })
  if (path === '/api/games/hearts/paid/session') return json(response, heartsSession(mode, phone))
  return json(response, { error: `No local API fixture for ${path}` }, 503)
}
const server = createHttpServer((request, response) => {
  const url = new URL(request.url ?? '/', origin)
  if (url.pathname.startsWith('/api/')) {
    void api(request, response, url.pathname).catch(error => json(response, { error: String(error) }, 500))
    return
  }
  if (url.pathname === '/__navigation-state') return json(response, { syntheticOnly: true, walletBalance, states: Object.fromEntries(states) })
  if (url.pathname === '/__navigation-fixture') {
    response.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' })
    response.end(`<title>Local navigation review</title><h1>Synthetic navigation review</h1><p>No backend proxy or real account. Buttons cannot make real financial requests. All state is process memory.</p>${Object.entries(paths).map(([game, path]) => `<h2>${game}</h2><ul>${modes.filter(mode => game !== 'house' || mode !== 'queue').map(mode => `<li><a href="${path}?fixture=${mode}">${mode}</a> / <a href="${path}?fixture=${mode}&phone=1">${mode}, all-in/between-rounds data</a></li>`).join('')}</ul>`).join('')}<p><a href="/slots/arcane-archives?fixture=idle">Arcane Archives: accepted spin may leave immediately (minimum wager)</a></p><p>Use navbar Home to open the dialog. Stay/Escape keeps the page; Leave page only navigates. Native Back/close has a generic best-effort browser prompt while active; the app does not alter browser history.</p>`)
    return
  }
  vite.middlewares(request, response)
})
server.listen(port, '127.0.0.1', () => console.log(`Synthetic-only navigation review: ${origin}/__navigation-fixture`))
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => {
  server.close(() => { void vite.close().finally(() => process.exit(0)) })
})
