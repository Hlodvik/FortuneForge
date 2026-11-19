// Edited: 2026-11-18T22:45:37-06:00 | Computer: Vinnabok
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { isDeepStrictEqual, parseArgs } from 'node:util'
import { gzipSync } from 'node:zlib'
import { PRESERVED_GAME_DIRECTORY, SITE_ASSET_DIRECTORY, SITE_GAME_PREFIXES } from './site-release-contract.mjs'

export { SITE_GAME_PREFIXES } from './site-release-contract.mjs'

const apiOrigin = 'https://firebasehosting.googleapis.com'
const apiBase = apiOrigin + '/v1beta1/'
const mutablePaths = new Set(['/index.html', '/release.json'])
const hashPattern = /^[a-f0-9]{64}$/
const projectPattern = /^[a-z][a-z0-9-]{4,61}[a-z0-9]$/

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')
const jsonBytes = value => Buffer.from(JSON.stringify(value, null, 2) + '\n')
const assert = (condition, message) => { if (!condition) throw new Error(message) }

function hostingPath(path) {
  assert(typeof path === 'string' && path.startsWith('/') && !/[\\%?#\u0000-\u001f]/.test(path)
    && !path.includes('//') && !path.split('/').some(part => part === '.' || part === '..'),
  'Invalid Hosting file path')
  return path
}

function versionName(name, site) {
  assert(typeof name === 'string' && new RegExp(`^sites/${site}/versions/[A-Za-z0-9_-]+$`).test(name),
    'Expected a version belonging to the selected Hosting site')
  return name
}

function optionsForPublish(options) {
  assert(projectPattern.test(options.project ?? ''), 'A valid Firebase project is required')
  assert(/^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$/.test(options.site ?? ''), 'A valid Hosting site is required')
  versionName(options.baselineVersion, options.site)
  assert(new RegExp(`^sites/${options.site}/releases/[A-Za-z0-9_-]+$`).test(options.baselineRelease ?? ''),
    'An exact baseline release is required')
  assert(/^[a-f0-9]{40}$/.test(options.sourceSha ?? ''), 'A full lowercase source SHA is required')
  return options
}

export function manifestDigest(manifest) {
  return sha256(jsonBytes(Object.fromEntries([...manifest].sort(([left], [right]) => left.localeCompare(right)))))
}

// Firebase stores the SHA256 of compressed bytes, not the uncompressed file.
export function compressSiteFiles(files) {
  const compressed = new Map()
  for (const [path, contents] of files) {
    hostingPath(path)
    assert(path === '/index.html' || path.startsWith(`/${SITE_ASSET_DIRECTORY}/`),
      `Site artifact contains a file outside its deployment scope: ${path}`)
    const bytes = gzipSync(contents, { level: 9 })
    compressed.set(path, { hash: sha256(bytes), bytes })
  }
  assert(compressed.has('/index.html'), 'Site artifact must contain index.html')
  return compressed
}

export async function readSiteArtifact(directory) {
  const root = await realpath(directory)
  const files = new Map()
  async function visit(directoryPath, prefix) {
    for (const entry of await readdir(directoryPath, { withFileTypes: true })) {
      const child = join(directoryPath, entry.name)
      const path = prefix + '/' + entry.name
      assert(!entry.isSymbolicLink(), 'Site artifact must not contain symbolic links')
      if (entry.isDirectory()) await visit(child, path)
      else {
        assert(entry.isFile(), 'Site artifact must contain only regular files')
        files.set(hostingPath(path), await readFile(child))
      }
    }
  }
  await visit(root, '')
  return files
}

export function assertArtifactManifest(files, manifest, sourceSha) {
  assert(manifest?.schemaVersion === 1 && manifest.sourceSha === sourceSha && Array.isArray(manifest.files),
    'A locked site artifact manifest for the candidate source SHA is required')
  const expected = new Map()
  for (const entry of manifest.files) {
    const path = hostingPath(entry.path.startsWith('/') ? entry.path : '/' + entry.path)
    assert(!expected.has(path) && Number.isSafeInteger(entry.bytes) && entry.bytes >= 0
      && hashPattern.test(entry.sha256 ?? ''), 'Site artifact manifest contains an invalid or duplicate entry')
    expected.set(path, entry)
  }
  assert(files.size === expected.size, 'Site artifact file set differs from its locked manifest')
  for (const [path, contents] of files) {
    const entry = expected.get(path)
    assert(entry && contents.length === entry.bytes && sha256(contents) === entry.sha256,
      `Site artifact differs from its locked manifest: ${path}`)
  }
}

export function selectPreservedGameEntry({ baselineVersion, baselineFiles, baselineMarker, baselineConfig, site }) {
  const indexHash = baselineFiles.get('/index.html')
  assert(hashPattern.test(indexHash ?? ''), 'Baseline Hosting version has no valid index.html')
  const gameRules = (baselineConfig.rewrites ?? []).filter(rule =>
    SITE_GAME_PREFIXES.some(prefix => rule.glob === prefix + '**'))
  if (baselineMarker?.scope === 'site') {
    const entry = baselineMarker.preservedGameEntry
    assert(entry && baselineMarker.target === 'hosting' && baselineMarker.schemaVersion === 1,
      'Prior site release has an invalid preservation marker')
    versionName(entry.sourceVersion, site)
    assert(entry.sourceVersion !== baselineVersion, 'Prior site release cannot preserve its own site index as a game entry')
    const versionId = entry.sourceVersion.split('/').at(-1)
    assert(entry.path === `/${PRESERVED_GAME_DIRECTORY}/${versionId}.html` && hashPattern.test(entry.indexHash ?? ''),
      'Prior site release has an invalid preserved game entry')
    assert(baselineFiles.get(entry.path) === entry.indexHash,
      'Preserved game entry does not match the immutable baseline manifest')
    assert(gameRules.length === SITE_GAME_PREFIXES.length && SITE_GAME_PREFIXES.every(prefix =>
      gameRules.some(rule => rule.glob === prefix + '**' && rule.path === entry.path)),
    'Prior site release game rewrites do not match its preservation marker')
    return { path: entry.path, sourceVersion: entry.sourceVersion, indexHash: entry.indexHash }
  }
  assert(!baselineMarker?.preservedGameEntry && gameRules.length === 0,
    'Baseline has game preservation rules but no valid prior site marker')
  const path = `/${PRESERVED_GAME_DIRECTORY}/${baselineVersion.split('/').at(-1)}.html`
  assert(!baselineFiles.has(path) || baselineFiles.get(path) === indexHash,
    'Preserved game entry collides with an existing baseline file')
  return { path, sourceVersion: baselineVersion, indexHash }
}

export function siteServingConfig(baselineConfig, preservedGameEntry) {
  assert(baselineConfig && Array.isArray(baselineConfig.rewrites), 'Baseline Hosting serving config is required')
  assert(baselineConfig.rewrites.some(rule => rule.glob === '**' && rule.path === '/index.html'),
    'Baseline must have the established SPA fallback')
  const config = structuredClone(baselineConfig)
  // Explicit game routes precede the SPA fallback. Exact /slots, /cards and /games
  // remain catalogue pages; existing static game files still take precedence.
  config.rewrites = [
    ...SITE_GAME_PREFIXES.map(prefix => ({ glob: prefix + '**', path: preservedGameEntry.path })),
    ...config.rewrites.filter(rule => !SITE_GAME_PREFIXES.some(prefix => rule.glob === prefix + '**')),
  ]
  config.headers ??= []
  const immutable = { glob: `/${SITE_ASSET_DIRECTORY}/**`, headers: { 'Cache-Control': 'public, max-age=31536000, immutable' } }
  config.headers = [...config.headers.filter(rule => rule.glob !== immutable.glob), immutable]
  return config
}

export function expectedSiteManifest(baselineFiles, siteFiles, preservedGameEntry) {
  const expected = new Map(baselineFiles)
  for (const [path, { hash }] of siteFiles) {
    assert(mutablePaths.has(path) || path.startsWith(`/${SITE_ASSET_DIRECTORY}/`), 'Site release attempted an out-of-scope path')
    assert(mutablePaths.has(path) || !expected.has(path) || expected.get(path) === hash,
      `Site release would overwrite an immutable baseline file: ${path}`)
    expected.set(path, hash)
  }
  assert(!expected.has(preservedGameEntry.path) || expected.get(preservedGameEntry.path) === preservedGameEntry.indexHash,
    'Site release would overwrite its preserved game entry')
  expected.set(preservedGameEntry.path, preservedGameEntry.indexHash)
  return expected
}

export function assertExactManifest(actual, expected) {
  assert(actual.size === expected.size, 'Hosting version file count differs from the locked manifest')
  for (const [path, hash] of expected) {
    assert(actual.get(path) === hash, `Hosting version changed or omitted a locked file: ${path}`)
  }
}

function activeToken(project) {
  const result = spawnSync(process.platform === 'win32' ? 'gcloud.cmd' : 'gcloud',
    ['auth', 'print-access-token', `--project=${project}`],
    { encoding: 'utf8', windowsHide: true, shell: process.platform === 'win32' })
  assert(!result.error && result.status === 0 && result.stdout.trim(),
    'Configure active gcloud authentication before publishing a site release')
  return result.stdout.trim()
}

export function createHostingRequest(project, token, fetchImpl = fetch) {
  assert(projectPattern.test(project ?? ''), 'A valid Firebase project is required')
  return async ({ method = 'GET', url, body, bytes }) => {
    let response
    try {
      response = await fetchImpl(url, {
        method,
        redirect: 'error',
        signal: AbortSignal.timeout(120_000),
        headers: {
          Authorization: `Bearer ${token}`,
          'x-goog-user-project': project,
          'Content-Type': bytes ? 'application/octet-stream' : 'application/json',
        },
        body: bytes ?? (body === undefined ? undefined : JSON.stringify(body)),
      })
    } catch { throw new Error('Firebase Hosting network request failed') }
    // Never put credentials, signed upload URLs, or response bodies in errors.
    assert(response.ok, `Firebase Hosting request failed (${response.status})`)
    if (bytes || response.status === 204) return {}
    try { return await response.json() }
    catch { throw new Error('Firebase Hosting returned an invalid JSON response') }
  }
}

async function listFiles(request, name) {
  const files = new Map()
  let pageToken = ''
  const seenTokens = new Set()
  do {
    const query = new URLSearchParams({ pageSize: '1000', status: 'ACTIVE' })
    if (pageToken) query.set('pageToken', pageToken)
    const result = await request({ url: apiBase + name + '/files?' + query })
    for (const file of result.files ?? []) {
      hostingPath(file.path)
      assert(hashPattern.test(file.hash ?? '') && !files.has(file.path), 'Hosting returned an invalid or duplicate file')
      assert(!file.status || file.status === 'ACTIVE', 'Hosting version still has pending file uploads')
      files.set(file.path, file.hash)
    }
    pageToken = result.nextPageToken ?? ''
    assert(!pageToken || !seenTokens.has(pageToken), 'Hosting file pagination did not advance')
    seenTokens.add(pageToken)
  } while (pageToken)
  return files
}

async function assertLiveBaseline(request, options) {
  const result = await request({ url: apiBase + `sites/${options.site}/releases?pageSize=1` })
  const latest = result.releases?.[0]
  assert(latest?.name === options.baselineRelease && latest?.version?.name === options.baselineVersion,
    'Live Hosting changed since preflight; refuse to overwrite another release')
}

async function waitForClone(request, operation, sleep) {
  for (let attempt = 0; attempt < 600; attempt += 1) {
    assert(!operation.error, 'Firebase Hosting version clone failed')
    if (operation.done) return operation.response
    assert(typeof operation.name === 'string' && /^[A-Za-z0-9_/-]+$/.test(operation.name),
      'Firebase Hosting returned an invalid clone operation')
    await sleep(1000)
    operation = await request({ url: apiBase + operation.name })
  }
  throw new Error('Firebase Hosting version clone timed out')
}

function validateUploadUrl(value, site, version) {
  const url = new URL(value)
  assert(url.origin === 'https://upload-firebasehosting.googleapis.com'
    && url.pathname === `/upload/sites/${site}/versions/${version.split('/').at(-1)}/files`
    && !url.username && !url.password && !url.search && !url.hash,
  'Firebase Hosting returned an unexpected upload endpoint')
  return url.href
}

// All network and credential access is injectable so tests never contact Hosting.
export async function publishSiteHosting(options, dependencies = {}) {
  optionsForPublish(options)
  const artifact = dependencies.files ?? await readSiteArtifact(options.directory)
  assertArtifactManifest(artifact, options.artifactManifest, options.sourceSha)
  const files = compressSiteFiles(artifact)
  const request = dependencies.request ?? createHostingRequest(options.project,
    await (dependencies.getToken ?? activeToken)(options.project), dependencies.fetch)
  const sleep = dependencies.sleep ?? (milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)))
  const now = dependencies.now ?? (() => new Date().toISOString())
  const record = {
    schemaVersion: 1, target: 'hosting', scope: 'site', sourceSha: options.sourceSha,
    baselineVersion: options.baselineVersion, baselineRelease: options.baselineRelease, startedAtUtc: now(),
  }
  let releaseAttempted = false
  const saveRecord = async () => {
    if (dependencies.writeRecord) await dependencies.writeRecord(structuredClone(record))
    else if (options.recordPath) {
      await mkdir(dirname(resolve(options.recordPath)), { recursive: true })
      await writeFile(options.recordPath, jsonBytes(record))
    }
  }
  try {
    await assertLiveBaseline(request, options)
    const baseline = await request({ url: apiBase + options.baselineVersion })
    assert(baseline.name === options.baselineVersion && baseline.status === 'FINALIZED',
      'Baseline Hosting version must be finalized and immutable')
    const baselineFiles = await listFiles(request, options.baselineVersion)
    const preservedGameEntry = selectPreservedGameEntry({
      baselineVersion: options.baselineVersion, baselineConfig: baseline.config,
      baselineMarker: options.baselineMarker, baselineFiles, site: options.site,
    })
    // The original source version may have expired under Hosting retention.
    // Its bytes remain pinned in the current baseline and are verified above;
    // sourceVersion records provenance without creating a retention dependency.
    const marker = {
      schemaVersion: 1, target: 'hosting', scope: 'site', sourceSha: options.sourceSha,
      builtAtUtc: now(), preservedGameEntry,
    }
    const markerBytes = gzipSync(jsonBytes(marker), { level: 9 })
    files.set('/release.json', { hash: sha256(markerBytes), bytes: markerBytes })
    const expected = expectedSiteManifest(baselineFiles, files, preservedGameEntry)
    const config = siteServingConfig(baseline.config, preservedGameEntry)
    Object.assign(record, {
      preservedGameEntry, marker,
      baselineManifestSha256: manifestDigest(baselineFiles), manifestSha256: manifestDigest(expected),
      preservedFileCount: baselineFiles.size - [...mutablePaths].filter(path => baselineFiles.has(path)).length,
    })
    await saveRecord()
    const operation = await request({ method: 'POST', url: apiBase + `sites/${options.site}/versions:clone`,
      body: { sourceVersion: options.baselineVersion, finalize: false } })
    const clone = await waitForClone(request, operation, sleep)
    const name = versionName(clone?.name, options.site)
    assert(name !== options.baselineVersion, 'Clone unexpectedly returned the baseline version')
    record.versionName = name
    await saveRecord()
    assertExactManifest(await listFiles(request, name), baselineFiles)
    const additions = Object.fromEntries([...files].map(([path, entry]) => [path, entry.hash]))
    additions[preservedGameEntry.path] = preservedGameEntry.indexHash
    const populated = await request({ method: 'POST', url: apiBase + name + ':populateFiles', body: { files: additions } })
    const blobs = new Map([...files.values()].map(entry => [entry.hash, entry.bytes]))
    const baselineHashes = new Set(baselineFiles.values())
    const required = new Set(populated.uploadRequiredHashes ?? [])
    assert([...required].every(hash => blobs.has(hash) && !baselineHashes.has(hash)),
      'Hosting requested a baseline game blob; refuse to upload or rebuild preserved games')
    if (required.size) {
      const uploadUrl = validateUploadUrl(populated.uploadUrl, options.site, name)
      // A small bounded pool avoids overwhelming the local machine with uploads.
      const queue = [...required]
      await Promise.all(Array.from({ length: Math.min(4, queue.length) }, async () => {
        while (queue.length) {
          const hash = queue.shift()
          await request({ method: 'POST', url: uploadUrl + '/' + hash, bytes: blobs.get(hash) })
        }
      }))
    }
    await request({ method: 'PATCH', url: apiBase + name + '?updateMask=config', body: { config } })
    assertExactManifest(await listFiles(request, name), expected)
    const finalized = await request({ method: 'PATCH', url: apiBase + name + '?updateMask=status',
      body: { status: 'FINALIZED' } })
    assert(finalized.name === name && finalized.status === 'FINALIZED', 'Hosting did not finalize the candidate version')
    assertExactManifest(await listFiles(request, name), expected)
    const locked = await request({ url: apiBase + name })
    assert(isDeepStrictEqual(locked.config, config), 'Hosting candidate config differs from the locked config')
    // Hosting has no conditional release-create API. Recheck the pinned release
    // immediately before the single publish call and never retry that mutation.
    record.state = 'publishing'
    record.uploadedBlobCount = required.size
    await saveRecord()
    await assertLiveBaseline(request, options)
    releaseAttempted = true
    const released = await request({ method: 'POST',
      url: apiBase + `sites/${options.site}/releases?` + new URLSearchParams({ versionName: name }),
      body: { message: `Site-only ${options.sourceSha}; games preserved from ${preservedGameEntry.sourceVersion}` } })
    assert(released.version?.name === name && typeof released.name === 'string',
      'Hosting release response did not identify the published candidate; inspect the release record before retrying')
    record.releaseName = released.name
    record.state = 'released'
    record.finishedAtUtc = now()
    await saveRecord()
    return record
  } catch (error) {
    record.state = record.state === 'released' ? 'released' : releaseAttempted ? 'publish-unknown' : 'failed'
    record.error = error.message
    await saveRecord()
    throw error
  }
}

