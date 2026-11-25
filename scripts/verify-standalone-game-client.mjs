import assert from 'node:assert/strict'
import { resolve } from 'node:path'
import { readFile } from 'node:fs/promises'
import { build } from 'vite'

/** Import-graph evidence for a real game entry; does not certify paid server execution. */
export async function verifyStandaloneGameClient(client, repository, folder) {
  const game = resolve(repository, 'games', folder)
  const release = JSON.parse(await readFile(resolve(game, 'game-release.json'), 'utf8'))
  assert.equal(release.frontend.manifestExport, undefined)
  const normalized = path => path.replaceAll('\\', '/').toLowerCase()
  const allowed = normalized(resolve(client)) + '/', shared = normalized(resolve(repository, 'games/shared')) + '/'
  let count = 0
  await build({ configFile: false, root: client,
    plugins: [{ name: 'verify-owned-game-entry', generateBundle(_options, bundle) {
      for (const output of Object.values(bundle)) if (output.type === 'chunk') {
        assert.ok(!output.isEntry || /export\s*\{[^}]*\bdefault\b/s.test(output.code), 'Game entry must export its playable component')
        for (const module of Object.keys(output.modules).map(normalized)) {
          assert.ok(!module.includes('/fortuneforge.client/src/'), `Imports host implementation: ${module}`)
          assert.ok(!module.includes('/games/') || module.startsWith(allowed) || module.startsWith(shared), `Imports outside its game client: ${module}`)
          assert.ok(!/\/slot-game-runtime\/.*(?:view|controller|layout)/i.test(module), `Imports a retired generic game implementation: ${module}`)
          count++
        }
      }
    } }],
    build: { outDir: resolve(repository, 'artifacts/client-verification', release.id), emptyOutDir: false,
      lib: { entry: resolve(client, release.frontend.entry), formats: ['es'], fileName: release.id },
      rollupOptions: { external: ['react', 'react-dom', 'react/jsx-runtime', 'react/jsx-dev-runtime'] } },
  })
  assert.ok(count > 0)
  console.log(`Verified ${folder}: ${count} browser modules, package-owned playable entry, no host or foreign game source.`)
}
