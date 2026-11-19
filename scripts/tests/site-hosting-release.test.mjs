// Edited: 2026-11-18T22:48:00-06:00 | Computer: Vinnabok
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdtemp, mkdir, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { basename, dirname, join } from 'node:path'
import test from 'node:test'
import { gunzipSync, gzipSync } from 'node:zlib'
import {
  assertArtifactManifest, assertExactManifest, compressSiteFiles, createHostingRequest, manifestDigest,
  publishSiteHosting, readSiteArtifact, SITE_GAME_PREFIXES, siteServingConfig,
} from '../publish-site-hosting.mjs'

const site = 'fortune-test'
const baselineVersion = `sites/${site}/versions/full-v1`
const baselineRelease = `sites/${site}/releases/release-v1`
const sourceSha = 'a'.repeat(40)
const digest = bytes => createHash('sha256').update(bytes).digest('hex')
const contentHash = text => digest(gzipSync(text, { level: 9 }))
const siteFiles = () => new Map([
  ['/index.html', Buffer.from('<html><script src="/assets/site/index-hash.js"></script></html>')],
  ['/assets/site/index-hash.js', Buffer.from('document.body.dataset.site = "new"')],
])
const artifactManifest = files => ({
  schemaVersion: 1, sourceSha,
  files: [...files].map(([path, bytes]) => ({ path: path.slice(1), bytes: bytes.length, sha256: digest(bytes) })),
})
const baseConfig = () => ({
  cleanUrls: true, trailingSlashBehavior: 'REMOVE',
  headers: [{ glob: '/assets/**', headers: { 'Cache-Control': 'public, max-age=31536000, immutable' } }],
  rewrites: [
    { glob: '/api/**', run: { serviceId: 'existing-api', region: 'us-east4' } },
    { glob: '**', path: '/index.html' },
  ],
})
const baselineFiles = () => new Map([
  ['/index.html', contentHash('old entire SPA')],
  ['/release.json', contentHash('{"sourceSha":"old"}')],
  ['/assets/old-game.js', contentHash('old game code')],
  ['/assets/old-runtime.js', contentHash('old React runtime')],
  ['/games/jade-dragon/index.html', contentHash('standalone jade game')],
  ['/games/jade-dragon/game.js', contentHash('standalone jade code')],
])

