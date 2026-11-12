import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync, realpathSync } from 'node:fs'
import { join } from 'node:path'
import test from 'node:test'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { embeddedSlotSmokeInventory, gameSmokeRoutes, nonSlotSmokeInventory, slotSmokeInventory } from '../game-smoke-inventory.mjs'
import { verifyGameRouteInventory } from '../verify-game-route-inventory.mjs'

const source = relative => readFileSync(new URL(`../../${relative}`, import.meta.url), 'utf8')
const current = {
  appRoutes: source('fortuneforge.client/src/app/AppRoutes.tsx'),
  catalog: source('fortuneforge.client/src/pages/games/OtherGamesPage.tsx'),
  slotRoutes: source('fortuneforge.client/src/games/slots/routeRegistry.ts'),
  runtimeCatalog: source('FortuneForge.Server/Admin/Management/AdminManagementCatalog.cs'),
  slotCatalog: source('fortuneforge.client/src/games/slots/catalog.ts'),
  jadeCatalog: source('fortuneforge.client/src/games/slots/jadeDragon/catalog.ts'),
}

test('all 45 routes cover 19 slot contracts and the embedded Jade Dragon package, with physical and resolved-alias CLI execution', () => {
  assert.doesNotThrow(() => verifyGameRouteInventory(current))
  assert.equal(gameSmokeRoutes.length, 45)
  assert.equal(slotSmokeInventory.length, 19)
  assert.deepEqual(embeddedSlotSmokeInventory, [{ smokePath: '/slots/jade-dragon' }])
  assert.equal(gameSmokeRoutes.filter(route => route === '/slots/jade-dragon').length, 1)
  assert.equal(gameSmokeRoutes.includes('/slots/jade-dragon/demo'), false)
  assert.equal(slotSmokeInventory.some(([route]) => route === 'jade-dragon'), false)

  const root = realpathSync(fileURLToPath(new URL('../../', import.meta.url)))
  const physical = join(root, 'scripts/verify-game-route-inventory.mjs')
  const alias = join(root, 'logical-junction/scripts/verify-game-route-inventory.mjs')
  // Model junction resolution without requiring permission to create Windows links.
  const aliasMain = `
    import fs from 'node:fs'
    import { syncBuiltinESMExports } from 'node:module'
    const realpathSync = fs.realpathSync
    fs.realpathSync = path => path === ${JSON.stringify(alias)} ? ${JSON.stringify(physical)} : realpathSync(path)
    syncBuiltinESMExports()
    process.argv[1] = ${JSON.stringify(alias)}
    await import(${JSON.stringify(pathToFileURL(physical).href)})
  `
  for (const args of [[physical], ['--input-type=module', '-e', aliasMain]]) {
    const cli = spawnSync(process.execPath, args, { encoding: 'utf8' })
    assert.equal(cli.status, 0, cli.stderr)
    assert.match(cli.stdout, /45 routes, 19 slot contracts and 1 embedded Jade Dragon package \(20 slots\)/)
  }
})

test('40 games are listed, including free Solitaire, while five games remain hidden', () => {
  const hidden = nonSlotSmokeInventory.filter(game => game.listed === false).map(game => game.smokePath).sort()
  assert.deepEqual(hidden, ['/games/asteroids', '/games/flappy', '/games/horse-flight', '/games/liars-dice', '/games/snake'])
  assert.equal(nonSlotSmokeInventory.length - hidden.length + slotSmokeInventory.length + embeddedSlotSmokeInventory.length, 40)
  for (const route of ['/cards/free-solitaire', '/games/2048', '/games/drop-merge']) {
    const listedGame = nonSlotSmokeInventory.find(game => game.smokePath === route)
    assert.ok(listedGame, `${route} must remain in the smoke inventory`)
    assert.equal(listedGame.listed ?? true, true)
    assert.throws(() => verifyGameRouteInventory({
      ...current,
      runtimeCatalog: current.runtimeCatalog.replace(`"${route}")`, `"${route}", false)`),
    }), /runtime default visibility must be true/)
    assert.throws(() => verifyGameRouteInventory({
      ...current,
      catalog: current.catalog.split('\n').filter(line => !line.includes(`href: '${route}'`)).join('\n'),
    }), /is missing from the game catalog/)
  }
})