async function main() {
  const { values } = parseArgs({ options: Object.fromEntries([
    'project', 'site', 'baseline-version', 'baseline-release', 'baseline-marker', 'directory', 'source-sha', 'record-path', 'artifact-manifest',
  ].map(name => [name, { type: 'string' }])) })
  for (const name of ['project', 'site', 'baseline-version', 'baseline-release', 'directory', 'source-sha', 'record-path', 'artifact-manifest']) {
    assert(values[name], `Missing --${name}`)
  }
  const baselineMarker = values['baseline-marker']
    ? JSON.parse((await readFile(values['baseline-marker'], 'utf8')).replace(/^\uFEFF/, '')) : null
  const result = await publishSiteHosting({
    project: values.project, site: values.site, baselineVersion: values['baseline-version'],
    baselineRelease: values['baseline-release'], baselineMarker, directory: values.directory,
    sourceSha: values['source-sha'], recordPath: values['record-path'],
    artifactManifest: JSON.parse((await readFile(values['artifact-manifest'], 'utf8')).replace(/^\uFEFF/, '')),
  })
  process.stdout.write(JSON.stringify(result) + '\n')
}

const invokedPath = process.argv[1] && await realpath(process.argv[1]).catch(() => resolve(process.argv[1]))
if (invokedPath && pathToFileURL(invokedPath).href === pathToFileURL(await realpath(fileURLToPath(import.meta.url))).href) {
  main().catch(error => { process.stderr.write(error.message + '\n'); process.exitCode = 1 })
}
