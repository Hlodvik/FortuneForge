import { readFileSync, realpathSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { embeddedSlotSmokeInventory, gameSmokeRoutes, nonSlotSmokeInventory, slotSmokeInventory } from './game-smoke-inventory.mjs'

const root = fileURLToPath(new URL('../', import.meta.url))
export function verifyGameRouteInventory({ appRoutes, catalog, slotRoutes, runtimeCatalog, slotCatalog, jadeCatalog }) {
  assert(gameSmokeRoutes.length === 45, `expected 45 smoke routes, found ${gameSmokeRoutes.length}`)
  assert(new Set(gameSmokeRoutes).size === gameSmokeRoutes.length, 'smoke routes must be unique')
  assert(slotSmokeInventory.length === 19, `expected 19 playable slot contracts, found ${slotSmokeInventory.length}`)
  assert(embeddedSlotSmokeInventory.length === 1 && embeddedSlotSmokeInventory[0].smokePath === '/slots/jade-dragon',
    'Jade Dragon must retain embedded-client coverage')

  const registeredSlots = [...slotRoutes.matchAll(/playPath:\s*'([^']+)',\s*demoPath:\s*'([^']+)',\s*serverGameIds:\s*\[([^\]]*)\]/g)]
  assert(registeredSlots.length === 19, `expected 19 playable slot route definitions, found ${registeredSlots.length}`)
  assert(new Set(registeredSlots.flatMap(([, playPath, demoPath]) => [playPath, demoPath])).size === 38,
    'playable slot play and demo routes must be unique')
  for (const [route, gameId] of slotSmokeInventory) {
    const demoPath = `/slots/${route}/demo`
    const registered = registeredSlots.find(([, , path]) => path === demoPath)
    assert(registered !== undefined, `${demoPath} is missing from the slot route registry`)
    assert(registered[1] === `/slots/${route}`, `${demoPath} must retain its playable route /slots/${route}`)
    assert([...registered[3].matchAll(/'([^']+)'/g)].some(([, id]) => id === gameId),
      `${demoPath} does not declare server profile ${gameId}`)
  }

  const runtimeGames = runtimeCatalog === undefined ? null : parseRuntimeCatalog(runtimeCatalog)
  if (runtimeGames) {
    const expectedPaths = [
      ...slotSmokeInventory.map(([route]) => `/slots/${route}`),
      ...nonSlotSmokeInventory.map(({ smokePath, catalogPath = smokePath }) => catalogPath),
      ...embeddedSlotSmokeInventory.map(({ smokePath }) => smokePath),
    ]
    assert(new Set(expectedPaths).size === 45, 'runtime catalogue coverage must contain 45 distinct routes')
    assert(runtimeGames.size === 45, `expected 45 runtime catalogue routes, found ${runtimeGames.size}`)
    for (const route of expectedPaths) assert(runtimeGames.has(route), `${route} is missing from the runtime catalogue`)
    for (const [route] of slotSmokeInventory) assert(runtimeGames.get(`/slots/${route}`) === true, `/slots/${route} must be listed by default`)
  }

  for (const { smokePath, catalogPath = smokePath, listed = true } of nonSlotSmokeInventory) {
    assert(appRoutes.includes(`pathname === '${smokePath}'`), `${smokePath} is missing from AppRoutes`)
    assert(appRoutes.includes(`pathname === '${catalogPath}'`), `${catalogPath} is missing from AppRoutes`)
    const appearsInCatalog = catalog.includes(`href: '${catalogPath}'`)
    if (runtimeGames) assert(runtimeGames.get(catalogPath) === listed,
      `${catalogPath} runtime default visibility must be ${listed}`)
    if (listed) assert(appearsInCatalog, `${catalogPath} is missing from the game catalog`)
    else if (appearsInCatalog) {
      assert(runtimeGames?.get(catalogPath) === false,
        `${catalogPath} must remain unlisted in the game catalog or be runtime-default-hidden`)
      verifyRuntimeCatalogueFilter(catalog, catalogPath)
    }
  }

  for (const { smokePath } of embeddedSlotSmokeInventory) {
    assert(appRoutes.includes(`pathname === '${smokePath}') route = <JadeDragonRoute />`),
      `${smokePath} is missing its custom Jade Dragon route in AppRoutes`)
    assert(!registeredSlots.some(([, playPath, demoPath]) => playPath === smokePath || demoPath.startsWith(`${smokePath}/`)),
      `${smokePath} must remain an embedded client rather than a generic slot contract`)
    if (runtimeGames) assert(runtimeGames.get(smokePath) === true, `${smokePath} runtime default visibility must be true`)
    assert(jadeCatalog?.includes(`playHref: '${smokePath}'`), `${smokePath} is missing from the Jade Dragon catalog`)
    const definitions = slotCatalog?.match(/const catalogDefinitions:[\s\S]*?=\s*\[([\s\S]*?)\n\]/)?.[1] ?? ''
    assert(/import\s*\{\s*JADE_DRAGON_CATALOG\s*\}\s*from\s*'\.\/jadeDragon\/catalog'/.test(slotCatalog ?? '')
      && /^\s*JADE_DRAGON_CATALOG,\s*$/m.test(definitions)
      && /if\s*\(catalog\.id === JADE_DRAGON_CATALOG\.id\) return JADE_DRAGON_CATALOG/.test(slotCatalog ?? ''),
      `${smokePath} must remain in the slot catalog through its Jade Dragon entry`)
    assert(/const slotGames:[^\n]*= SLOT_GAME_CATALOG\.map\(catalogGameFromSlot\)/.test(catalog)
      && /href: game\.playHref \?\? undefined/.test(catalog),
      `${smokePath} requires the slot catalog mapping in the game catalog`)
  }
}