test('legacy static catalogue must omit the five unlisted games entirely', () => {
  const legacy = {
    ...current,
    runtimeCatalog: undefined,
    catalog: current.catalog.split('\n').filter(line => !['/games/horse-flight', '/games/flappy', '/games/asteroids', '/games/liars-dice', '/games/snake'].some(route => line.includes(`href: '${route}'`))).join('\n'),
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

test('rejects incorrect runtime visibility, missing Jade coverage, extra routes and duplicates', () => {
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/games/horse-flight", false)', '"/games/horse-flight", true)'),
  }), /runtime default visibility must be false/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/games/horse-flight"', '"/games/not-a-game"'),
  }), /horse-flight is missing from the runtime catalogue/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/slots/jade-dragon")', '"/slots/jade-dragon", false)'),
  }), /jade-dragon runtime default visibility must be true/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/slots/jade-dragon"', '"/slots/unknown-jade"'),
  }), /jade-dragon is missing from the runtime catalogue/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.split('\n').filter(line => !line.includes('"/slots/jade-dragon"')).join('\n'),
  }), /expected 45 runtime catalogue routes, found 44/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: `${current.runtimeCatalog}\nnew("extra", "Extra", "slots", "/slots/extra")`,
  }), /expected 45 runtime catalogue routes, found 46/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, runtimeCatalog: current.runtimeCatalog.replace('"/slots/jade-dragon"', '"/slots/wukong"'),
  }), /runtime catalogue routes must be unique/)
})

test('rejects missing generic contracts, embedded Jade dispatch and Jade catalog wiring', () => {
  assert.throws(() => verifyGameRouteInventory({
    ...current, appRoutes: current.appRoutes.replaceAll("pathname === '/games/horse-flight'", "pathname === '/games/not-a-game'"),
  }), /horse-flight is missing from AppRoutes/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotRoutes: current.slotRoutes.replaceAll("'wukong-journey-v1'", "'unknown-server-game'"),
  }), /does not declare server profile wukong-journey-v1/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotRoutes: current.slotRoutes.replace("demoPath: '/slots/wukong/demo'", "demoPath: '/slots/unknown/demo'"),
  }), /wukong\/demo is missing from the slot route registry/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotRoutes: current.slotRoutes.replace("playPath: '/slots/wukong'", "playPath: '/slots/unknown'"),
  }), /must retain its playable route/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotRoutes: `${current.slotRoutes}\n{ playPath: '/slots/jade-dragon', demoPath: '/slots/jade-dragon/demo', serverGameIds: ['empty-slots-v1'] }`,
  }), /expected 19 playable slot route definitions, found 20/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, appRoutes: current.appRoutes.replace("pathname === '/slots/jade-dragon') route = <JadeDragonRoute />", "pathname === '/slots/jade-dragon') route = <NotFoundPage />"),
  }), /jade-dragon is missing its custom Jade Dragon route/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, jadeCatalog: current.jadeCatalog.replace("playHref: '/slots/jade-dragon'", "playHref: '/slots/unknown-jade'"),
  }), /jade-dragon is missing from the Jade Dragon catalog/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotCatalog: current.slotCatalog.replace('  JADE_DRAGON_CATALOG,', ''),
  }), /must remain in the slot catalog through its Jade Dragon entry/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, slotCatalog: current.slotCatalog.replace('if (catalog.id === JADE_DRAGON_CATALOG.id) return JADE_DRAGON_CATALOG', ''),
  }), /must remain in the slot catalog through its Jade Dragon entry/)
  assert.throws(() => verifyGameRouteInventory({
    ...current, catalog: current.catalog.replace('SLOT_GAME_CATALOG.map(catalogGameFromSlot)', '[]'),
  }), /requires the slot catalog mapping in the game catalog/)
})
