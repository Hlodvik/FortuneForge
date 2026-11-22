import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { mkdir, mkdtemp, readFile, readdir, realpath, rm, symlink, unlink, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { backendComposition, backendDescriptor, describeBackendRelease, materializeBackendComposition,
  pinBackendPackage, stageBackendPackage, verifyBackendComposition, verifyMaterializedBackendComposition } from '../shared-backend-packages.mjs'

const first = '00000000-0000-0000-0000-000000000001'
const second = '00000000-0000-0000-0000-000000000002'
const third = '00000000-0000-0000-0000-000000000003'
const assembly = 'FortuneForge.Games.PackagedGame.Server'
const entry = assembly + '.dll'
const moduleType = assembly + '.GameBackendPackage'
const sourceSha = 'a'.repeat(40)
const hash = bytes => createHash('sha256').update(bytes).digest('hex')

async function fixture(t) {
  const temporaryRoot = await realpath(tmpdir())
  const root = await mkdtemp(join(temporaryRoot, 'ff-shared-packages-'))
  t.after(async () => {
    const child = relative(temporaryRoot, await realpath(root))
    assert.ok(child.startsWith('ff-shared-packages-') && !child.includes(sep))
    await rm(root, { recursive: true, force: true })
  })
  const storeRoot = join(root, 'store')
  const manifestPath = join(root, 'config', 'backend-packages.lock.json')
  const output = join(root, 'publish')
  await mkdir(output)
  async function publish(gameId, releaseId, runtimeGameId = gameId, extra = {}) {
    const folder = join(storeRoot, 'games', gameId, 'releases', releaseId)
    await mkdir(join(folder, 'runtimes', 'linux-x64', 'native'), { recursive: true })
    const descriptor = backendDescriptor({ contractVersion: 1, gameId, runtimeGameId, releaseId,
      sourceSha, entry, moduleType, ...extra })
    await Promise.all([
      writeFile(join(folder, 'release.json'), JSON.stringify(descriptor)),
      writeFile(join(folder, entry), `assembly:${gameId}:${releaseId}`),
      writeFile(join(folder, assembly + '.deps.json'), JSON.stringify({ runtimeTarget: { name: '.NETCoreApp,Version=v10.0' } })),
      writeFile(join(folder, assembly + '.runtimeconfig.json'), JSON.stringify({ runtimeOptions: { tfm: 'net10.0' } })),
      writeFile(join(folder, 'runtimes', 'linux-x64', 'native', 'libgrpc.so'), Buffer.from([0, 255, 12, 4])),
    ])
    return folder
  }
  return { root, storeRoot, manifestPath, output, publish }
}

test('descriptor identity comes from module metadata and preserves release/runtime aliases', () => {
  const descriptor = describeBackendRelease({ contractVersion: 1, runtimeGameId: 'empty-slots-v1', moduleType },
    { gameId: 'jade-dragon', releaseId: first, sourceSha, entry })
  assert.equal(descriptor.gameId, 'jade-dragon')
  assert.equal(descriptor.runtimeGameId, 'empty-slots-v1')
  assert.throws(() => describeBackendRelease({ contractVersion: 1, runtimeGameId: 'empty-slots-v1', moduleType,
    gameId: 'spoofed' }, descriptor), /description fields/)
  assert.throws(() => backendDescriptor({ ...descriptor, sourceSha: 'dirty' }), /descriptor/)
  assert.throws(() => backendDescriptor({ ...descriptor, contractVersion: 2 }), /descriptor/)
})

test('capture inventories the complete portable package including native dependencies and descriptor', async t => {
  const context = await fixture(t)
  await context.publish('jade-dragon', first, 'empty-slots-v1')
  const selected = await stageBackendPackage({ ...context, gameId: 'jade-dragon', releaseId: first })
  assert.equal(selected.files.length, 5)
  assert.deepEqual(selected.files.map(file => file.path), [...selected.files.map(file => file.path)].sort())
  const native = selected.files.find(file => file.path === 'runtimes/linux-x64/native/libgrpc.so')
  assert.deepEqual(native, { path: 'runtimes/linux-x64/native/libgrpc.so', size: 4, sha256: hash(Buffer.from([0, 255, 12, 4])) })
  assert.equal(selected.runtimeGameId, 'empty-slots-v1')
})

test('pinning one new release leaves every other selected package and materialized byte unchanged', async t => {
  const context = await fixture(t)
  await context.publish('jade-dragon', first, 'empty-slots-v1')
  const siblingFolder = await context.publish('blackjack', second)
  await pinBackendPackage({ ...context, gameId: 'jade-dragon', releaseId: first })
  const before = await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: second })
  const sibling = before.packages.find(item => item.gameId === 'blackjack')
  await context.publish('jade-dragon', third, 'empty-slots-v1', { sourceSha: 'b'.repeat(40) })
  const after = await pinBackendPackage({ ...context, gameId: 'jade-dragon', releaseId: third })
  assert.deepEqual(after.packages.find(item => item.gameId === 'blackjack'), sibling)
  assert.equal(after.packages.find(item => item.gameId === 'jade-dragon').sourceSha, 'b'.repeat(40))
  await verifyBackendComposition(context)
  await materializeBackendComposition(context)
  const copied = join(context.output, 'game-packages', 'blackjack', second)
  for (const file of sibling.files)
    assert.deepEqual(await readFile(join(copied, file.path)), await readFile(join(siblingFolder, file.path)))
  assert.deepEqual(JSON.parse(await readFile(join(context.output, 'game-packages', 'composition.json'), 'utf8')), after)
  assert.equal((await readdir(join(context.output, 'game-packages', 'empty-slots-v1'))).length, 1)
})

