import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { gameSmokeRoutes, nonSlotSmokeInventory, slotSmokeInventory } from './game-smoke-inventory.mjs'

const root = fileURLToPath(new URL('../', import.meta.url))
export function verifyGameRouteInventory({ appRoutes, catalog, slotRoutes, runtimeCatalog }) {

assert(gameSmokeRoutes.length === 40, `expected 40 smoke routes, found ${gameSmokeRoutes.length}`)
assert(new Set(gameSmokeRoutes).size === gameSmokeRoutes.length, 'smoke routes must be unique')
assert(slotSmokeInventory.length === 20, `expected 20 slot contracts, found ${slotSmokeInventory.length}`)

for (const [route, gameId] of slotSmokeInventory) {
  const demoPath = `/slots/${route}/demo`
  const routeBlock = slotRoutes.match(new RegExp(`demoPath: '${escapeRegExp(demoPath)}'[\\s\\S]*?serverGameIds: \\[([^\\]]+)\\]`))
  assert(routeBlock !== null, `${demoPath} is missing from the slot route registry`)
  assert(routeBlock[1].includes(`'${gameId}'`), `${demoPath} does not declare server profile ${gameId}`)
}

const runtimeGames = runtimeCatalog === undefined ? null : parseRuntimeCatalog(runtimeCatalog)
if (runtimeGames) {
  const expectedPaths = [
    ...slotSmokeInventory.map(([route]) => `/slots/${route}`),
    ...nonSlotSmokeInventory.map(({ smokePath, catalogPath = smokePath }) => catalogPath),
  ]
  assert(runtimeGames.size === 40, `expected 40 runtime catalogue routes, found ${runtimeGames.size}`)
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

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  verifyGameRouteInventory({
    appRoutes: source('fortuneforge.client/src/app/AppRoutes.tsx'),
    catalog: source('fortuneforge.client/src/pages/games/OtherGamesPage.tsx'),
    slotRoutes: source('fortuneforge.client/src/games/slots/routeRegistry.ts'),
    runtimeCatalog: source('FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs'),
  })
  console.log(`✓ game inventory verified: ${gameSmokeRoutes.length} routes and ${slotSmokeInventory.length} slot contracts`)
}

function source(relativePath) {
  return readFileSync(`${root}${relativePath}`, 'utf8')
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function assert(condition, message) {
  if (!condition) throw new Error(message)
}
