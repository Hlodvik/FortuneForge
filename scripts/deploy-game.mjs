import { spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdir, readFile, readdir, realpath, stat, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { basename, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { parseArgs } from 'node:util'
import { activateRelease, isGameId, isReleaseId, readActiveBackendReleaseId, stageRelease } from './game-release-activation.mjs'
import { gameHostRuntime } from './game-release-runtime.mjs'

const repository = fileURLToPath(new URL('../', import.meta.url))
const readJson = async path => JSON.parse(await readFile(path, 'utf8'))
const sharedBackendProjects = new Map([
  ['classic-slot', 'FortuneForge.Games.ClassicSlot.Server'],
  ['packaged-game', 'FortuneForge.Games.PackagedGame.Server'],
  ['game-compute', 'FortuneForge.Games.GameCompute.Server'],
])

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

async function buildFrontend(frontend, entry, directory, config) {
  const { kind = 'module', manifestExport } = config
  const isFrame = kind === 'frame'
  if (kind !== 'module' && !isFrame) throw new Error('Unknown browser release kind: ' + kind)
  if (isFrame && (manifestExport !== undefined || basename(entry) !== 'index.html')) {
    throw new Error('A frame release requires index.html as its browser entry')
  }
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
    envDir: false,
    publicDir: isFrame ? 'public' : false,
    base: './',
    build: {
      outDir: directory,
      emptyOutDir: false,
      assetsInlineLimit: 0,
      cssCodeSplit: false,
      modulePreload: false,
      rolldownOptions: {
        input: plugins.length ? manifestEntry : entry,
        ...(isFrame ? {} : {
          external: Object.keys(gameHostRuntime),
          preserveEntrySignatures: 'strict',
          output: { paths: gameHostRuntime },
        }),
      },
    },
  })
  const output = Array.isArray(bundle) ? bundle.flatMap(result => result.output) : bundle.output
  if (isFrame) {
    if (!output.some(item => item.type === 'asset' && item.fileName === 'index.html')) {
      throw new Error('Frame build did not emit index.html')
    }
    return { kind, entry: 'index.html', styles: [] }
  }
  const entryChunk = output.find(item => item.type === 'chunk' && item.isEntry && item.exports.includes('default'))
  if (!entryChunk) throw new Error('The browser entry must export a default component')
  const styles = output.filter(item => item.type === 'asset' && item.fileName.endsWith('.css')).map(item => item.fileName)
  return { entry: entryChunk.fileName, styles }
}