for (const corruption of ['same-size content', 'missing file', 'extra file']) {
  test('verification rejects ' + corruption + ' against the immutable file inventory', async t => {
    const context = await fixture(t)
    const folder = await context.publish('blackjack', first)
    await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
    if (corruption === 'same-size content') {
      const bytes = await readFile(join(folder, entry)); bytes[0] ^= 1
      await writeFile(join(folder, entry), bytes)
    } else if (corruption === 'missing file') await unlink(join(folder, 'runtimes/linux-x64/native/libgrpc.so'))
    else await writeFile(join(folder, 'extra.dll'), 'unexpected')
    await assert.rejects(verifyBackendComposition(context), /differs/)
    await assert.rejects(materializeBackendComposition(context), /differs/)
    assert.deepEqual(await readdir(context.output), [])
  })
}

test('an already selected release UUID cannot be repinned after its bytes change', async t => {
  const context = await fixture(t)
  const folder = await context.publish('blackjack', first)
  await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
  const original = await readFile(context.manifestPath)
  await writeFile(join(folder, entry), 'different bytes')
  await assert.rejects(pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first }), /immutable selected release/)
  assert.deepEqual(await readFile(context.manifestPath), original)
  assert.deepEqual(await readdir(dirname(context.manifestPath)), ['backend-packages.lock.json'])
})

test('composition rejects traversal, nonportable paths, duplicate runtimes and unsupported fields', async t => {
  const context = await fixture(t)
  await context.publish('blackjack', first)
  const selected = await stageBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
  for (const path of ['../outside.dll', 'C:/outside.dll', '/outside.dll', 'a\\outside.dll', 'a/./file.dll', 'a/con.dll', 'a/file.']) {
    assert.throws(() => backendComposition({ contractVersion: 1, packages: [
      { ...selected, files: [...selected.files, { path, size: 0, sha256: 'b'.repeat(64) }] },
    ] }), /artifact path/)
  }
  assert.throws(() => backendComposition({ contractVersion: 1, packages: [selected,
    { ...selected, gameId: 'another-game', releaseId: second }] }), /Duplicate/)
  assert.throws(() => backendComposition({ contractVersion: 1, packages: [
    { ...selected, files: [...selected.files, { ...selected.files[0], path: selected.files[0].path.toUpperCase() }] },
  ] }), /case-colliding/)
  assert.throws(() => backendComposition({ contractVersion: 1, packages: [selected], copyAll: true }), /composition fields/)
})

