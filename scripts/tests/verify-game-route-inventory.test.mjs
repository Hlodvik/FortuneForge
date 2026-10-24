import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { verifyGameRouteInventory } from '../verify-game-route-inventory.mjs'

const source = relative => readFileSync(new URL(`../../${relative}`, import.meta.url), 'utf8')
const current = {
  appRoutes: source('fortuneforge.client/src/app/AppRoutes.tsx'),
  catalog: source('fortuneforge.client/src/pages/games/OtherGamesPage.tsx'),
  slotRoutes: source('fortuneforge.client/src/games/slots/routeRegistry.ts'),
  runtimeCatalog: source('FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs'),
}

test('all 43 routes and 20 slot contracts allow a filtered, runtime-default-hidden Horse Flight', () => {
  assert.doesNotThrow(() => verifyGameRouteInventory(current))
})

test('legacy static catalogue must omit the unlisted game entirely', () => {
  const legacy = {
    ...current,
    runtimeCatalog: undefined,
    catalog: current.catalog.split('\n').filter(line => !['/games/horse-flight', '/games/flappy', '/games/asteroids', '/games/drop-merge', '/games/2048', '/games/snake'].some(route => line.includes(`href: '${route}'`))).join('\n'),
  }
  assert.doesNotThrow(() => verifyGameRouteInventory(legacy))
  assert.throws(() => verifyGameRouteInventory({ ...current, runtimeCatalog: undefined }), /must remain unlisted/)
})

test('rejects an unfiltered Horse Flight entry even if the runtime default is hidden', () => {
  assert.throws(() => verifyGameRouteInventory({
    ...current, catalog: current.catalog.replace('visibleGames.map(', 'games.map('),
  }), /visibility filter before mapping/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, catalog: current.catalog.replace('games.filter(game => visibility.isVisible(game.href))', 'games'),
  }), /visibility filter before mapping/)
})

test('rejects a visible-by-default Horse Flight or a missing runtime route', () => {
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/games/horse-flight", false)', '"/games/horse-flight", true)'),
  }), /runtime default visibility must be false/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/games/horse-flight"', '"/games/not-a-game"'),
  }), /horse-flight is missing from the runtime catalogue/)
})

test('still rejects missing playable routes and slot server contracts', () => {
  assert.throws(() => verifyGameRouteInventory({
    ...current, appRoutes: current.appRoutes.replaceAll("pathname === '/games/horse-flight'", "pathname === '/games/not-a-game'"),
  }), /horse-flight is missing from AppRoutes/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotRoutes: current.slotRoutes.replaceAll("'wukong-journey-v1'", "'unknown-server-game'"),
  }), /does not declare server profile wukong-journey-v1/)
})
