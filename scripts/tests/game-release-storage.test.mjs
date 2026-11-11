import assert from 'node:assert/strict'
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, relative, sep } from 'node:path'
import test from 'node:test'
import { createGameReleaseStorage } from '../game-release-storage.mjs'

test('stages immutable browser assets and protects the live pointer with its exact generation', async t => {
  const temp = await realpath(tmpdir())
  const repository = await mkdtemp(join(temp, 'ff-game-storage-test-'))
  t.after(async () => {
    const fixture = relative(temp, await realpath(repository))
    assert.ok(fixture.startsWith('ff-game-storage-test-') && !fixture.includes(sep))
    await rm(repository, { recursive: true, force: true })
  })

  const gameId = 'fixture-game'
  const firstId = '00000000-0000-0000-0000-000000000001'
  const nextId = '00000000-0000-0000-0000-000000000002'
  const liveBackendId = '00000000-0000-0000-0000-000000000003'
  const activeName = 'games/' + gameId + '/active.json'
  const prefix = releaseId => 'games/' + gameId + '/releases/' + releaseId
  const objects = new Map()
  const writes = []
  let generation = 9_007_199_254_740_993n
  let concurrentRelease

  function save(name, metadata, body) {
    const stored = { metadata: { ...metadata, name, generation: String(generation++), size: String(body.length) }, body }
    objects.set(name, stored)
    return stored
  }

  async function fakeFetch(url, { method, headers, body, redirect }) {
    assert.equal(url.origin, 'https://storage.googleapis.com')
    assert.equal(headers.Authorization, 'Bearer fixture-token')
    assert.equal(redirect, 'error')
    const match = url.searchParams.get('ifGenerationMatch')
    if (method === 'POST') {
      assert.equal(url.pathname, '/upload/storage/v1/b/fixture-bucket/o')
      assert.equal(url.searchParams.get('uploadType'), 'multipart')
      const boundary = headers['Content-Type'].split('boundary=')[1]
      const [, descriptor, media] = body.toString().split('--' + boundary)
      const metadata = JSON.parse(descriptor.split('\r\n\r\n')[1])
      const content = Buffer.from(media.slice(media.indexOf('\r\n\r\n') + 4, -2))
      writes.push({ name: metadata.name, expected: match })
      if (metadata.name === activeName && concurrentRelease) {
        const current = objects.get(activeName)
        save(activeName, current.metadata, Buffer.from(JSON.stringify(concurrentRelease)))
        concurrentRelease = undefined
      }
      if (match !== (objects.get(metadata.name)?.metadata.generation ?? '0')) {
        return new Response(null, { status: 412 })
      }
      return Response.json(save(metadata.name, metadata, content).metadata)
    }
    const name = decodeURIComponent(url.pathname.slice('/storage/v1/b/fixture-bucket/o/'.length))
    const stored = objects.get(name)
    if (!stored) return new Response(null, { status: 404 })
    if (url.searchParams.get('alt') !== 'media') return Response.json(stored.metadata)
    if (match !== stored.metadata.generation) return new Response(null, { status: 412 })
    return new Response(stored.body)
  }

  const storage = createGameReleaseStorage({ bucket: 'fixture-bucket', accessToken: 'fixture-token', fetchImpl: fakeFetch })
  const stage = async releaseId => {
    const dir = join(repository, 'artifacts', 'game-releases', prefix(releaseId))
    await mkdir(join(dir, 'assets'), { recursive: true })
    await writeFile(join(dir, 'assets', 'entry.js'), 'export default function Game() {}\n')
    await writeFile(join(dir, 'assets', 'theme.css'), '.game { display: grid }\n')
    await writeFile(join(dir, 'release.json'), JSON.stringify({
      gameId, releaseId, backendReleaseId: releaseId, contractVersion: 1,
      entry: 'assets/entry.js', styles: ['assets/theme.css'],
    }))
    return storage.stageBrowserRelease({ repository, gameId, releaseId })
  }

  const first = await stage(firstId)
  assert.equal(first.objectCount, 3)
  assert.equal(first.release.backendReleaseId, undefined)
  assert.deepEqual(writes.slice(0, 3).map(write => write.expected), ['0', '0', '0'])
  assert.equal(writes[2].name, prefix(firstId) + '/release.json')
  assert.deepEqual(await storage.readActive(gameId), { generation: '0', release: null })
  const originalAsset = objects.get(prefix(firstId) + '/assets/entry.js')
  await stage(firstId)
  assert.equal(objects.get(prefix(firstId) + '/assets/entry.js'), originalAsset)
  await assert.rejects(storage.promoteBrowserRelease({ gameId, releaseId: firstId, expectedGeneration: 0 }), /decimal string/)
  const initial = await storage.promoteBrowserRelease({ gameId, releaseId: firstId, expectedGeneration: '0' })
  assert.equal(objects.get(activeName).metadata.cacheControl, 'no-store')
  assert.equal(initial.release.backendReleaseId, undefined)

  const live = { ...initial.release, backendReleaseId: liveBackendId }
  const liveObject = save(activeName, objects.get(activeName).metadata, Buffer.from(JSON.stringify(live)))
  await stage(nextId)
  const next = await storage.promoteBrowserRelease({ gameId, releaseId: nextId, expectedGeneration: liveObject.metadata.generation })
  assert.equal(next.release.backendReleaseId, liveBackendId)
  assert.deepEqual(writes.at(-1), { name: activeName, expected: liveObject.metadata.generation })

  concurrentRelease = live
  await assert.rejects(storage.promoteBrowserRelease({ gameId, releaseId: nextId, expectedGeneration: next.generation }), /generation changed/)
  assert.deepEqual(writes.at(-1), { name: activeName, expected: next.generation })
  assert.deepEqual((await storage.readActive(gameId)).release, live)
})