test('package directory identity and runtime framework must match their descriptor', async t => {
  const context = await fixture(t)
  const folder = await context.publish('blackjack', first)
  const descriptorPath = join(folder, 'release.json')
  const original = JSON.parse(await readFile(descriptorPath, 'utf8'))
  await writeFile(descriptorPath, JSON.stringify({ ...original, gameId: 'roulette' }))
  await assert.rejects(stageBackendPackage({ ...context, gameId: 'blackjack', releaseId: first }), /does not match/)
  await writeFile(descriptorPath, JSON.stringify(original))
  await writeFile(join(folder, assembly + '.runtimeconfig.json'), JSON.stringify({ runtimeOptions: { tfm: 'net9.0' } }))
  await assert.rejects(stageBackendPackage({ ...context, gameId: 'blackjack', releaseId: first }), /\.NET 10/)
})

test('redirected package trees are rejected instead of copying files outside their store', async t => {
  const context = await fixture(t)
  const folder = await context.publish('blackjack', first)
  const outside = join(context.root, 'outside')
  await mkdir(outside)
  await writeFile(join(outside, 'private.dll'), 'must not copy')
  await symlink(outside, join(folder, 'redirect'), process.platform === 'win32' ? 'junction' : 'dir')
  await assert.rejects(stageBackendPackage({ ...context, gameId: 'blackjack', releaseId: first }), /symlinks|redirected/)
})

test('materialization refuses to replace an existing package directory', async t => {
  const context = await fixture(t)
  await context.publish('blackjack', first)
  await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
  await mkdir(join(context.output, 'game-packages'))
  await writeFile(join(context.output, 'game-packages', 'sentinel'), 'keep')
  await assert.rejects(materializeBackendComposition(context), /Refusing to replace/)
  assert.equal(await readFile(join(context.output, 'game-packages', 'sentinel'), 'utf8'), 'keep')
})

test('existing materialized packages verify without the source store and reject stale lock selections', async t => {
  const context = await fixture(t)
  await context.publish('blackjack', first)
  await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
  const materialized = await materializeBackendComposition(context)
  assert.equal(dirname(context.storeRoot), context.root)
  assert.equal(await realpath(context.storeRoot), resolve(context.storeRoot))
  await rm(context.storeRoot, { recursive: true })
  const verified = await verifyMaterializedBackendComposition(context)
  assert.equal(verified.compositionSha256, materialized.compositionSha256)
  assert.equal(verified.manifestSha256, materialized.manifestSha256)
  const changed = JSON.parse(await readFile(context.manifestPath, 'utf8'))
  changed.packages[0].files[0].sha256 = '0'.repeat(64)
  await writeFile(context.manifestPath, JSON.stringify(changed))
  await assert.rejects(verifyMaterializedBackendComposition(context), /differ/)
})

for (const corruption of ['noncanonical composition', 'unexpected file', 'changed binary']) {
  test('materialized verification rejects ' + corruption, async t => {
    const context = await fixture(t)
    await context.publish('blackjack', first)
    await pinBackendPackage({ ...context, gameId: 'blackjack', releaseId: first })
    const { directory } = await materializeBackendComposition(context)
    if (corruption === 'noncanonical composition') {
      const compositionPath = join(directory, 'composition.json')
      await writeFile(compositionPath, (await readFile(compositionPath, 'utf8')) + '\n')
    } else if (corruption === 'unexpected file') await writeFile(join(directory, 'unexpected.dll'), 'extra')
    else await writeFile(join(directory, 'blackjack', first, entry), 'modified')
    await assert.rejects(verifyMaterializedBackendComposition(context), /differ/)
  })
}

test('CLI pins, verifies and materializes using the same exported contract', async t => {
  const context = await fixture(t)
  await context.publish('jade-dragon', first, 'empty-slots-v1')
  const script = fileURLToPath(new URL('../shared-backend-packages.mjs', import.meta.url))
  function run(args) {
    const result = spawnSync(process.execPath, [script, ...args, '--manifest', context.manifestPath, '--store', context.storeRoot], { encoding: 'utf8' })
    assert.equal(result.status, 0, result.stderr)
    return JSON.parse(result.stdout)
  }
  assert.equal(run(['pin', 'jade-dragon', first]).packages, 1)
  assert.match(run(['verify']).manifestSha256, /^[a-f0-9]{64}$/)
  assert.equal(run(['materialize', '--output', context.output]).directory, resolve(context.output, 'game-packages'))
  assert.match(run(['verify-materialized', '--output', context.output]).compositionSha256, /^[a-f0-9]{64}$/)
})