// A stateful, paginated REST fixture checks protocol behavior without network or
// credentials. It models content-addressed aliases and pending uploads.
function hostingFixture({ files = baselineFiles(), config = baseConfig(), marker = null, hooks = {} } = {}) {
  const candidateName = `sites/${site}/versions/site-v2`
  const versions = new Map([[baselineVersion, { files: new Map(files), config: structuredClone(config), status: 'FINALIZED' }]])
  const knownBlobs = new Set([...versions.values()].flatMap(version => [...version.files.values()]))
  const requests = []
  const uploaded = new Map()
  const records = []
  let liveReadCount = 0
  const request = async input => {
    const { url, body, bytes, method = 'GET' } = input
    const parsed = new URL(url)
    requests.push(input)
    if (hooks.before) await hooks.before(input, { versions, candidateName, knownBlobs })
    if (parsed.hostname === 'upload-firebasehosting.googleapis.com') {
      assert.equal(method, 'POST')
      const hash = parsed.pathname.split('/').at(-1)
      assert.equal(digest(bytes), hash)
      uploaded.set(hash, Buffer.from(bytes))
      knownBlobs.add(hash)
      return {}
    }
    assert.equal(parsed.origin, 'https://firebasehosting.googleapis.com')
    const path = parsed.pathname.replace('/v1beta1/', '')
    if (path === `sites/${site}/releases` && method === 'GET') {
      liveReadCount += 1
      return { releases: [{ name: hooks.race && liveReadCount > 1 ? 'another-release' : baselineRelease,
        version: { name: baselineVersion } }] }
    }
    if (path === `sites/${site}/versions:clone`) {
      assert.equal(method, 'POST')
      assert.deepEqual(body, { sourceVersion: baselineVersion, finalize: false })
      versions.set(candidateName, { files: new Map(files), config: structuredClone(config), status: 'CREATED' })
      if (hooks.corruptClone) versions.get(candidateName).files.delete('/assets/old-game.js')
      return { name: 'projects/example/operations/clone-1', done: false }
    }
    if (path === 'projects/example/operations/clone-1') {
      return { done: true, response: { name: candidateName, status: 'CREATED' } }
    }
    if (path.endsWith('/files')) {
      const name = path.slice(0, -'/files'.length)
      const version = versions.get(name)
      assert.ok(version, 'file listing must identify an existing version')
      assert.equal(parsed.searchParams.get('status'), 'ACTIVE')
      const entries = [...version.files].filter(([, hash]) => knownBlobs.has(hash))
      const start = Number(parsed.searchParams.get('pageToken') ?? 0)
      return {
        files: entries.slice(start, start + 2).map(([path, hash]) => ({ path, hash, status: 'ACTIVE' })),
        ...(entries.length > start + 2 ? { nextPageToken: String(start + 2) } : {}),
      }
    }
    if (path === candidateName + ':populateFiles') {
      assert.equal(method, 'POST')
      const candidate = versions.get(candidateName)
      for (const [path, hash] of Object.entries(body.files)) {
        assert.notEqual(hash, '', 'site release must never delete a file')
        candidate.files.set(path, hash)
      }
      return {
        uploadRequiredHashes: hooks.requestOldBlob
          ? [files.get('/index.html')]
          : [...new Set(Object.values(body.files))].filter(hash => !knownBlobs.has(hash)),
        uploadUrl: hooks.uploadUrl ?? `https://upload-firebasehosting.googleapis.com/upload/${candidateName}/files`,
      }
    }
    if (path === candidateName && method === 'PATCH') {
      const candidate = versions.get(candidateName)
      if (parsed.searchParams.get('updateMask') === 'config') candidate.config = structuredClone(body.config)
      else {
        assert.equal(body.status, 'FINALIZED')
        assert.ok([...candidate.files.values()].every(hash => knownBlobs.has(hash)), 'finalize requires all blobs')
        candidate.status = 'FINALIZED'
        if (hooks.corruptFinalized) candidate.files.set('/assets/old-game.js', contentHash('changed'))
      }
      return { name: candidateName, status: candidate.status, config: candidate.config }
    }
    if (versions.has(path) && method === 'GET') {
      const version = versions.get(path)
      return { name: path, status: version.status,
        config: hooks.corruptConfig && path === candidateName ? baseConfig() : structuredClone(version.config) }
    }
    if (path === `sites/${site}/releases` && method === 'POST') {
      assert.equal(parsed.searchParams.get('versionName'), candidateName)
      assert.equal(versions.get(candidateName).status, 'FINALIZED')
      if (hooks.releaseError) throw new Error('Simulated lost release response')
      return { name: `sites/${site}/releases/release-v2`, version: { name: candidateName } }
    }
    throw new Error('Unexpected fixture request: ' + method + ' ' + path)
  }
  const artifact = siteFiles()
  return {
    requests, uploaded, versions, records, candidateName,
    options: { project: 'fortune-test', site, sourceSha, baselineVersion, baselineRelease,
      baselineMarker: marker, artifactManifest: artifactManifest(artifact) },
    dependencies: { request, files: artifact, sleep: async () => {}, now: () => '2026-11-19T04:48:00.000Z',
      writeRecord: async record => records.push(record) },
  }
}

const publish = fixture => publishSiteHosting(fixture.options, fixture.dependencies)
const releaseRequests = fixture => fixture.requests.filter(request => request.method === 'POST'
  && new URL(request.url).pathname.endsWith('/releases'))

