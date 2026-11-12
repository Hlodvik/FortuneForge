import { embeddedSlotSmokeInventory, gameSmokeRoutes, slotSmokeInventory } from './game-smoke-inventory.mjs'

const baseUrl = normalizeBaseUrl(process.env.FORTUNE_FORGE_URL ?? process.argv[2] ?? 'https://fortuneforgegame.web.app')

if (gameSmokeRoutes.length !== 45) throw new Error(`Smoke inventory must contain 45 games, found ${gameSmokeRoutes.length}.`)
const slotGameIds = [...slotSmokeInventory.map(([, gameId]) => gameId), 'empty-slots-v1']
if (slotGameIds.length !== 20) throw new Error(`Smoke inventory must contain 20 playable slot contracts, found ${slotGameIds.length}.`)

console.log(`Fortune Forge production smoke: ${baseUrl}`)

for (const route of ['/home', ...gameSmokeRoutes]) {
  const response = await request(`${baseUrl}${route}`)
  const html = await response.text()
  assert(response.ok, `${route} returned ${response.status}`)
  assert(html.includes('<div id="root"></div>'), `${route} did not return the application shell`)
}
console.log(`✓ Home and ${gameSmokeRoutes.length} game routes returned the application shell (${embeddedSlotSmokeInventory.length} embedded Jade Dragon package)`)

for (const gameId of slotGameIds) {
  const state = await request(`${baseUrl}/api/slots/state?gameId=${encodeURIComponent(gameId)}`)
  assert(state.status === 401, `${gameId} unauthenticated slot state returned ${state.status}`)
}

for (const path of ['/api/slots/demo/status?gameId=empty-slots-v1', '/api/cards/blackjack/demo/status']) {
  const retired = await request(`${baseUrl}${path}`)
  assert(retired.status === 404, `${path} should be retired, returned ${retired.status}`)
}

const practice = await request(`${baseUrl}/api/slots/state?gameId=empty-slots-v1`, {
  headers: { 'X-FortuneForge-Practice': 'true' },
})
assert(practice.status === 404, `production practice request returned ${practice.status}`)

console.log(`✓ production smoke passed: ${gameSmokeRoutes.length} game routes, ${slotGameIds.length} authenticated slot contracts, and no public demo API`)

async function request(url, init, attempt = 1) {
  const response = await fetch(url, { ...init, signal: AbortSignal.timeout(20_000) })
  if ((response.status === 429 || response.status >= 500) && attempt < 4) {
    await new Promise(resolve => setTimeout(resolve, attempt * 1_000))
    return request(url, init, attempt + 1)
  }
  return response
}

function normalizeBaseUrl(value) {
  const url = new URL(value)
  if (url.protocol !== 'https:' && url.hostname !== 'localhost') throw new Error('Production smoke requires HTTPS unless the target is localhost.')
  return url.href.replace(/\/$/, '')
}

function assert(condition, message) {
  if (!condition) throw new Error(message)
}
