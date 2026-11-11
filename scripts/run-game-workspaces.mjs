import { readFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { planGameWorkspaceTasks } from './game-workspace-plan.mjs'

const [task, workspaceName, scope, ...extra] = process.argv.slice(2)
if (!task || extra.length || (scope && (scope !== '--dependencies-only' || task !== 'build' || !workspaceName))) {
  console.error('Usage: node scripts/run-game-workspaces.mjs <script> [workspace] [--dependencies-only]')
  process.exit(2)
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const rootPackage = JSON.parse(readFileSync(resolve(repositoryRoot, 'package.json'), 'utf8'))
const workspaces = (rootPackage.workspaces ?? []).map((path) => ({
  path,
  packageJson: JSON.parse(readFileSync(resolve(repositoryRoot, path, 'package.json'), 'utf8')),
}))
const packagesByName = new Map(workspaces.map(({ packageJson }) => [packageJson.name, packageJson]))

for (const { name, script, skip } of planGameWorkspaceTasks(workspaces, task, workspaceName)) {
  if (scope === '--dependencies-only' && name === workspaceName) continue
  if (skip) {
    console.log(`Skipping ${name}: no ${script} script`)
    continue
  }

  const packageJson = packagesByName.get(name)
  console.log(`\n> ${name} ${script}`)
  const args = ['run', script, '--workspace', name]
  // UI suites include real deal/reveal animations and large accessible boards.
  // Their combined interaction budget needs headroom on a busy release host;
  // retain every assertion and a finite timeout, without automatic retries.
  if (script === 'test' && packageJson.scripts[script].trim() === 'vitest run') {
    args.push('--', '--testTimeout', '15000')
  }
  const result = spawnSync(
    'npm',
    args,
    { cwd: repositoryRoot, stdio: 'inherit', shell: process.platform === 'win32' },
  )

  if (result.error) throw result.error
  if (result.status !== 0) {
    process.exit(result.status ?? 1)
  }
}
