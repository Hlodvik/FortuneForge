import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { gameSmokeRoutes, nonSlotSmokeInventory, slotSmokeInventory } from './game-smoke-inventory.mjs'

const root = fileURLToPath(new URL('../', import.meta.url))
const appRoutes = source('fortuneforge.client/src/app/AppRoutes.tsx')
const catalog = source('fortuneforge.client/src/pages/games/OtherGamesPage.tsx')
const slotRoutes = source('fortuneforge.client/src/games/slots/routeRegistry.ts')

assert(gameSmokeRoutes.length === 38, `expected 38 smoke routes, found ${gameSmokeRoutes.length}`)
assert(new Set(gameSmokeRoutes).size === gameSmokeRoutes.length, 'smoke routes must be unique')
assert(slotSmokeInventory.length === 20, `expected 20 slot contracts, found ${slotSmokeInventory.length}`)

for (const [route, gameId] of slotSmokeInventory) {
  const demoPath = `/slots/${route}/demo`
  const routeBlock = slotRoutes.match(new RegExp(`demoPath: '${escapeRegExp(demoPath)}'[\\s\\S]*?serverGameIds: \\[([^\\]]+)\\]`))
  assert(routeBlock !== null, `${demoPath} is missing from the slot route registry`)
  assert(routeBlock[1].includes(`'${gameId}'`), `${demoPath} does not declare server profile ${gameId}`)
}

for (const { smokePath, catalogPath = smokePath } of nonSlotSmokeInventory) {
  assert(appRoutes.includes(`pathname === '${smokePath}'`), `${smokePath} is missing from AppRoutes`)
  assert(appRoutes.includes(`pathname === '${catalogPath}'`), `${catalogPath} is missing from AppRoutes`)
  assert(catalog.includes(`href: '${catalogPath}'`), `${catalogPath} is missing from the game catalog`)
}

console.log(`✓ game inventory verified: ${gameSmokeRoutes.length} routes and ${slotSmokeInventory.length} slot contracts`)

function source(relativePath) {
  return readFileSync(`${root}${relativePath}`, 'utf8')
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function assert(condition, message) {
  if (!condition) throw new Error(message)
}
