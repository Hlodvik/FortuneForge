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
  return child === '' || !child.startsWith(`..${sep}`) && child !== '..' && !child.includes(':')
}

/** Runs before npm install in CI, so it deliberately uses only Node built-ins. */
export function verifyNeonStandalone(root = repositoryRoot) {
  const errors = []
  const label = path => relative(root, path).replaceAll('\\', '/')
  const gameRoot = resolve(root, 'games/NeonNights')
  const engineRoot = resolve(gameRoot, 'src/FortuneForge.Games.NeonNights')
  const clientRoot = resolve(gameRoot, 'client/FortuneForge.Games.NeonNights.Client')
  const sharedRoot = resolve(root, 'games/shared')
  const engineProject = resolve(engineRoot, 'FortuneForge.Games.NeonNights.csproj')
  const clientManifest = resolve(clientRoot, 'package.json')
  for (const required of [engineProject, clientManifest]) {
    if (!existsSync(required)) errors.push(`Missing standalone Neon package: ${label(required)}`)
  }
  if (!['index.ts', 'index.tsx'].some(entry => existsSync(resolve(clientRoot, 'src', entry)))) {
    errors.push('Missing standalone Neon browser entry.')
  }

  for (const project of filesIn(engineRoot).filter(path => path.endsWith('.csproj'))) {
    const source = readFileSync(project, 'utf8')
    for (const [, reference] of source.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
      const dependency = resolve(dirname(project), reference.replaceAll('\\', '/'))
      if (!isInside(gameRoot, dependency) && !isInside(sharedRoot, dependency)) {
        errors.push(`${label(project)} references a host or another game: ${reference}`)
      }
    }
    for (const [, include] of source.matchAll(/<Compile\s+Include="([^"]+)"/g)) {
      if (!isInside(engineRoot, resolve(dirname(project), include.replaceAll('\\', '/')))) {
        errors.push(`${label(project)} compiles source outside its own engine: ${include}`)
      }
    }
  }
  for (const path of filesIn(engineRoot).filter(path => path.endsWith('.cs'))) {
    const source = readFileSync(path, 'utf8')
    for (const [, namespace] of source.matchAll(/\busing\s+(?:static\s+)?(?:\w+\s*=\s*)?(FortuneForge\.[\w.]+)/g)) {
      if (!namespace.startsWith('FortuneForge.Games.NeonNights') &&
          !namespace.startsWith('FortuneForge.Games.Slots')) {
        errors.push(`${label(path)} imports host or foreign game namespace ${namespace}`)
      }
    }
  }

  const checkClient = (directory, packageName) => {
    for (const path of filesIn(resolve(directory, 'src'))) {
      if (!['.ts', '.tsx', '.js', '.mjs', '.css'].includes(extname(path))) continue
      const source = readFileSync(path, 'utf8')
      if (packageName === '@fortuneforge/slot-client-core' &&
          /\[data-(?:slot-theme|special-round)\s*=/.test(source)) {
        errors.push(`${label(path)} contains a game-specific presentation selector in the neutral core.`)
      }
      const references = [...source.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*|@import\s*)['"]([^'"]+)['"]/g)]
      for (const [, reference] of references) {
        if (reference.startsWith('.') && !isInside(directory, resolve(dirname(path), reference))) {
          errors.push(`${label(path)} imports outside its package: ${reference}`)
        }
        if (reference.startsWith('@fortuneforge/games-') &&
            reference !== packageName && !reference.startsWith(`${packageName}/`)) {
          errors.push(`${label(path)} imports another game: ${reference}`)
        }
        if (reference.includes('fortuneforge.client') || reference.startsWith('@/')) {
          errors.push(`${label(path)} imports the host app: ${reference}`)
        }
      }
    }
  }
  if (existsSync(clientManifest)) {
    const manifest = JSON.parse(readFileSync(clientManifest, 'utf8'))
    if (manifest.name !== '@fortuneforge/games-neon-nights') errors.push('Neon client has an unexpected package identity.')
    if (!manifest.scripts?.check || !manifest.scripts?.build) errors.push('Neon client must provide independent check and build commands.')
    checkClient(clientRoot, manifest.name)
    for (const dependency of Object.keys({ ...manifest.dependencies, ...manifest.peerDependencies })) {
      if (dependency.startsWith('@fortuneforge/games-')) errors.push(`Neon depends on another game package: ${dependency}`)
    }
  }
  const coreRoot = resolve(root, 'packages/slot-client-core')
  if (!existsSync(resolve(coreRoot, 'package.json'))) errors.push('Missing neutral slot-client-core package.')
  else checkClient(coreRoot, '@fortuneforge/slot-client-core')
  return { errors, engineRoot: label(engineRoot), clientRoot: label(clientRoot) }
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const report = verifyNeonStandalone()
  if (report.errors.length) {
    report.errors.forEach(error => console.error(error))
    process.exitCode = 1
  } else console.log(`Verified independent Neon engine/client source boundaries and neutral client core.`)
}
