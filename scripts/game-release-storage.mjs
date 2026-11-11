import { createHash, randomUUID } from 'node:crypto'
import { readFile, readdir } from 'node:fs/promises'
import { extname, join, resolve } from 'node:path'
import { assertArtifactPath, browserDescriptor, isAssetPath, isGameId, isReleaseId } from './game-release-activation.mjs'

const storageOrigin = 'https://storage.googleapis.com'
const immutableCache = 'public, max-age=31536000, immutable'
const jsonType = 'application/json'
const generationPattern = /^(?:0|[1-9][0-9]*)$/
const mimeTypes = new Map([
  ['.html', 'text/html'], ['.css', 'text/css'],
  ['.js', 'text/javascript'], ['.mjs', 'text/javascript'], ['.cjs', 'text/javascript'],
  ['.json', jsonType], ['.wasm', 'application/wasm'],
  ['.svg', 'image/svg+xml'], ['.ico', 'image/x-icon'], ['.png', 'image/png'],
  ['.jpg', 'image/jpeg'], ['.jpeg', 'image/jpeg'], ['.gif', 'image/gif'],
  ['.webp', 'image/webp'], ['.avif', 'image/avif'],
  ['.woff', 'font/woff'], ['.woff2', 'font/woff2'], ['.ttf', 'font/ttf'], ['.otf', 'font/otf'],
  ['.wav', 'audio/wav'], ['.ogg', 'audio/ogg'], ['.mp3', 'audio/mpeg'], ['.m4a', 'audio/mp4'],
  ['.mp4', 'video/mp4'], ['.webm', 'video/webm'], ['.ogv', 'video/ogg'],
  ['.gltf', 'model/gltf+json'], ['.glb', 'model/gltf-binary'],
])

function gamePrefix(gameId) {
  if (!isGameId(gameId)) throw new Error('Invalid game ID for browser publication')
  return 'games/' + gameId
}

function releasePrefix(gameId, releaseId) {
  if (!isReleaseId(releaseId)) throw new Error('Invalid game release ID for browser publication')
  return gamePrefix(gameId) + '/releases/' + releaseId
}

async function browserFiles(directory, prefix = '') {
  await assertArtifactPath(directory, true)
  const files = []
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const name = prefix + entry.name
    if (!isAssetPath(name)) throw new Error('Unsupported browser artifact path: ' + name)
    if (name === 'release.json') continue
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      files.push(...await browserFiles(path, name + '/'))
    } else {
      const contentType = mimeTypes.get(extname(entry.name).toLowerCase())
      if (!entry.isFile() || !contentType) throw new Error('Unsupported browser artifact: ' + name)
      await assertArtifactPath(path)
      files.push({ name, path, contentType })
    }
  }
  return files
}

function objectMetadata(name, bytes, contentType, cacheControl) {
  return {
    name,
    contentType,
    cacheControl,
    md5Hash: createHash('md5').update(bytes).digest('base64'),
    metadata: { sha256: createHash('sha256').update(bytes).digest('hex') },
  }
}

function jsonBytes(value) {
  return Buffer.from(JSON.stringify(value, null, 2) + '\n')
}

function checkedGeneration(object, name) {
  if (object?.name !== name || typeof object.generation !== 'string' ||
    object.generation === '0' || !generationPattern.test(object.generation)) {
    throw new Error('Cloud Storage returned an invalid generation for ' + name)
  }
  return object.generation
}

function generationConflict(name) {
  return new Error('Cloud Storage generation changed for ' + name + '. Inspect the active release and try again.')
}

