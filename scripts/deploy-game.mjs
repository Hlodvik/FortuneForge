import { spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdir, open, readFile, readdir, realpath, rename, stat, unlink, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { basename, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { gameHostRuntime } from './game-release-runtime.mjs'

const repository = fileURLToPath(new URL('../', import.meta.url))
const readJson = async path => JSON.parse(await readFile(path, 'utf8'))

async function readOptionalJson(path) {
  try {
    return await readJson(path)
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  }
}

async function resolveWithin(directory, path) {
  if (typeof path !== 'string' || !path || isAbsolute(path)) throw new Error('Expected a relative release path')
  const resolved = await realpath(resolve(directory, path))
  const child = relative(directory, resolved)
  if (!child || isAbsolute(child) || child === '..' || child.startsWith('..' + sep)) {
    throw new Error('Release path must be inside its declared game or frontend directory: ' + path)
  }
  return resolved
}

function run(executable, args) {
  const result = spawnSync(executable, args, { cwd: repository, stdio: 'inherit' })
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(executable + ' failed while preparing the game release')
}

async function findGame(gameId) {
  const root = join(repository, 'games')
  const directories = (await readdir(root, { withFileTypes: true })).filter(item => item.isDirectory())
  const matches = (await Promise.all(directories.map(async item => {
    const directory = join(root, item.name)
    return { directory, config: await readOptionalJson(join(directory, 'game-release.json')) }
  }))).filter(item => item.config?.id === gameId)
  if (matches.length !== 1) throw new Error('Expected exactly one game release configuration for ' + gameId)
  const [{ directory, config }] = matches
  return { directory: await realpath(directory), config }
}

async function buildBackend(project, directory, gameId, releaseId, gamePackage) {
  if (!project.endsWith('.csproj')) throw new Error('Backend release requires a .NET executable project')
  const assembly = basename(project, '.csproj')
  const entry = assembly + '.dll'
  await mkdir(directory, { recursive: true })
  const args = ['publish', project, '--configuration', 'Release', '--no-self-contained', '--output', directory]
  if (gamePackage) args.push('-p:GamePackage=' + gamePackage)
  run('dotnet', args)
  for (const file of [entry, assembly + '.runtimeconfig.json', assembly + '.deps.json']) {
    if (!(await stat(join(directory, file))).isFile()) throw new Error('Missing backend artifact: ' + file)
  }
  const descriptor = JSON.stringify({ gameId, releaseId, entry }, null, 2) + '\n'
  await writeFile(join(directory, 'release.json'), descriptor, { flag: 'wx' })
}

async function buildFrontend(frontend, entry, directory, manifestExport) {
  const requireFromWorkspace = createRequire(join(frontend, 'package.json'))
  const { build, normalizePath } = await import(pathToFileURL(requireFromWorkspace.resolve('vite')).href)
  const manifestEntry = 'virtual:fortuneforge-slot-release'
  const resolvedManifestEntry = '\0' + manifestEntry
  const plugins = []
  if (manifestExport !== undefined) {
    if (typeof manifestExport !== 'string' || !/^[A-Z][A-Z0-9_]*$/.test(manifestExport)) {
      throw new Error('Invalid slot manifest export: ' + manifestExport)
    }
    plugins.push({
      name: 'fortuneforge-slot-release-entry',
      resolveId(id) { if (id === manifestEntry) return resolvedManifestEntry },
      load(id) {
        if (id !== resolvedManifestEntry) return
        return [
          'import { ' + manifestExport + ' as manifest } from ' + JSON.stringify(normalizePath(entry)),
          'export default function SlotRelease({ renderSlot }) { return renderSlot(manifest) }',
        ].join('\n')
      },
    })
  }
  const bundle = await build({
    plugins,
    root: frontend,
    configFile: false,
    envFile: false,
    publicDir: false,
    base: './',
    build: {
      outDir: directory,
      emptyOutDir: false,
      assetsInlineLimit: 0,
      cssCodeSplit: false,
      modulePreload: false,
      rolldownOptions: {
        input: plugins.length ? manifestEntry : entry,
        external: Object.keys(gameHostRuntime),
        preserveEntrySignatures: 'strict',
        output: { paths: gameHostRuntime },
      },
    },
  })
  const output = Array.isArray(bundle) ? bundle.flatMap(result => result.output) : bundle.output
  const entryChunk = output.find(item => item.type === 'chunk' && item.isEntry && item.exports.includes('default'))
  if (!entryChunk) throw new Error('The browser entry must export a default component')
  const styles = output.filter(item => item.type === 'asset' && item.fileName.endsWith('.css')).map(item => item.fileName)
  return { entry: entryChunk.fileName, styles }
}

async function activateRelease(gameDirectory, backendRoot, assetsOnly, release) {
  const active = join(gameDirectory, 'active.json')
  const lockPath = join(backendRoot, 'active.lock')
  const lock = await open(lockPath, 'wx')
  const pending = join(gameDirectory, '.' + release.releaseId + '.json')
  try {
    let backendReleaseId = release.releaseId
    if (assetsOnly) {
      backendReleaseId = (await readOptionalJson(active))?.backendReleaseId
      if (typeof backendReleaseId !== 'string' || !/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/.test(backendReleaseId)) {
        throw new Error('Publish a combined game release before using --assets-only')
      }
      const backend = await readJson(join(backendRoot, backendReleaseId, 'release.json'))
      if (backend.gameId !== release.gameId || backend.releaseId !== backendReleaseId) {
        throw new Error('The active backend release does not match this game')
      }
    }
    const descriptor = JSON.stringify({ ...release, backendReleaseId }, null, 2) + '\n'
    await writeFile(join(gameDirectory, 'releases', release.releaseId, 'release.json'), descriptor, { flag: 'wx' })
    await writeFile(pending, descriptor, { flag: 'wx' })
    await rename(pending, active)
  } finally {
    try {
      await unlink(pending).catch(error => { if (error.code !== 'ENOENT') throw error })
    } finally {
      await lock.close()
      await unlink(lockPath)
    }
  }
}

async function publishGame(gameId, assetsOnly) {
  const game = await findGame(gameId)
  const frontend = await resolveWithin(game.directory, game.config.frontend?.directory)
  const entry = await resolveWithin(frontend, game.config.frontend?.entry)
  const gamePackage = game.config.backend?.kind === 'classic-slot' ? basename(game.directory) : null
  if (gamePackage && !/^[A-Z][A-Za-z0-9]*$/.test(gamePackage)) throw new Error('Invalid classic slot package directory')
  const project = assetsOnly ? null : gamePackage
    ? join(repository, 'games', 'shared', 'FortuneForge.Games.ClassicSlot.Server', 'FortuneForge.Games.ClassicSlot.Server.csproj')
    : await resolveWithin(game.directory, game.config.backend?.project)
  const releaseId = randomUUID()
  const gameDirectory = join(repository, 'artifacts', 'game-releases', 'games', gameId)
  const releaseDirectory = join(gameDirectory, 'releases', releaseId)
  const backendRoot = join(repository, 'artifacts', 'game-engine-releases', 'games', gameId, 'releases')
  if (assetsOnly && !(await readOptionalJson(join(gameDirectory, 'active.json')))?.backendReleaseId) {
    throw new Error('Publish a combined game release before using --assets-only')
  }

  const { workspaces } = await readJson(join(repository, 'package.json'))
  if (workspaces.some(path => resolve(repository, path) === frontend)) {
    const { name } = await readJson(join(frontend, 'package.json'))
    run(process.execPath, [
      fileURLToPath(new URL('./run-game-workspaces.mjs', import.meta.url)),
      'build', name, '--dependencies-only',
    ])
  }
  await mkdir(releaseDirectory, { recursive: true })
  const browser = await buildFrontend(frontend, entry, releaseDirectory, game.config.frontend.manifestExport)
  if (project) await buildBackend(project, join(backendRoot, releaseId), gameId, releaseId, gamePackage)

  await activateRelease(gameDirectory, backendRoot, assetsOnly, { gameId, releaseId, contractVersion: 1, ...browser })
  console.log('Activated local ' + gameId + ' release ' + releaseId)
}

const [gameId, option, ...extra] = process.argv.slice(2)
if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(gameId ?? '') || (option && option !== '--assets-only') || extra.length) {
  throw new Error('Usage: npm run deploy:game -- <game-id> [--assets-only]')
}
await publishGame(gameId, option === '--assets-only')
