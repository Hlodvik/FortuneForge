import { spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { mkdir, readFile, rename, unlink, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { gameHostRuntime } from './game-release-runtime.mjs'

const repository = fileURLToPath(new URL('../', import.meta.url))
const readJson = async path => JSON.parse(await readFile(path, 'utf8'))

async function publishGame(gameId) {
  const workspaceName = `@fortuneforge/games-${gameId}`
  const { workspaces } = await readJson(join(repository, 'package.json'))
  const games = await Promise.all(workspaces.filter(path => path.startsWith('games/')).map(async path => {
    const directory = join(repository, path)
    return { directory, manifest: await readJson(join(directory, 'package.json')) }
  }))
  const workspace = games.find(game => game.manifest.name === workspaceName)
  const entry = workspace?.manifest.gameRelease?.entry
  if (typeof entry !== 'string' || isAbsolute(entry)) throw new Error(`${gameId} has no browser release entry`)
  const entryPath = resolve(workspace.directory, entry)
  if (!relative(workspace.directory, entryPath).startsWith(`src${sep}`)) {
    throw new Error(`${gameId} browser entry must be inside its src directory`)
  }

  const prerequisites = spawnSync(process.execPath, [
    fileURLToPath(new URL('./run-game-workspaces.mjs', import.meta.url)),
    'build', workspaceName, '--dependencies-only',
  ], { cwd: repository, stdio: 'inherit' })
  if (prerequisites.error) throw prerequisites.error
  if (prerequisites.status !== 0) throw new Error(`Dependency build failed for ${gameId}`)

  const releaseId = randomUUID()
  const gameDirectory = join(repository, 'artifacts', 'game-releases', 'games', gameId)
  const releasesDirectory = join(gameDirectory, 'releases')
  await mkdir(releasesDirectory, { recursive: true })
  const releaseDirectory = join(releasesDirectory, releaseId)
  await mkdir(releaseDirectory)

  const requireFromWorkspace = createRequire(join(workspace.directory, 'package.json'))
  const { build } = await import(pathToFileURL(requireFromWorkspace.resolve('vite')).href)
  const bundle = await build({
    root: workspace.directory,
    configFile: false,
    envFile: false,
    publicDir: false,
    base: './',
    build: {
      outDir: releaseDirectory,
      emptyOutDir: false,
      assetsInlineLimit: 0,
      cssCodeSplit: false,
      modulePreload: false,
      rolldownOptions: {
        input: entryPath,
        external: Object.keys(gameHostRuntime),
        preserveEntrySignatures: 'strict',
        output: { paths: gameHostRuntime },
      },
    },
  })
  const output = Array.isArray(bundle) ? bundle.flatMap(result => result.output) : bundle.output
  const entryChunk = output.find(item => item.type === 'chunk' && item.isEntry && item.exports.includes('default'))
  if (!entryChunk) throw new Error(`${gameId} browser entry must export a default component`)
  const styles = output.filter(item => item.type === 'asset' && item.fileName.endsWith('.css')).map(item => item.fileName)
  const release = { gameId, releaseId, contractVersion: 1, entry: entryChunk.fileName, styles }
  const descriptor = JSON.stringify(release, null, 2) + '\n'
  await writeFile(join(releaseDirectory, 'release.json'), descriptor, { flag: 'wx' })

  const pendingPointer = join(gameDirectory, `.${releaseId}.json`)
  await writeFile(pendingPointer, descriptor, { flag: 'wx' })
  try {
    await rename(pendingPointer, join(gameDirectory, 'active.json'))
  } catch (error) {
    await unlink(pendingPointer).catch(() => {})
    throw error
  }
  console.log(`Activated local ${gameId} release ${releaseId}`)
}

const [gameId, ...extra] = process.argv.slice(2)
if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(gameId ?? '') || extra.length) {
  throw new Error('Usage: npm run deploy:game -- <game-id>')
}
await publishGame(gameId)
