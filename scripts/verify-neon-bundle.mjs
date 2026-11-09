import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const clientRoot = resolve(root, 'fortuneforge.client')
const require = createRequire(resolve(clientRoot, 'package.json'))
const { build } = await import(pathToFileURL(require.resolve('vite')).href)
// The normal Vite config includes the module-loading isolation gate, so this
// command is also the same production build used by the application and CI.
await build({ root: clientRoot, configFile: resolve(clientRoot, 'vite.config.ts') })
