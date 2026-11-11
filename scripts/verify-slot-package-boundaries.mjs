import { existsSync, readFileSync, readdirSync, realpathSync } from 'node:fs'
import { dirname, extname, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const ignored = new Set(['node_modules', 'dist', 'bin', 'obj', '.git'])

function filesIn(directory) {
  if (!existsSync(directory)) return []
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (ignored.has(entry.name)) return []
    const path = resolve(directory, entry.name)
    return entry.isDirectory() ? filesIn(path) : [path]
  })
}

function isInside(directory, path) {
  const child = relative(directory, path)
  return child === '' || child !== '..' && !child.startsWith(`..${sep}`) && !child.includes(':')
}

function sourcePackageName(folder) {
  return `FortuneForge.Games.${folder}`
}

export function verifySlotPackageBoundaries(root = repositoryRoot) {
  const inventoryPath = resolve(root, 'games/catalog/slot-packages.json')
  if (!existsSync(inventoryPath)) return { errors: ['Missing games/catalog/slot-packages.json.'], games: 0 }
  const inventory = JSON.parse(readFileSync(inventoryPath, 'utf8'))
  const errors = []
  const sharedRoot = resolve(root, 'games/shared')
  let packagedClients = 0
  let hostRoutes = 0

  for (const game of inventory.games ?? []) {
    const gameRoot = resolve(root, 'games', game.folder)
    const sourceName = sourcePackageName(game.folder)
    const backendRoot = resolve(gameRoot, 'src', sourceName)
    const backendProject = resolve(backendRoot, `${sourceName}.csproj`)
    const clientRoot = game.clientRoot
      ? resolve(root, game.clientRoot)
      : resolve(gameRoot, 'client', `${sourceName}.Client`)
    const clientManifest = resolve(clientRoot, 'package.json')
    const hasClientPackage = typeof game.clientPackage === 'string'

    if (!existsSync(backendProject)) errors.push(`${game.folder}: missing backend project.`)
    if (hasClientPackage) {
      packagedClients += 1
      if (!existsSync(clientManifest)) errors.push(`${game.folder}: missing browser package.`)
    } else {
      errors.push(`${game.folder}: slot inventory needs a browser package.`)
    }
    if (game.clientKind === 'embedded-package') {
      hostRoutes += 1
      if (!game.hostEntry || !existsSync(resolve(root, game.hostEntry)))
        errors.push(`${game.folder}: missing host-rendered slot entry.`)
    }

    if (existsSync(backendProject)) {
      const project = readFileSync(backendProject, 'utf8')
      for (const [, reference] of project.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
        const target = resolve(dirname(backendProject), reference.replaceAll('\\', '/'))
        if (!isInside(gameRoot, target) && !isInside(sharedRoot, target))
          errors.push(`${game.folder}: backend references host or another game: ${reference}`)
      }
      for (const [, include] of project.matchAll(/<Compile\s+Include="([^"]+)"/g)) {
        if (!isInside(backendRoot, resolve(dirname(backendProject), include.replaceAll('\\', '/'))))
          errors.push(`${game.folder}: backend compiles source outside its package: ${include}`)
      }
    }

    for (const path of filesIn(backendRoot).filter(path => path.endsWith('.cs'))) {
      const source = readFileSync(path, 'utf8')
      if (/\bFortuneForge\.Server\b/.test(source))
        errors.push(`${game.folder}: backend imports the Fortune Forge host: ${relative(root, path)}`)
      for (const [, namespace] of source.matchAll(/\busing\s+(?:static\s+)?(?:\w+\s*=\s*)?(FortuneForge\.Games\.[\w.]+)/g)) {
        if (!namespace.startsWith(sourceName) && !namespace.startsWith('FortuneForge.Games.Slots'))
          errors.push(`${game.folder}: backend imports foreign game namespace ${namespace}.`)
      }
    }

    for (const path of hasClientPackage ? filesIn(resolve(clientRoot, 'src')) : []) {
      if (!['.ts', '.tsx', '.js', '.mjs', '.css'].includes(extname(path))) continue
      const source = readFileSync(path, 'utf8')
      const refs = [...source.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*|@import\s*)['"]([^'"]+)['"]/g)]
      for (const [, reference] of refs) {
        if (reference.startsWith('.') && !isInside(clientRoot, resolve(dirname(path), reference)))
          errors.push(`${game.folder}: client import escapes its package: ${reference}`)
        if (reference.startsWith('@fortuneforge/games-') && reference !== game.clientPackage &&
            !reference.startsWith(`${game.clientPackage}/`))
          errors.push(`${game.folder}: client imports another game: ${reference}`)
        if (reference.includes('fortuneforge.client') || reference.startsWith('@/'))
          errors.push(`${game.folder}: client imports the host app: ${reference}`)
      }
    }

    if (hasClientPackage && existsSync(clientManifest)) {
      const manifest = JSON.parse(readFileSync(clientManifest, 'utf8'))
      if (manifest.name !== game.clientPackage)
        errors.push(`${game.folder}: expected client package ${game.clientPackage}, found ${manifest.name}.`)
      if (!manifest.scripts?.check || !manifest.scripts?.build)
        errors.push(`${game.folder}: client package needs independent check and build scripts.`)
      for (const dependency of Object.keys({ ...manifest.dependencies, ...manifest.optionalDependencies })) {
        if (dependency.startsWith('@fortuneforge/games-') && dependency !== game.clientPackage)
          errors.push(`${game.folder}: client depends on foreign game ${dependency}.`)
      }
    }
  }

  return { errors, games: inventory.games?.length ?? 0, packagedClients, hostRoutes }
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const report = verifySlotPackageBoundaries()
  if (report.errors.length) {
    report.errors.forEach(error => console.error(error))
    process.exitCode = 1
  } else {
    console.log(`Verified ${report.games} standalone slot backend/client boundaries (${report.packagedClients} packaged clients; ${report.hostRoutes} embedded host bridge).`)
  }
}
