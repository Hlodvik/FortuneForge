import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const clientRoot = resolve(repositoryRoot, 'fortuneforge.client')
const unavailablePackage = '@fortuneforge/games-wukong/manifest'
const wukongWorkspaceLink = resolve(repositoryRoot, 'node_modules/@fortuneforge/games-wukong')

test('Vite keeps a healthy slot route available when a sibling package cannot resolve', async (context) => {
  let blockedResolutions = 0
  const server = await createServer({
    root: clientRoot,
    logLevel: 'silent',
    server: { middlewareMode: true },
    plugins: [{
      name: 'simulate-missing-slot-sibling',
      enforce: 'pre',
      resolveId(source) {
        if (source !== unavailablePackage) return null
        blockedResolutions += 1
        throw new Error(`Simulated unavailable package: ${source}`)
      },
    }],
  })
  context.after(() => server.close())

  const registry = await server.ssrLoadModule('/src/games/slots/routeRegistry.ts')

  const neon = await registry.loadSlotGameByRoute('/slots/neon-nights/demo')
  assert.equal(neon.id, 'neon-nights')
  assert.equal(blockedResolutions, 0, 'loading Neon must not resolve Wukong')

  await assert.rejects(
    registry.loadSlotGameByRoute('/slots/wukong/demo'),
    /Simulated unavailable package: @fortuneforge\/games-wukong\/manifest/,
  )
  assert.equal(blockedResolutions, 1)

  const neonAfterSiblingFailure = await registry.loadSlotGameByRoute('/slots/neon-nights/demo')
  assert.equal(neonAfterSiblingFailure.id, 'neon-nights')
  assert.equal(blockedResolutions, 1, 'the failed sibling must not poison a healthy route')
})

test('Vite opens Neon with the Wukong workspace link actually absent', {
  skip: existsSync(wukongWorkspaceLink) && 'the Wukong workspace link is installed',
}, async (context) => {
  const server = await createServer({
    root: clientRoot,
    logLevel: 'silent',
    server: { middlewareMode: true },
  })
  context.after(() => server.close())

  const registry = await server.ssrLoadModule('/src/games/slots/routeRegistry.ts')
  const neon = await registry.loadSlotGameByRoute('/slots/neon-nights/demo')
  assert.equal(neon.id, 'neon-nights')

  await assert.rejects(
    registry.loadSlotGameByRoute('/slots/wukong/demo'),
    /(?:Failed to load url|Cannot find (?:module|package)|Failed to resolve import).*games-wukong/s,
  )

  const neonAfterSiblingFailure = await registry.loadSlotGameByRoute('/slots/neon-nights/demo')
  assert.equal(neonAfterSiblingFailure.id, 'neon-nights')
})
