import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { resolve } from 'node:path'
import { build } from 'vite'

const client = fileURLToPath(new URL('../', import.meta.url))
const repository = fileURLToPath(new URL('../../../../../', import.meta.url))
const release = JSON.parse(await readFile(resolve(client, '../../game-release.json'), 'utf8'))
assert.equal(release.frontend.entry, 'src/browser.ts')
assert.equal(release.frontend.manifestExport, undefined)
const normalize = value => value.replaceAll('\\', '/').toLowerCase()
let verified = 0

await build({
  configFile: false, root: client,
  plugins: [{
    name: 'verify-pharaoh-package-ownership',
    generateBundle(_options, bundle) {
      for (const chunk of Object.values(bundle)) {
        if (chunk.type !== 'chunk') continue
        const modules = Object.keys(chunk.modules).map(normalize)
        for (const path of modules) {
          assert.ok(!path.includes('/fortuneforge.client/src/'), `Game imports the host: ${path}`)
          assert.ok(!path.includes('/games/') || path.includes('/games/pharaohsascent/') ||
            path.includes('/games/shared/'), `Game imports another game: ${path}`)
          assert.ok(!/\/slot-game-runtime\/.*(?:view|controller|layout)/i.test(path), `Game imports a generic screen: ${path}`)
        }
        verified += modules.length
      }
    },
  }],
  build: {
    outDir: resolve(repository, 'output/artwork/pharaohs-ascent/standalone'), emptyOutDir: false,
    lib: { entry: resolve(client, 'src/browser.ts'), formats: ['es'], fileName: 'pharaohs-ascent' },
    rollupOptions: { external: ['react', 'react-dom', 'react/jsx-runtime', 'react/jsx-dev-runtime'] },
  },
})
assert.ok(verified > 0)
console.log(`Verified ${verified} browser modules: package-owned game, no generic slots screen or host source.`)