async function publishGame({ gameId, gameDirectory, backendRoot }, assetsOnly, stageOnly) {
  const game = await findGame(gameId)
  const frontend = await resolveWithin(game.directory, game.config.frontend?.directory)
  const entry = await resolveWithin(frontend, game.config.frontend?.entry)
  const backendKind = game.config.backend?.kind
  const sharedBackend = sharedBackendProjects.get(backendKind)
  if (backendKind !== undefined && !sharedBackend) throw new Error('Unknown backend release kind: ' + backendKind)
  const gamePackage = sharedBackend ? basename(game.directory) : null
  if (gamePackage && !/^[A-Z][A-Za-z0-9]*$/.test(gamePackage)) throw new Error('Invalid game package directory')
  const project = assetsOnly ? null : sharedBackend
    ? join(repository, 'games', 'shared', sharedBackend, sharedBackend + '.csproj')
    : await resolveWithin(game.directory, game.config.backend?.project)
  const releaseId = randomUUID()
  const releaseDirectory = join(gameDirectory, 'releases', releaseId)
  const backendReleaseId = assetsOnly
    ? await readActiveBackendReleaseId({ gameDirectory, backendRoot, gameId }) : releaseId

  const navbarPolicy = game.config.frontend.kind === 'frame'
    ? null : await import('./verify-game-navbar.mjs')
  if (navbarPolicy) {
    const { findings } = navbarPolicy.inspectRepository(repository, { releaseId: gameId })
    if (findings.length > 0) {
      const diagnostics = findings.map(({ file, line, code, message }) =>
        `${file}:${line} [${code}] ${message}`,
      )
      throw new Error(`Game release ${gameId} failed the navbar policy:\n${diagnostics.join('\n')}`)
    }
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
  const browser = await buildFrontend(frontend, entry, releaseDirectory, game.config.frontend)
  if (navbarPolicy) {
    for (const style of browser.styles) {
      const file = join(releaseDirectory, style)
      navbarPolicy.assertReleasedGameStyles(await readFile(file, 'utf8'), file)
    }
  }
  if (project) await buildBackend(project, join(backendRoot, releaseId), gameId, releaseId, gamePackage)

  await stageRelease({
    gameDirectory, backendRoot,
    release: { gameId, releaseId, backendReleaseId, contractVersion: 1, ...browser },
  })
  if (stageOnly) console.log('Staged local ' + gameId + ' release ' + releaseId)
  else {
    await activateRelease({ gameDirectory, backendRoot, gameId, releaseId })
    console.log('Activated local ' + gameId + ' release ' + releaseId)
  }
}

async function publishCloudBrowser(gameId, options) {
  await findGame(gameId)
  const bucket = options['browser-bucket']
  const assetBase = new URL(process.env.VITE_GAME_RELEASE_BASE_URL ?? 'https://invalid.invalid/')
  if (assetBase.href.replace(/\/$/, '') !== 'https://storage.googleapis.com/' + bucket) {
    throw new Error('Set VITE_GAME_RELEASE_BASE_URL to the configured public game-release bucket URL')
  }
  const credentials = spawnSync('gcloud', ['auth', 'print-access-token'], {
    encoding: 'utf8', shell: process.platform === 'win32', windowsHide: true,
  })
  if (credentials.error || credentials.status !== 0) throw new Error('Configure gcloud authentication for game releases')
  const { createGameReleaseStorage } = await import('./game-release-storage.mjs')
  const storage = createGameReleaseStorage({ bucket, accessToken: credentials.stdout.trim() })

  if (options.inspect) {
    console.log(JSON.stringify(await storage.readActive(gameId), null, 2))
    return
  }
  const upload = options['publish-browser']
  if (upload !== undefined) {
    const { objectCount } = await storage.stageBrowserRelease({ repository, gameId, releaseId: upload })
    console.log('Staged ' + objectCount + ' cloud browser artifacts for ' + gameId + ' release ' + upload)
    return
  }
  const releaseId = options.promote ?? options.rollback
  const { generation } = await storage.promoteBrowserRelease({
    gameId, releaseId, expectedGeneration: options['if-generation'],
  })
  console.log('Activated cloud browser release ' + releaseId + ' for ' + gameId + ' at generation ' + generation)
}

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    'assets-only': { type: 'boolean' }, stage: { type: 'boolean' },
    promote: { type: 'string' }, rollback: { type: 'string' },
    'browser-bucket': { type: 'string' }, 'publish-browser': { type: 'string' },
    'if-generation': { type: 'string' }, inspect: { type: 'boolean' },
  },
})
const [gameId] = positionals
const selected = values.promote ?? values.rollback
const bucket = values['browser-bucket']
const upload = values['publish-browser']
const generation = values['if-generation']
const invalidSelection = values.promote !== undefined && values.rollback !== undefined ||
  selected !== undefined && !isReleaseId(selected)
const invalidCloudMode = bucket === undefined
  ? upload !== undefined || generation !== undefined || values.inspect || selected !== undefined && (values.stage || values['assets-only'])
  : !bucket || !values['assets-only'] || values.stage ||
    [upload !== undefined, selected !== undefined, values.inspect === true].filter(Boolean).length !== 1 ||
    upload !== undefined && !isReleaseId(upload) ||
    (selected === undefined ? generation !== undefined : !/^(0|[1-9][0-9]*)$/.test(generation ?? ''))
if (positionals.length !== 1 || !isGameId(gameId) || invalidSelection || invalidCloudMode) {
  throw new Error('Usage: npm run deploy:game -- <game-id> [--assets-only] [--stage | --promote <release-id> | --rollback <release-id>]\n' +
    'Cloud browser releases require --assets-only --browser-bucket <bucket> and one of --inspect, --publish-browser <release-id>, or --promote/--rollback <release-id> --if-generation <generation>')
}
const target = {
  gameId,
  gameDirectory: join(repository, 'artifacts', 'game-releases', 'games', gameId),
  backendRoot: join(repository, 'artifacts', 'game-engine-releases', 'games', gameId, 'releases'),
}
if (bucket !== undefined) {
  await publishCloudBrowser(gameId, values)
} else if (selected !== undefined) {
  await activateRelease({ ...target, releaseId: selected, rollback: values.rollback !== undefined })
  console.log('Activated local ' + gameId + ' release ' + selected)
} else {
  await publishGame(target, values['assets-only'], values.stage)
}
