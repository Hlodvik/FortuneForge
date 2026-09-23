const baseUrl = normalizeBaseUrl(process.env.FORTUNE_FORGE_URL ?? process.argv[2] ?? 'https://fortuneforgegame.web.app')

const slots = [
  ['wukong', 'classic-demo-v1'],
  ['rainbow-realm', 'rainbow-realm-fruits-v1'],
  ['pirates-fortune', 'pirates-fortune-v1'],
  ['gods-of-olympus', 'gods-of-olympus-v1'],
  ['reel-riches', 'reel-riches-v1'],
  ['high-noon-fortune', 'high-noon-fortune-v1'],
  ['royal-draw', 'royal-draw-v1'],
  ['arcane-archives', 'arcane-archives-v1'],
  ['cosmic-fortune', 'cosmic-fortune-v1'],
  ['dino-dominion', 'dino-dominion-v1'],
  ['neon-nights', 'neon-nights-v1'],
  ['jungle-jackpot', 'jungle-jackpot-v1'],
  ['ocean-odyssey', 'ocean-odyssey-v1'],
  ['samurai-fortune', 'samurai-fortune-v1'],
  ['candy-carnival', 'candy-carnival-v1'],
  ['phantom-manor', 'phantom-manor-v1'],
  ['nordic-legends', 'nordic-legends-v1'],
  ['desert-treasures', 'desert-treasures-v1'],
  ['robot-revolution', 'robot-revolution-v1'],
  ['dragon-hoard', 'dragon-hoard-v1'],
]

const gameRoutes = [
  ...slots.map(([route]) => `/slots/${route}/demo`),
  '/demo/cards/blackjack',
  '/demo/cards/texas-holdem',
  '/demo/cards/solitaire/bot-practice',
  '/games/video-poker',
  '/games/baccarat',
  '/games/casino-war',
  '/cards/hearts',
  '/games/keno',
  '/games/sic-bo',
  '/games/roulette',
  '/games/craps',
  '/games/liars-dice',
  '/games/asteroids',
  '/games/flappy',
  '/games/horse-flight',
  '/games/2048',
  '/games/drop-merge',
  '/games/snake',
]

if (gameRoutes.length !== 38) throw new Error(`Smoke inventory must contain 38 games, found ${gameRoutes.length}.`)

console.log(`Fortune Forge production smoke: ${baseUrl}`)

for (const route of gameRoutes) {
  const response = await request(`${baseUrl}${route}`)
  const html = await response.text()
  assert(response.ok, `${route} returned ${response.status}`)
  assert(html.includes('<div id="root"></div>'), `${route} did not return the application shell`)
}
console.log(`✓ ${gameRoutes.length} game routes returned the application shell`)

for (const [route, gameId] of slots) {
  const status = await request(`${baseUrl}/api/slots/demo/status?gameId=${encodeURIComponent(gameId)}`)
  assert(status.status === 204, `${route} demo status returned ${status.status}`)

  const spin = await request(`${baseUrl}/api/slots/demo/spins`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      gameId,
      wagerPoints: 10,
      useFreeSpin: false,
      freeSpinsRemaining: 0,
      freeSpinWagerPoints: null,
      energyBalance: 0,
      sealCollections: [],
      freeSpinFeatureMode: null,
    }),
  })
  const result = await spin.json().catch(() => null)
  assert(spin.ok, `${route} demo spin returned ${spin.status}: ${JSON.stringify(result)}`)
  assert(result?.gameId === gameId, `${route} demo spin returned the wrong game profile`)
  assert(typeof result?.spinId === 'string' && result.spinId.length > 0, `${route} demo spin did not return a spin ID`)
  console.log(`✓ ${route} (${gameId}) demo contract`)
}

console.log(`✓ production smoke passed: 38 routes and ${slots.length} live demo spins`)

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