test('HTTP transport sends project quota attribution on API calls and unmodified gzip uploads', async () => {
  const requests = []
  let jsonReads = 0
  const request = createHostingRequest('quota-project', 'fake-access-token', async (url, options) => {
    requests.push({ url, ...options })
    return { ok: true, status: 200, json: async () => { jsonReads += 1; return { success: true } } }
  })
  const apiUrl = `https://firebasehosting.googleapis.com/v1beta1/sites/${site}/versions`
  await request({ url: apiUrl })
  await request({ method: 'POST', url: apiUrl, body: { sourceVersion: baselineVersion, finalize: false } })
  await request({ method: 'PATCH', url: apiUrl + '/candidate', body: { status: 'FINALIZED' } })
  const compressed = gzipSync(Buffer.from('exact site upload bytes'), { level: 9 })
  await request({ method: 'POST',
    url: `https://upload-firebasehosting.googleapis.com/upload/sites/${site}/versions/candidate/files/${digest(compressed)}`,
    bytes: compressed })
  for (const sent of requests) {
    assert.equal(sent.headers['x-goog-user-project'], 'quota-project')
    assert.equal(sent.headers.Authorization, 'Bearer fake-access-token')
    assert.equal(sent.redirect, 'error')
    assert.ok(sent.signal instanceof AbortSignal)
  }
  assert.equal(requests[0].body, undefined)
  assert.equal(requests[1].body, JSON.stringify({ sourceVersion: baselineVersion, finalize: false }))
  assert.equal(requests[2].headers['Content-Type'], 'application/json')
  assert.equal(requests[3].headers['Content-Type'], 'application/octet-stream')
  assert.equal(requests[3].body, compressed)
  assert.equal(gunzipSync(requests[3].body).toString(), 'exact site upload bytes')
  assert.equal(jsonReads, 3, 'uploads do not attempt to parse a JSON response')
})

test('publisher wires the selected project through the real HTTP transport for every API request and upload', async () => {
  const fixture = hostingFixture()
  const requestFixture = fixture.dependencies.request
  delete fixture.dependencies.request
  const calls = []
  fixture.options.project = 'quota-project'
  fixture.dependencies.getToken = project => { assert.equal(project, 'quota-project'); return 'fake-access-token' }
  fixture.dependencies.fetch = async (url, options) => {
    calls.push({ url, ...options })
    assert.equal(options.headers['x-goog-user-project'], 'quota-project')
    const result = await requestFixture({
      url, method: options.method,
      ...(typeof options.body === 'string' ? { body: JSON.parse(options.body) } : { bytes: options.body }),
    })
    return { ok: true, status: 200, json: async () => result }
  }
  const result = await publish(fixture)
  assert.equal(result.state, 'released')
  assert.equal(calls.filter(call => new URL(call.url).hostname === 'upload-firebasehosting.googleapis.com').length, 3)
  assert.ok(calls.some(call => call.method === 'PATCH'))
  assert.equal(releaseRequests(fixture).length, 1)
})

test('HTTP failures redact credentials, endpoint queries and response contents', async () => {
  const url = 'https://firebasehosting.googleapis.com/v1beta1/sites/fortune-test/releases?private=endpoint-secret'
  let bodyRead = false
  const denied = createHostingRequest('quota-project', 'fake-secret-token', async () => ({
    ok: false, status: 403, json: async () => { bodyRead = true; return { secret: 'response-secret' } },
  }))
  await assert.rejects(denied({ url }), { message: 'Firebase Hosting request failed (403)' })
  assert.equal(bodyRead, false)
  const unavailable = createHostingRequest('quota-project', 'fake-secret-token', async () => {
    throw new Error('Transport included fake-secret-token and endpoint-secret')
  })
  await assert.rejects(unavailable({ url }), { message: 'Firebase Hosting network request failed' })
  const malformed = createHostingRequest('quota-project', 'fake-secret-token', async () => ({
    ok: true, status: 200, json: async () => { throw new Error('response-secret') },
  }))
  await assert.rejects(malformed({ url }), { message: 'Firebase Hosting returned an invalid JSON response' })
})

