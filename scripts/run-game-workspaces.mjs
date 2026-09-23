import { readFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const task = process.argv[2]
if (!task) {
  console.error('Usage: node scripts/run-game-workspaces.mjs <script>')
  process.exit(2)
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const rootPackage = JSON.parse(readFileSync(resolve(repositoryRoot, 'package.json'), 'utf8'))
const workspaces = rootPackage.workspaces ?? []

for (const workspace of workspaces.filter((path) => path.startsWith('game-packages/'))) {
  const packageJson = JSON.parse(readFileSync(resolve(repositoryRoot, workspace, 'package.json'), 'utf8'))
  if (!packageJson.scripts?.[task]) {
    console.log(`Skipping ${packageJson.name}: no ${task} script`)
    continue
  }

  console.log(`\n> ${packageJson.name} ${task}`)
  const result = spawnSync(
    'npm',
    ['run', task, '--workspace', packageJson.name],
    { cwd: repositoryRoot, stdio: 'inherit', shell: process.platform === 'win32' },
  )

  if (result.error) throw result.error
  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}