export function createGameReleaseStorage({ bucket, accessToken, fetchImpl = globalThis.fetch }) {
  if (typeof bucket !== 'string' || !/^[a-z0-9][a-z0-9._-]+[a-z0-9]$/.test(bucket) ||
    typeof accessToken !== 'string' || !/^\S+$/.test(accessToken)) {
    throw new Error('A Cloud Storage bucket and OAuth access token are required')
  }
  const bucketPath = '/storage/v1/b/' + encodeURIComponent(bucket) + '/o'
  const objectUrl = name => new URL(bucketPath + '/' + encodeURIComponent(name), storageOrigin)

  function request(url, { method = 'GET', contentType, body } = {}) {
    const headers = { Authorization: 'Bearer ' + accessToken }
    if (contentType) headers['Content-Type'] = contentType
    return fetchImpl(url, { method, headers, body, redirect: 'error', signal: AbortSignal.timeout(120_000) })
  }

  async function metadata(name) {
    const response = await request(objectUrl(name))
    if (response.status === 404) return null
    if (!response.ok) throw new Error('Cloud Storage could not read ' + name + ' (HTTP ' + response.status + ')')
    const object = await response.json()
    checkedGeneration(object, name)
    return object
  }

  async function readObjectJson(name, generation) {
    const url = objectUrl(name)
    url.searchParams.set('alt', 'media')
    url.searchParams.set('ifGenerationMatch', generation)
    const response = await request(url)
    if (response.status === 412) throw generationConflict(name)
    if (!response.ok) throw new Error('Cloud Storage could not read ' + name + ' (HTTP ' + response.status + ')')
    return response.json()
  }

  async function insert(object, bytes, expectedGeneration) {
    const url = new URL('/upload' + bucketPath, storageOrigin)
    url.searchParams.set('uploadType', 'multipart')
    url.searchParams.set('ifGenerationMatch', expectedGeneration)
    const boundary = 'ff-' + randomUUID()
    const header = Buffer.from('--' + boundary + '\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n' +
      JSON.stringify(object) + '\r\n--' + boundary + '\r\nContent-Type: ' + object.contentType + '\r\n\r\n')
    const body = Buffer.concat([header, bytes, Buffer.from('\r\n--' + boundary + '--\r\n')])
    const response = await request(url, { method: 'POST', contentType: 'multipart/related; boundary=' + boundary, body })
    if (response.status === 412) return null
    if (!response.ok) throw new Error('Cloud Storage could not upload ' + object.name + ' (HTTP ' + response.status + ')')
    return checkedGeneration(await response.json(), object.name)
  }

  async function uploadImmutable(name, bytes, contentType) {
    const object = objectMetadata(name, bytes, contentType, immutableCache)
    if (await insert(object, bytes, '0') !== null) return

    // A retry may encounter assets written before an earlier attempt stopped.
    const existing = await metadata(name)
    if (!existing || existing.size !== String(bytes.byteLength) || existing.md5Hash !== object.md5Hash ||
      existing.metadata?.sha256 !== object.metadata.sha256 || existing.contentType !== contentType ||
      existing.cacheControl !== immutableCache) {
      throw new Error('An immutable game release object already contains different data or metadata: ' + name)
    }
  }

  async function readActive(gameId) {
    const name = gamePrefix(gameId) + '/active.json'
    const object = await metadata(name)
    if (!object) return { generation: '0', release: null }
    const value = await readObjectJson(name, object.generation)
    const release = browserDescriptor(value, gameId, value?.releaseId, { allowBrowserOnly: true })
    return { generation: object.generation, release }
  }

  async function stageBrowserRelease({ repository, gameId, releaseId }) {
    const prefix = releasePrefix(gameId, releaseId)
    const directory = resolve(repository, 'artifacts', 'game-releases', prefix)
    const descriptor = join(directory, 'release.json')
    await assertArtifactPath(descriptor)
    const release = browserDescriptor(JSON.parse(await readFile(descriptor, 'utf8')), gameId, releaseId)
    delete release.backendReleaseId

    const files = await browserFiles(directory)
    const names = new Set(files.map(file => file.name))
    for (const asset of [release.entry, ...release.styles]) {
      if (!names.has(asset)) throw new Error('The staged browser release is missing ' + asset)
    }
    for (const file of files) {
      await uploadImmutable(prefix + '/' + file.name, await readFile(file.path), file.contentType)
    }
    await uploadImmutable(prefix + '/release.json', jsonBytes(release), jsonType)
    return { release, objectCount: files.length + 1 }
  }

  async function promoteBrowserRelease({ gameId, releaseId, expectedGeneration }) {
    if (typeof expectedGeneration !== 'string' || !generationPattern.test(expectedGeneration)) {
      throw new Error('Promotion requires the observed active generation as a decimal string')
    }
    const prefix = releasePrefix(gameId, releaseId)
    const activeName = gamePrefix(gameId) + '/active.json'
    const current = await readActive(gameId)
    if (current.generation !== expectedGeneration) throw generationConflict(activeName)

    const descriptor = prefix + '/release.json'
    const object = await metadata(descriptor)
    if (!object || object.cacheControl !== immutableCache) throw new Error('The browser release has not been staged')
    const value = await readObjectJson(descriptor, object.generation)
    const release = browserDescriptor(value, gameId, releaseId, { allowBrowserOnly: true })
    if (release.backendReleaseId !== undefined) throw new Error('Expected a staged browser-only release')
    for (const asset of [release.entry, ...release.styles]) {
      const stored = await metadata(prefix + '/' + asset)
      if (!stored || stored.cacheControl !== immutableCache) throw new Error('The staged browser release is missing ' + asset)
    }
    if (current.release?.backendReleaseId !== undefined) release.backendReleaseId = current.release.backendReleaseId

    const bytes = jsonBytes(release)
    const generation = await insert(objectMetadata(activeName, bytes, jsonType, 'no-store'), bytes, expectedGeneration)
    if (generation === null) throw generationConflict(activeName)
    return { generation, release }
  }

  return { readActive, stageBrowserRelease, promoteBrowserRelease }
}
