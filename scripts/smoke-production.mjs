import { embeddedSlotSmokeInventory, gameSmokeRoutes, slotSmokeInventory } from './game-smoke-inventory.mjs'

const baseUrl = normalizeBaseUrl(process.env.FORTUNE_FORGE_URL ?? process.argv[2] ?? 'https://fortuneforgegame.web.app')

if (gameSmokeRoutes.length !== 45) throw new Error(`Smoke inventory must contain 45 games, found ${gameSmokeRoutes.length}.`)
if (slotSmokeInventory.length !== 19) throw new Error(`Smoke inventory must contain 19 playable slot contracts, found ${slotSmokeInventory.length}.`)

console.log(`Fortune Forge production smoke: ${baseUrl}`)

for (const route of ['/home', ...gameSmokeRoutes]) {
  const response = await request(`${baseUrl}${route}`)
  const html = await response.text()
  assert(response.ok, `${route} returned ${response.status}`)
  assert(html.includes('<div id="root"></div>'), `${route} did not return the application shell`)
}
console.log(`✓ Home and ${gameSmokeRoutes.length} game routes returned the application shell (${embeddedSlotSmokeInventory.length} embedded Jade Dragon package)`)

for (const [route, gameId] of slotSmokeInventory) {
  const status = await request(`${baseUrl}/api/slots/demo/status?gameId=${encodeURIComponent(gameId)}`)
  assert(status.status === 204, `${route} demo status returned ${status.status}`)

  const spin = await request(`${baseUrl}/api/slots/demo/spins`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      gameId,
      wagerPoints: gameId === 'neon-nights-v1' ? 50 : 10,
      useFreeSpin: false,
      freeSpinsRemaining: 0,
      freeSpinWagerPoints: null,
      energyBalance: 0,
      sealCollections: [],
      freeSpinFeatureMode: null,
      ...(gameId === 'gods-of-olympus-v1' ? { olympusTumbleShowcase: false } : {}),
    }),
  })
  const result = await spin.json().catch(() => null)
  assert(spin.ok, `${route} demo spin returned ${spin.status}: ${JSON.stringify(result)}`)
  assert(result?.gameId === gameId, `${route} demo spin returned the wrong game profile`)
  assert(typeof result?.spinId === 'string' && result.spinId.length > 0, `${route} demo spin did not return a spin ID`)
  if (gameId === 'gods-of-olympus-v1') {
    assert(result.reelSetId === 'olympus-tumble-demo-v3' && Array.isArray(result.olympusTumble?.steps),
      `${route} did not return the active tumble model`)
  }
  if (gameId === 'neon-nights-v1') {
    assert(result.neonArcade?.featureGamesEnabled === true && result.neonArcade?.boostPercentPerSymbol === 25,
      `${route} did not return the active Special Plays model`)
  }
  console.log(`✓ ${route} (${gameId}) demo contract`)
}

console.log(`✓ production smoke passed: ${gameSmokeRoutes.length} routes and ${slotSmokeInventory.length} live demo spins`)

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
