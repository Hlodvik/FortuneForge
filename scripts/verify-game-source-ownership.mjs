import { existsSync, readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = (path) => readFileSync(resolve(root, path), 'utf8')
const fail = (message) => {
  console.error(message)
  process.exitCode = 1
}

const applicationProjects = [
  'FortuneForge.Server/FortuneForge.Server.csproj',
  'FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj',
]
const sourceProjectPaths = new Set()
for (const applicationProject of applicationProjects) {
  const projectContents = read(applicationProject)
  const binaryGameReferences = [...projectContents.matchAll(/<PackageReference Include="(FortuneForge\.Games\.[^"]+)"/g)]
  if (binaryGameReferences.length > 0) {
    fail(`${applicationProject} still consumes external game packages: ${binaryGameReferences.map((match) => match[1]).join(', ')}`)
  }

  const sourceProjectReferences = [...projectContents.matchAll(/<ProjectReference Include="([^"]*FortuneForge\.Games\.[^"]+\.csproj)"/g)]
  for (const [, projectPath] of sourceProjectReferences) {
    const normalizedPath = projectPath.replaceAll('\\', '/')
    const resolvedProject = resolve(root, dirname(applicationProject), normalizedPath)
    sourceProjectPaths.add(resolvedProject)
    if (!existsSync(resolvedProject)) fail(`Missing game source project: ${projectPath}`)
  }
}
if (sourceProjectPaths.size === 0) fail('Application has no game source project references.')

const rootPackage = JSON.parse(read('package.json'))
const clientPackage = JSON.parse(read('fortuneforge.client/package.json'))
const workspacePackages = new Map()
for (const workspace of rootPackage.workspaces ?? []) {
  if (!workspace.startsWith('game-packages/')) continue
  const packagePath = resolve(root, workspace, 'package.json')
  if (!existsSync(packagePath)) {
    fail(`Missing game workspace package: ${workspace}`)
    continue
  }
  const packageJson = JSON.parse(readFileSync(packagePath, 'utf8'))
  workspacePackages.set(packageJson.name, { packageJson, workspace })
}

const clientGameDependencies = Object.entries(clientPackage.dependencies ?? {})
  .filter(([name]) => name.startsWith('@fortuneforge/games-'))

for (const [name, version] of clientGameDependencies) {
  if (String(version).startsWith('file:')) fail(`${name} still points to a copied package artifact.`)
  const workspace = workspacePackages.get(name)
  if (!workspace) {
    fail(`${name} has no source workspace in this repository.`)
    continue
  }
  if (workspace.packageJson.version !== version) {
    fail(`${name} dependency ${version} does not match workspace ${workspace.packageJson.version}.`)
  }
}

for (const oldFeed of ['packages/client', 'packages/games']) {
  if (existsSync(resolve(root, oldFeed))) fail(`Obsolete copied-package feed still exists: ${oldFeed}`)
}

if (process.exitCode) process.exit(process.exitCode)
console.log(`Verified ${sourceProjectPaths.size} server game projects and ${clientGameDependencies.length} client game workspaces are owned by this repository.`)