test('first site deployment preserves every game byte, aliases its existing index blob, and retains API config', async () => {
  const fixture = hostingFixture()
  const result = await publish(fixture)
  const candidate = fixture.versions.get(fixture.candidateName)
  for (const [path, hash] of baselineFiles()) {
    if (path !== '/index.html' && path !== '/release.json') assert.equal(candidate.files.get(path), hash)
  }
  assert.deepEqual(result.preservedGameEntry, {
    path: '/game-shell/full-v1.html', sourceVersion: baselineVersion, indexHash: baselineFiles().get('/index.html'),
  })
  assert.equal(candidate.files.get(result.preservedGameEntry.path), baselineFiles().get('/index.html'))
  assert.equal(fixture.uploaded.has(result.preservedGameEntry.indexHash), false)
  assert.equal(fixture.uploaded.size, 3)
  assert.equal(result.uploadedBlobCount, 3)
  assert.equal(result.scope, 'site')
  assert.equal(result.marker.sourceSha, sourceSha)
  assert.equal(result.marker.target, 'hosting')
  assert.deepEqual(candidate.config.rewrites.slice(0, SITE_GAME_PREFIXES.length),
    SITE_GAME_PREFIXES.map(prefix => ({ glob: prefix + '**', path: result.preservedGameEntry.path })))
  assert.ok(SITE_GAME_PREFIXES.includes('/demo/'))
  assert.deepEqual(candidate.config.rewrites.at(-2), baseConfig().rewrites[0])
  assert.deepEqual(candidate.config.rewrites.at(-1), baseConfig().rewrites[1])
  assert.equal(candidate.config.cleanUrls, true)
  assert.equal(result.state, 'released')
  assert.equal(releaseRequests(fixture).length, 1)
  assert.equal(fixture.records.at(-1).versionName, fixture.candidateName)
  assert.equal(result.manifestSha256, manifestDigest(candidate.files))
  const markerHash = candidate.files.get('/release.json')
  assert.deepEqual(JSON.parse(gunzipSync(fixture.uploaded.get(markerHash))), result.marker)
})

test('successive site releases keep the original game index rather than the previous site index', async () => {
  const original = baselineFiles()
  const marker = {
    schemaVersion: 1, target: 'hosting', scope: 'site', sourceSha: 'b'.repeat(40),
    preservedGameEntry: { path: '/game-shell/original-v0.html',
      sourceVersion: `sites/${site}/versions/original-v0`, indexHash: original.get('/index.html') },
  }
  const files = new Map(original)
  files.set('/index.html', contentHash('previous site HTML, not games'))
  files.set(marker.preservedGameEntry.path, marker.preservedGameEntry.indexHash)
  const fixture = hostingFixture({ files, marker,
    config: siteServingConfig(baseConfig(), marker.preservedGameEntry) })
  const result = await publish(fixture)
  assert.deepEqual(result.preservedGameEntry, marker.preservedGameEntry)
  assert.notEqual(result.preservedGameEntry.indexHash, files.get('/index.html'))
  assert.equal(fixture.uploaded.has(result.preservedGameEntry.indexHash), false)
  assert.equal(fixture.versions.get(fixture.candidateName).config.rewrites.length, SITE_GAME_PREFIXES.length + 2)
  assert.equal(fixture.requests.some(request => request.url.includes('/versions/original-v0/')), false,
    'an expired original version must not prevent a later site-only release')
})

test('artifact namespace, complete locked file set, byte lengths and hashes fail before any REST mutation', async () => {
  for (const change of ['extra', 'missing', 'modified', 'wrong-size', 'wrong-sha', 'game-file']) {
    const fixture = hostingFixture()
    if (change === 'extra') fixture.dependencies.files.set('/assets/site/injected.js', Buffer.from('injected'))
    if (change === 'missing') fixture.dependencies.files.delete('/assets/site/index-hash.js')
    if (change === 'modified') fixture.dependencies.files.set('/index.html', Buffer.from('changed'))
    if (change === 'wrong-size') fixture.options.artifactManifest.files[0].bytes += 1
    if (change === 'wrong-sha') fixture.options.artifactManifest.sourceSha = 'b'.repeat(40)
    if (change === 'game-file') {
      fixture.dependencies.files.set('/games/jade-dragon/new.js', Buffer.from('new game'))
      fixture.options.artifactManifest = artifactManifest(fixture.dependencies.files)
    }
    await assert.rejects(publish(fixture), /artifact|scope/i, change)
    assert.equal(fixture.requests.length, 0, change)
  }
})

test('immutable site asset collisions abort before cloning', async () => {
  const files = baselineFiles()
  files.set('/assets/site/index-hash.js', contentHash('old immutable site module'))
  const fixture = hostingFixture({ files })
  await assert.rejects(publish(fixture), /overwrite an immutable baseline/)
  assert.equal(fixture.requests.some(request => request.method === 'POST'), false)
})

