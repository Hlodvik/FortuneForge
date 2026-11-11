import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const clientRoot = resolve(root, 'fortuneforge.client')
const require = createRequire(resolve(clientRoot, 'package.json'))
const { build } = await import(pathToFileURL(require.resolve('vite')).href)

// The application Vite config owns the emitted-graph boundary check. This is
// intentionally the real production bundle, not a synthetic package graph.
await build({ root: clientRoot, configFile: resolve(clientRoot, 'vite.config.ts') })
