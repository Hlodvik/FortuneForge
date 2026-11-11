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

/** Source-boundary verification intentionally uses only Node built-ins. */
export function verifyGoldFoundryStandalone(root = repositoryRoot) {
  const errors = []
  const label = path => relative(root, path).replaceAll('\\', '/')
  const gameRoot = resolve(root, 'games/GoldFoundry')
  const engineRoot = resolve(gameRoot, 'src/FortuneForge.Games.GoldFoundry')
  const clientRoot = resolve(gameRoot, 'client/FortuneForge.Games.GoldFoundry.Client')
  const sharedRoot = resolve(root, 'games/shared')
  const engineProject = resolve(engineRoot, 'FortuneForge.Games.GoldFoundry.csproj')
  const clientManifest = resolve(clientRoot, 'package.json')

  for (const required of [engineProject, clientManifest]) {
    if (!existsSync(required)) errors.push(`Missing standalone Gold Foundry package: ${label(required)}`)
  }
  if (!['index.ts', 'index.tsx'].some(entry => existsSync(resolve(clientRoot, 'src', entry))))
    errors.push('Missing standalone Gold Foundry browser entry.')
  if (!existsSync(resolve(clientRoot, 'src/assets/art')) || !existsSync(resolve(clientRoot, 'src/assets/audio')))
    errors.push('Gold Foundry must own its artwork and audio under its client package.')

  for (const project of filesIn(engineRoot).filter(path => path.endsWith('.csproj'))) {
    const source = readFileSync(project, 'utf8')
    for (const [, reference] of source.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
      const dependency = resolve(dirname(project), reference.replaceAll('\\', '/'))
      if (!isInside(gameRoot, dependency) && !isInside(sharedRoot, dependency))
        errors.push(`${label(project)} references a host or another game: ${reference}`)
    }
    for (const [, include] of source.matchAll(/<Compile\s+Include="([^"]+)"/g)) {
      if (!isInside(engineRoot, resolve(dirname(project), include.replaceAll('\\', '/'))))
        errors.push(`${label(project)} compiles source outside its own engine: ${include}`)
    }
  }
  for (const path of filesIn(engineRoot).filter(path => path.endsWith('.cs'))) {
    const source = readFileSync(path, 'utf8')
    for (const [, namespace] of source.matchAll(/\busing\s+(?:static\s+)?(?:\w+\s*=\s*)?(FortuneForge\.[\w.]+)/g)) {
      if (!namespace.startsWith('FortuneForge.Games.GoldFoundry') &&
          !namespace.startsWith('FortuneForge.Games.Slots'))
        errors.push(`${label(path)} imports host or foreign game namespace ${namespace}`)
    }
  }

  for (const path of filesIn(resolve(clientRoot, 'src'))) {
    if (!['.ts', '.tsx', '.js', '.mjs', '.css'].includes(extname(path))) continue
    const source = readFileSync(path, 'utf8')
    if (/['"`]\/assets\/slots\/gold-foundry(?:\/|['"`])/i.test(source))
      errors.push(`${label(path)} loads Gold Foundry media from the host public directory.`)
    const references = [...source.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*|@import\s*)['"]([^'"]+)['"]/g)]
    for (const [, reference] of references) {
      if (reference.startsWith('.') && !isInside(clientRoot, resolve(dirname(path), reference)))
        errors.push(`${label(path)} imports outside its package: ${reference}`)
      if (reference.startsWith('@fortuneforge/games-') && reference !== '@fortuneforge/games-gold-foundry' &&
          !reference.startsWith('@fortuneforge/games-gold-foundry/'))
        errors.push(`${label(path)} imports another game: ${reference}`)
      if (reference.includes('fortuneforge.client') || reference.startsWith('@/'))
        errors.push(`${label(path)} imports the host app: ${reference}`)
    }
  }

  if (existsSync(clientManifest)) {
    const manifest = JSON.parse(readFileSync(clientManifest, 'utf8'))
    if (manifest.name !== '@fortuneforge/games-gold-foundry') errors.push('Gold Foundry client has an unexpected package identity.')
    if (!manifest.scripts?.check || !manifest.scripts?.build) errors.push('Gold Foundry client must provide independent check and build commands.')
    const allowed = new Set(['@fortuneforge/client-presentation', '@fortuneforge/slot-client-core'])
    for (const dependency of Object.keys({ ...manifest.dependencies, ...manifest.optionalDependencies })) {
      if (dependency.startsWith('@fortuneforge/') && !allowed.has(dependency))
        errors.push(`Gold Foundry has a non-neutral Fortune Forge runtime dependency: ${dependency}`)
    }
  }

  const hostRuntime = filesIn(resolve(root, 'fortuneforge.client/src/games/slots/goldFoundry'))
    .map(label).filter(path => !path.endsWith('/catalog.ts') && !path.endsWith('/manifest.ts'))
  hostRuntime.forEach(path => errors.push(`Gold Foundry runtime remains in the host: ${path}`))
  return { errors, engineRoot: label(engineRoot), clientRoot: label(clientRoot) }
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const report = verifyGoldFoundryStandalone()
  if (report.errors.length) { report.errors.forEach(error => console.error(error)); process.exitCode = 1 }
  else console.log('Verified independent Gold Foundry engine/client source boundaries and owned media.')
}
