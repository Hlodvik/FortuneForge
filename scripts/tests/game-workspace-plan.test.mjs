import assert from 'node:assert/strict'
import test from 'node:test'
import { planGameWorkspaceTasks } from '../game-workspace-plan.mjs'

const workspace = (name, path, dependencies = {}, scripts = { build: 'build', check: 'check', test: 'vitest run' }) => ({
  path, packageJson: { name, dependencies, scripts },
})
const shared = workspace('@ff/presentation', 'packages/presentation')
const dragon = workspace('@ff/dragon', 'games/Dragon', { '@ff/presentation': '0.1.0', react: '^19' })
const hilo = workspace('@ff/hilo', 'games/HiLo', { '@ff/presentation': '0.1.0' })
const commands = (packages, task) => planGameWorkspaceTasks(packages, task).map(({ name, script, skip }) => `${name}:${script}${skip ? ':skip' : ''}`)

test('builds a shared dependency once before both games regardless of workspace array order', () => {
  assert.deepEqual(commands([dragon, hilo, shared], 'build'), [
    '@ff/presentation:build', '@ff/dragon:build', '@ff/hilo:build',
  ])
})

for (const task of ['check', 'test']) {
  test(`${task} compiles shared exports before checking or testing any consumer`, () => {
    assert.deepEqual(commands([dragon, hilo, shared], task), [
      '@ff/presentation:build', `@ff/presentation:${task}`, `@ff/dragon:${task}`, `@ff/hilo:${task}`,
    ])
  })
}

test('does not run the host or unrelated shared workspaces as game tasks', () => {
  const host = workspace('host', 'fortuneforge.client', { '@ff/dragon': '0.1.0' })
  assert.deepEqual(commands([host, shared, dragon, workspace('unrelated', 'packages/unrelated')], 'build'), [
    '@ff/presentation:build', '@ff/dragon:build',
  ])
})

test('includes transitive local dev and optional prerequisites but ignores external npm dependencies', () => {
  const base = workspace('@ff/base', 'packages/base')
  const presentation = { ...shared, packageJson: { ...shared.packageJson, devDependencies: { '@ff/base': '0.1.0' } } }
  const game = { ...dragon, packageJson: { ...dragon.packageJson, optionalDependencies: { '@ff/presentation': '0.1.0' } } }
  assert.deepEqual(commands([game, presentation, base], 'build'), ['@ff/base:build', '@ff/presentation:build', '@ff/dragon:build'])
})

test('preserves games without local dependencies and records missing task scripts as skipped', () => {
  assert.deepEqual(commands([workspace('standalone', 'games/Standalone', {}, { build: 'build' })], 'test'), ['standalone:test:skip'])
})

test('rejects dependency cycles instead of executing an incomplete build order', () => {
  const circular = { ...shared, packageJson: { ...shared.packageJson, dependencies: { '@ff/dragon': '0.1.0' } } }
  assert.throws(() => commands([dragon, circular], 'build'), /Workspace dependency cycle: @ff\/dragon -> @ff\/presentation -> @ff\/dragon/)
})

test('rejects duplicate package names before scheduling commands', () => {
  assert.throws(() => commands([dragon, { ...dragon, path: 'games/Other' }], 'check'), /Duplicate workspace package/)
})