function parseRuntimeCatalog(runtimeCatalog) {
  assert(/bool DefaultVisible\s*=\s*true\)/.test(runtimeCatalog), 'runtime catalogue constructor default must remain explicit')
  const rows = [...runtimeCatalog.matchAll(/new\("[^"\r\n]+",\s*"[^"\r\n]+",\s*"[^"\r\n]+",\s*"([^"\r\n]+)"(?:,\s*(true|false))?\)/g)]
  const games = new Map(rows.map(([, route, visible]) => [route, visible !== 'false']))
  assert(games.size === rows.length, 'runtime catalogue routes must be unique')
  return games
}

function verifyRuntimeCatalogueFilter(catalog, route) {
  const section = catalog.match(/function GameSection\([\s\S]*?(?=\nfunction |$)/)?.[0] ?? ''
  const hook = section.indexOf('const visibility = useCatalogueVisibility()')
  const filter = section.search(/const visibleGames\s*=\s*games\.filter\(game\s*=>\s*visibility\.isVisible\(game\.href\)\)/)
  const render = section.indexOf('visibleGames.map(')
  assert(hook >= 0 && filter > hook && render > filter && !/\bgames\.map\(/.test(section),
    `${route} requires the runtime visibility filter before mapping game tiles`)
}

if (process.argv[1] && realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url))) {
  verifyGameRouteInventory({
    appRoutes: source('fortuneforge.client/src/app/AppRoutes.tsx'),
    catalog: source('fortuneforge.client/src/pages/games/OtherGamesPage.tsx'),
    slotRoutes: source('fortuneforge.client/src/games/slots/routeRegistry.ts'),
    runtimeCatalog: source('FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs'),
    slotCatalog: source('fortuneforge.client/src/games/slots/catalog.ts'),
    jadeCatalog: source('fortuneforge.client/src/games/slots/jadeDragon/catalog.ts'),
  })
  console.log(`✓ game inventory verified: ${gameSmokeRoutes.length} routes, ${slotSmokeInventory.length} demo-capable slot packages and ${embeddedSlotSmokeInventory.length} embedded Jade Dragon package (20 slots)`)
}

function source(relativePath) {
  return readFileSync(`${root}${relativePath}`, 'utf8')
}

function assert(condition, message) {
  if (!condition) throw new Error(message)
}
