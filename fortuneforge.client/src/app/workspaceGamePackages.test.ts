import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

type ClientPackage = Readonly<{
  dependencies?: Readonly<Record<string, string>>
}>

type RootPackage = Readonly<{
  workspaces?: readonly string[]
}>

type WorkspacePackage = Readonly<{
  name?: string
  version?: string
  scripts?: Readonly<Record<string, string>>
}>

const clientPackageUrl = new URL('../../package.json', import.meta.url)
const rootPackageUrl = new URL('../../../package.json', import.meta.url)
const clientPackage = JSON.parse(readFileSync(clientPackageUrl, 'utf8')) as ClientPackage
const rootPackage = JSON.parse(readFileSync(rootPackageUrl, 'utf8')) as RootPackage

describe('workspace game packages', () => {
  it('resolves every game client dependency to source owned by this repository', () => {
    const gameDependencies = Object.entries(clientPackage.dependencies ?? {})
      .filter(([name]) => name.startsWith('@fortuneforge/games-'))

    expect(gameDependencies.length).toBeGreaterThan(0)

    const workspaces = new Map<string, WorkspacePackage>()
    for (const workspacePath of rootPackage.workspaces ?? []) {
      if (!workspacePath.startsWith('game-packages/')) continue

      const packageJsonUrl = new URL(`${workspacePath}/package.json`, rootPackageUrl)
      expect(existsSync(fileURLToPath(packageJsonUrl)), `${workspacePath} is missing`).toBe(true)
      const workspacePackage = JSON.parse(readFileSync(packageJsonUrl, 'utf8')) as WorkspacePackage
      expect(workspacePackage.name, `${workspacePath} has no package name`).toBeTruthy()
      workspaces.set(workspacePackage.name!, workspacePackage)
    }

    for (const [name, version] of gameDependencies) {
      const workspacePackage = workspaces.get(name)
      expect(workspacePackage, `${name} has no source workspace`).toBeDefined()
      expect(workspacePackage?.version, `${name} version drifted from the app dependency`).toBe(version)
      expect(workspacePackage?.scripts?.build, `${name} has no reproducible build`).toBeTruthy()
      expect(workspacePackage?.scripts?.check, `${name} has no type check`).toBeTruthy()
    }
  })
})
