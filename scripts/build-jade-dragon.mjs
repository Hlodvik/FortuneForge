import { spawnSync } from 'node:child_process'
import { cpSync, mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../', import.meta.url))
const source = path.join(root, 'games/JadeDragon/client/jade-dragon')
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'

function run(args) {
  const result = spawnSync(npm, args, { cwd: source, stdio: 'inherit', shell: process.platform === 'win32' })
  if (result.status !== 0) process.exit(result.status ?? 1)
}

// Workspace installs may hoist Vite. A physical nested node_modules check would
// incorrectly reinstall the entire workspace during a frontend build.
const requireFromGame = createRequire(path.join(source, 'package.json'))
try { requireFromGame.resolve('vite') }
catch { throw new Error('Install repository workspace dependencies with npm ci before building Jade Dragon.') }
run(['run', 'build'])
const destination = path.join(root, 'fortuneforge.client/public/games/jade-dragon')
mkdirSync(destination, { recursive: true })
cpSync(path.join(source, 'dist'), destination, { recursive: true })
console.log('Jade Dragon presentation built into the host public directory.')
