import { randomUUID } from 'node:crypto'
import { open, readFile, realpath, rename, stat, unlink, writeFile } from 'node:fs/promises'
import { join, relative } from 'node:path'

const gameIdentifier = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const releaseIdentifier = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/
const assetSegment = /^[a-z0-9_-][a-z0-9._-]*$/i

export function isReleaseId(value) {
  return typeof value === 'string' && releaseIdentifier.test(value)
}

function assertReleaseIdentity(gameId, releaseId) {
  if (typeof gameId !== 'string' || gameId.length > 128 || !gameIdentifier.test(gameId) || !isReleaseId(releaseId)) {
    throw new Error('Invalid game or release ID')
  }
}

function isAssetPath(value, extension) {
  return typeof value === 'string' && extension.test(value) &&
    value.split('/').every(segment => assetSegment.test(segment))
}

async function assertArtifactPath(path, directory = false) {
  const actual = await realpath(path)
  if (relative(path, actual)) throw new Error('Release artifacts cannot use redirected paths: ' + path)
  const info = await stat(actual)
  if (directory ? !info.isDirectory() : !info.isFile()) {
    throw new Error('Unexpected release artifact type: ' + path)
  }
}

async function readDescriptor(path, optional = false) {
  try {
    await assertArtifactPath(path)
    return JSON.parse(await readFile(path, 'utf8'))
  } catch (error) {
    if (optional && error.code === 'ENOENT') return null
    throw error
  }
}

function browserDescriptor(value, gameId, releaseId) {
  assertReleaseIdentity(gameId, releaseId)
  const kind = value?.kind
  const entryExtension = kind === 'frame' ? /\.html$/i : /\.[cm]?js$/i
  if (value?.gameId !== gameId || value.releaseId !== releaseId || value.contractVersion !== 1 ||
    (kind !== undefined && kind !== 'module' && kind !== 'frame') ||
    !isReleaseId(value.backendReleaseId) || !isAssetPath(value.entry, entryExtension) ||
    !Array.isArray(value.styles) || !value.styles.every(path => isAssetPath(path, /\.css$/i)) ||
    (kind === 'frame' && value.styles.length !== 0)) {
    throw new Error('Invalid browser descriptor for game release ' + releaseId)
  }
  return { gameId, releaseId, contractVersion: 1, ...(kind === undefined ? {} : { kind }),
    entry: value.entry, styles: value.styles,
    backendReleaseId: value.backendReleaseId }
}

async function assertBackend(backendRoot, gameId, releaseId) {
  const directory = join(backendRoot, releaseId)
  const backend = await readDescriptor(join(directory, 'release.json'))
  if (backend?.gameId !== gameId || backend.releaseId !== releaseId ||
    !isAssetPath(backend.entry, /\.dll$/i) || backend.entry.includes('/')) {
    throw new Error('Invalid backend descriptor for game release ' + releaseId)
  }
  const assembly = backend.entry.slice(0, -4)
  for (const file of [backend.entry, assembly + '.runtimeconfig.json', assembly + '.deps.json']) {
    await assertArtifactPath(join(directory, file))
  }
}

async function assertPair(directory, backendRoot, release) {
  for (const path of [release.entry, ...release.styles]) {
    await assertArtifactPath(join(directory, path))
  }
  await assertBackend(backendRoot, release.gameId, release.backendReleaseId)
}

function serialize(release) {
  return JSON.stringify(release, null, 2) + '\n'
}

export async function readActiveBackendReleaseId({ gameDirectory, backendRoot, gameId }) {
  const active = await readDescriptor(join(gameDirectory, 'active.json'), true)
  if (!active) throw new Error('Publish a combined game release before using --assets-only')
  const release = browserDescriptor(active, gameId, active.releaseId)
  await assertBackend(backendRoot, gameId, release.backendReleaseId)
  return release.backendReleaseId
}

export async function stageRelease({ gameDirectory, backendRoot, release }) {
  const candidate = browserDescriptor(release, release.gameId, release.releaseId)
  const directory = join(gameDirectory, 'releases', candidate.releaseId)
  await assertPair(directory, backendRoot, candidate)
  await writeFile(join(directory, 'release.json'), serialize(candidate), { flag: 'wx' })
  return candidate
}

export async function activateRelease({ gameDirectory, backendRoot, gameId, releaseId, rollback = false }) {
  assertReleaseIdentity(gameId, releaseId)
  await assertArtifactPath(gameDirectory, true)
  await assertArtifactPath(backendRoot, true)
  const activePath = join(gameDirectory, 'active.json')
  const lockPath = join(backendRoot, 'active.lock')
  const pendingPath = join(gameDirectory, '.active-' + randomUUID() + '.json')
  const lock = await open(lockPath, 'wx')
  try {
    const directory = join(gameDirectory, 'releases', releaseId)
    const candidate = browserDescriptor(await readDescriptor(join(directory, 'release.json')), gameId, releaseId)
    await assertPair(directory, backendRoot, candidate)

    if (!rollback && candidate.backendReleaseId !== releaseId) {
      const active = await readDescriptor(activePath, true)
      if (!active || browserDescriptor(active, gameId, active.releaseId).backendReleaseId !== candidate.backendReleaseId) {
        throw new Error('The active backend changed after this assets-only release was staged')
      }
    }
    await writeFile(pendingPath, serialize(candidate), { flag: 'wx' })
    await rename(pendingPath, activePath)
    return candidate
  } finally {
    try {
      await unlink(pendingPath).catch(error => { if (error.code !== 'ENOENT') throw error })
    } finally {
      try {
        await lock.close()
      } finally {
        await unlink(lockPath)
      }
    }
  }
}