test('missing or tampered preservation markers cannot repin an existing site shell as the game shell', async () => {
  const entry = { path: '/game-shell/full-v1.html', sourceVersion: baselineVersion, indexHash: baselineFiles().get('/index.html') }
  for (const marker of [null, { schemaVersion: 1, target: 'hosting', scope: 'site', preservedGameEntry: { ...entry, indexHash: '0'.repeat(64) } }]) {
    const files = baselineFiles()
    files.set(entry.path, entry.indexHash)
    const fixture = hostingFixture({ files, config: siteServingConfig(baseConfig(), entry), marker })
    await assert.rejects(publish(fixture), /preservation|preserved game entry|own site index/i)
    assert.equal(fixture.requests.some(request => request.method === 'POST'), false)
  }
})

test('changed cloned/finalized game files, mismatched config, unexpected upload hosts and old-blob requests never publish', async () => {
  for (const hooks of [
    { corruptClone: true }, { corruptFinalized: true }, { corruptConfig: true }, { requestOldBlob: true },
    { uploadUrl: 'https://untrusted.invalid/steal-token' },
  ]) {
    const fixture = hostingFixture({ hooks })
    await assert.rejects(publish(fixture), /manifest|locked file|locked config|baseline game blob|upload endpoint/)
    assert.equal(releaseRequests(fixture).length, 0)
    assert.equal(fixture.records.at(-1).state, 'failed')
  }
})

test('a changed live baseline aborts immediately before release creation', async () => {
  const fixture = hostingFixture({ hooks: { race: true } })
  await assert.rejects(publish(fixture), /changed since preflight/)
  assert.equal(releaseRequests(fixture).length, 0)
  assert.equal(fixture.versions.get(fixture.candidateName).status, 'FINALIZED')
  assert.equal(fixture.records.at(-1).state, 'failed')
})

test('an uncertain publish response is recorded and never retried', async () => {
  const fixture = hostingFixture({ hooks: { releaseError: true } })
  await assert.rejects(publish(fixture), /lost release response/)
  assert.equal(releaseRequests(fixture).length, 1)
  assert.equal(fixture.records.at(-1).state, 'publish-unknown')
  assert.equal(fixture.records.at(-1).versionName, fixture.candidateName)
})

test('gzip hashes, config immutability and full manifest assertions use exact contents', () => {
  const files = siteFiles()
  const compressed = compressSiteFiles(files)
  assert.equal(compressed.get('/index.html').hash, contentHash(files.get('/index.html')))
  assert.deepEqual(gunzipSync(compressed.get('/index.html').bytes), files.get('/index.html'))
  const config = baseConfig()
  const snapshot = structuredClone(config)
  siteServingConfig(config, { path: '/game-shell/example.html' })
  assert.deepEqual(config, snapshot)
  assert.throws(() => assertExactManifest(new Map(), new Map([['/game.js', 'a'.repeat(64)]])), /file count/)
  assert.throws(() => assertArtifactManifest(files, artifactManifest(new Map()), sourceSha), /file set/)
})

test('artifact reader preserves bytes and exposes injected files to complete manifest validation', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'fortune-site-artifact-'))
  try {
    await mkdir(join(directory, 'assets', 'site'), { recursive: true })
    const files = siteFiles()
    for (const [path, bytes] of files) await writeFile(join(directory, path.slice(1)), bytes)
    const read = await readSiteArtifact(directory)
    assert.deepEqual(read, files)
    assert.doesNotThrow(() => assertArtifactManifest(read, artifactManifest(files), sourceSha))
    await writeFile(join(directory, 'assets', 'site', 'injected.js'), 'unexpected')
    assert.throws(() => assertArtifactManifest(new Map([...read, ['/assets/site/injected.js', Buffer.from('unexpected')]]),
      artifactManifest(files), sourceSha), /file set/)
  } finally {
    assert.equal(dirname(await realpath(directory)), await realpath(tmpdir()))
    assert.ok(basename(directory).startsWith('fortune-site-artifact-'))
    await rm(directory, { recursive: true, force: true })
  }
})
