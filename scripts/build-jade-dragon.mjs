import { spawnSync } from 'node:child_process'
import { cpSync, existsSync, mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../', import.meta.url))
const source = path.join(root, 'games/JadeDragon/client/jade-dragon')
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'

function run(args) {
  const result = spawnSync(npm, args, { cwd: source, stdio: 'inherit', shell: process.platform === 'win32' })
  if (result.status !== 0) process.exit(result.status ?? 1)
}

if (!existsSync(path.join(source, 'node_modules/vite'))) run(['ci', '--ignore-scripts'])
run(['run', 'build'])
const destination = path.join(root, 'fortuneforge.client/public/games/jade-dragon')
mkdirSync(destination, { recursive: true })
cpSync(path.join(source, 'dist'), destination, { recursive: true })
console.log('Jade Dragon presentation built into the host public directory.')
