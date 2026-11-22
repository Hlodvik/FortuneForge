import { createHash, randomUUID } from 'node:crypto'
import { lstat, mkdir, open, readFile, readdir, realpath, rename, rm, unlink, writeFile } from 'node:fs/promises'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { isGameId, isReleaseId } from './game-release-activation.mjs'

const repository = fileURLToPath(new URL('../', import.meta.url))
const hash = bytes => createHash('sha256').update(bytes).digest('hex')
const serialize = value => JSON.stringify(value, null, 2) + '\n'
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value)
const sourceHash = /^[a-f0-9]{40}$/
const fileHash = /^[a-f0-9]{64}$/
const moduleName = /^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+$/
const compare = (left, right) => left < right ? -1 : left > right ? 1 : 0
const defaultManifest = join(repository, 'config', 'backend-packages.lock.json')
const defaultStore = join(repository, 'artifacts', 'game-engine-releases')

function keys(value, expected, description) {
  if (!record(value) || Object.keys(value).sort().join(',') !== [...expected].sort().join(','))
    throw new Error(`Invalid ${description} fields`)
}

function artifactPath(path) {
  if (typeof path !== 'string' || path.length > 2048 || !path.split('/').every(segment =>
    /^[A-Za-z0-9_-][A-Za-z0-9_.-]*$/.test(segment) && !segment.endsWith('.') &&
    !/^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(segment)))
    throw new Error('Invalid package artifact path')
  return path
}

export function backendDescriptor(value) {
  keys(value, ['contractVersion', 'gameId', 'runtimeGameId', 'releaseId', 'sourceSha', 'entry', 'moduleType'], 'backend descriptor')
  if (value.contractVersion !== 1 || !isGameId(value.gameId) || !isGameId(value.runtimeGameId) ||
    !isReleaseId(value.releaseId) || !sourceHash.test(value.sourceSha ?? '') ||
    typeof value.moduleType !== 'string' || value.moduleType.length > 512 || !moduleName.test(value.moduleType) ||
    typeof value.entry !== 'string' || !/^[A-Za-z0-9_-][A-Za-z0-9_.-]*\.dll$/.test(value.entry))
    throw new Error('Invalid backend package descriptor')
  artifactPath(value.entry)
  return { contractVersion: 1, gameId: value.gameId, runtimeGameId: value.runtimeGameId,
    releaseId: value.releaseId, sourceSha: value.sourceSha, entry: value.entry, moduleType: value.moduleType }
}

export function describeBackendRelease(description, { gameId, releaseId, entry, sourceSha }) {
  keys(description, ['contractVersion', 'runtimeGameId', 'moduleType'], 'backend module description')
  return backendDescriptor({ ...description, gameId, releaseId, entry, sourceSha })
}

function packageSelection(value) {
  keys(value, ['gameId', 'runtimeGameId', 'releaseId', 'sourceSha', 'entry', 'moduleType', 'files'], 'package selection')
  const { contractVersion, ...identity } = backendDescriptor({ contractVersion: 1,
    ...Object.fromEntries(Object.entries(value).filter(([name]) => name !== 'files')) })
  if (!Array.isArray(value.files) || value.files.length === 0) throw new Error('Package selection requires its complete file inventory')
  const seen = new Set()
  const files = value.files.map(file => {
    keys(file, ['path', 'size', 'sha256'], 'package file')
    artifactPath(file.path)
    if (!Number.isSafeInteger(file.size) || file.size < 0 || !fileHash.test(file.sha256 ?? ''))
      throw new Error('Invalid package file size or SHA256')
    const portable = file.path.toLowerCase()
    if (seen.has(portable)) throw new Error('Duplicate or case-colliding package file path')
    seen.add(portable)
    return { path: file.path, size: file.size, sha256: file.sha256 }
  }).sort((left, right) => compare(left.path, right.path))
  const assembly = value.entry.slice(0, -4)
  for (const required of ['release.json', value.entry, assembly + '.deps.json', assembly + '.runtimeconfig.json']) {
    if (!files.some(file => file.path === required)) throw new Error('Missing required package artifact: ' + required)
  }
  return { ...identity, files }
}

export function backendComposition(value) {
  keys(value, ['contractVersion', 'packages'], 'backend composition')
  if (value.contractVersion !== 1 || !Array.isArray(value.packages)) throw new Error('Invalid backend composition contract')
  const games = new Set(), runtimes = new Set(), releases = new Set()
  const packages = value.packages.map(packageSelection).sort((left, right) => compare(left.gameId, right.gameId))
  for (const item of packages) {
    if (games.has(item.gameId) || runtimes.has(item.runtimeGameId) || releases.has(item.releaseId))
      throw new Error('Duplicate game, runtime game, or release in backend composition')
    games.add(item.gameId); runtimes.add(item.runtimeGameId); releases.add(item.releaseId)
  }
  return { contractVersion: 1, packages }
}

async function assertDirect(path, directory) {
  const absolute = resolve(path)
  if (relative(absolute, await realpath(absolute))) throw new Error('Package paths cannot use symlinks or redirected ancestors')
  const info = await lstat(absolute)
  if (info.isSymbolicLink() || (directory ? !info.isDirectory() : !info.isFile()))
    throw new Error('Unexpected package artifact type: ' + absolute)
}

function packageDirectory(storeRoot, gameId, releaseId) {
  if (!isGameId(gameId) || !isReleaseId(releaseId)) throw new Error('Invalid game or release ID')
  return join(resolve(storeRoot), 'games', gameId, 'releases', releaseId)
}

async function inventory(directory) {
  await assertDirect(directory, true)
  const files = [], seen = new Set()
  async function visit(current, prefix = '') {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = artifactPath(prefix + entry.name)
      const portable = path.toLowerCase()
      if (seen.has(portable)) throw new Error('Case-colliding package artifact path')
      seen.add(portable)
      const absolute = join(current, entry.name)
      await assertDirect(absolute, entry.isDirectory())
      if (entry.isDirectory()) await visit(absolute, path + '/')
      else {
        const bytes = await readFile(absolute)
        files.push({ path, size: bytes.length, sha256: hash(bytes) })
      }
    }
  }
  await visit(directory)
  return files.sort((left, right) => compare(left.path, right.path))
}

/** Capture an already-published local package; this does not build or publish code. */
export async function stageBackendPackage({ storeRoot = defaultStore, gameId, releaseId }) {
  const directory = packageDirectory(storeRoot, gameId, releaseId)
  const files = await inventory(directory)
  const descriptorBytes = await readFile(join(directory, 'release.json'))
  if (hash(descriptorBytes) !== files.find(file => file.path === 'release.json')?.sha256)
    throw new Error('Backend descriptor changed while reading the package')
  const descriptor = backendDescriptor(JSON.parse(descriptorBytes.toString('utf8')))
  if (descriptor.gameId !== gameId || descriptor.releaseId !== releaseId)
    throw new Error('Backend descriptor does not match its game and release directory')
  const assembly = descriptor.entry.slice(0, -4)
  const runtime = JSON.parse(await readFile(join(directory, assembly + '.runtimeconfig.json'), 'utf8'))
  const dependencies = JSON.parse(await readFile(join(directory, assembly + '.deps.json'), 'utf8'))
  if (runtime.runtimeOptions?.tfm !== 'net10.0' ||
    !/^\.NETCoreApp,Version=v10\.0(?:\/linux-x64)?$/.test(dependencies.runtimeTarget?.name ?? ''))
    throw new Error('Backend package requires portable or linux-x64 .NET 10 artifacts')
  const { contractVersion, ...identity } = descriptor
  const selection = packageSelection({ ...identity, files })
  if (serialize(await inventory(directory)) !== serialize(files)) throw new Error('Backend package changed while being captured')
  return selection
}

async function readManifest(path, optional = false) {
  try {
    await assertDirect(path, false)
    const bytes = await readFile(path)
    return { bytes, composition: backendComposition(JSON.parse(bytes.toString('utf8'))) }
  } catch (error) {
    if (optional && error.code === 'ENOENT') return { bytes: null, composition: { contractVersion: 1, packages: [] } }
    throw error
  }
}

async function assertManifestUnchanged(path, bytes) {
  const latest = await readManifest(path, bytes === null)
  if (bytes === null ? latest.bytes !== null : !bytes.equals(latest.bytes))
    throw new Error('Backend composition changed during the operation')
}

export async function pinBackendPackage({ manifestPath = defaultManifest, storeRoot = defaultStore, gameId, releaseId }) {
  manifestPath = resolve(manifestPath)
  await mkdir(dirname(manifestPath), { recursive: true })
  await assertDirect(dirname(manifestPath), true)
  const lockPath = manifestPath + '.lock'
  const lock = await open(lockPath, 'wx')
  const pending = manifestPath + '.' + randomUUID() + '.tmp'
  try {
    const { bytes, composition } = await readManifest(manifestPath, true)
    const selected = await stageBackendPackage({ storeRoot, gameId, releaseId })
    const previous = composition.packages.find(item => item.gameId === gameId)
    if (previous?.releaseId === releaseId && serialize(previous) !== serialize(selected))
      throw new Error('An immutable selected release has changed; stage a new release ID')
    const updated = backendComposition({ contractVersion: 1,
      packages: [...composition.packages.filter(item => item.gameId !== gameId), selected] })
    await writeFile(pending, serialize(updated), { flag: 'wx' })
    await assertManifestUnchanged(manifestPath, bytes)
    await rename(pending, manifestPath)
    return updated
  } finally {
    try { await unlink(pending).catch(error => { if (error.code !== 'ENOENT') throw error }) }
    finally {
      try { await lock.close() }
      finally { await unlink(lockPath) }
    }
  }
}

export async function verifyBackendComposition({ manifestPath = defaultManifest, storeRoot = defaultStore } = {}) {
  const { bytes, composition } = await readManifest(resolve(manifestPath))
  if (composition.packages.length === 0) throw new Error('Backend composition cannot be empty for verification or materialization')
  for (const expected of composition.packages) {
    const actual = await stageBackendPackage({ storeRoot, gameId: expected.gameId, releaseId: expected.releaseId })
    if (serialize(actual) !== serialize(expected)) throw new Error('Backend package differs from its immutable selection: ' + expected.gameId)
  }
  await assertManifestUnchanged(resolve(manifestPath), bytes)
  return { composition, manifestSha256: hash(bytes), compositionSha256: hash(serialize(composition)) }
}

export async function verifyMaterializedBackendComposition({ manifestPath = defaultManifest, output }) {
  if (typeof output !== 'string' || !output) throw new Error('Verification requires an output publish directory')
  output = resolve(output)
  await assertDirect(output, true)
  const directory = join(output, 'game-packages')
  await assertDirect(directory, true)
  const { bytes, composition } = await readManifest(resolve(manifestPath))
  if (composition.packages.length === 0) throw new Error('Backend composition cannot be empty for verification')
  const canonical = Buffer.from(serialize(composition))
  const files = [{ path: 'composition.json', size: canonical.length, sha256: hash(canonical) }]
  for (const selected of composition.packages) {
    const prefix = selected.runtimeGameId + '/' + selected.releaseId + '/'
    files.push(...selected.files.map(file => ({ ...file, path: prefix + file.path })))
  }
  files.sort((left, right) => compare(left.path, right.path))
  if (serialize(await inventory(directory)) !== serialize(files))
    throw new Error('Materialized backend packages differ from the selected composition')
  for (const selected of composition.packages) {
    const descriptorBytes = await readFile(join(directory, selected.runtimeGameId, selected.releaseId, 'release.json'))
    if (hash(descriptorBytes) !== selected.files.find(file => file.path === 'release.json').sha256)
      throw new Error('Materialized backend descriptor changed during verification')
    const descriptor = backendDescriptor(JSON.parse(descriptorBytes.toString('utf8')))
    const { contractVersion, ...identity } = descriptor
    const { files: ignored, ...expected } = selected
    if (serialize(identity) !== serialize(expected)) throw new Error('Materialized backend descriptor differs from its selected identity')
  }
  await assertManifestUnchanged(resolve(manifestPath), bytes)
  return { composition, manifestSha256: hash(bytes), compositionSha256: hash(canonical), directory }
}

export async function materializeBackendComposition({ manifestPath = defaultManifest, storeRoot = defaultStore, output }) {
  if (typeof output !== 'string' || !output) throw new Error('Materialization requires an output publish directory')
  output = resolve(output)
  await assertDirect(output, true)
  const destination = join(output, 'game-packages')
  try { await lstat(destination); throw new Error('Refusing to replace an existing game-packages directory') }
  catch (error) { if (error.code !== 'ENOENT') throw error }
  const { bytes, composition } = await readManifest(resolve(manifestPath))
  if (composition.packages.length === 0) throw new Error('Backend composition cannot be empty for materialization')
  const temporary = join(output, '.game-packages-' + randomUUID())
  await mkdir(temporary)
  try {
    for (const expected of composition.packages) {
      const source = packageDirectory(storeRoot, expected.gameId, expected.releaseId)
      const actual = await stageBackendPackage({ storeRoot, gameId: expected.gameId, releaseId: expected.releaseId })
      if (serialize(actual) !== serialize(expected)) throw new Error('Backend package differs from its immutable selection: ' + expected.gameId)
      const target = join(temporary, expected.runtimeGameId, expected.releaseId)
      for (const file of expected.files) {
        const path = join(source, ...file.path.split('/'))
        await assertDirect(path, false)
        const content = await readFile(path)
        if (content.length !== file.size || hash(content) !== file.sha256) throw new Error('Backend artifact changed during materialization: ' + file.path)
        const selected = join(target, ...file.path.split('/'))
        await mkdir(dirname(selected), { recursive: true })
        await writeFile(selected, content, { flag: 'wx' })
      }
      if (serialize(await inventory(source)) !== serialize(expected.files) || serialize(await inventory(target)) !== serialize(expected.files))
        throw new Error('Backend package tree changed during materialization: ' + expected.gameId)
    }
    await writeFile(join(temporary, 'composition.json'), serialize(composition), { flag: 'wx' })
    await assertManifestUnchanged(resolve(manifestPath), bytes)
    await rename(temporary, destination)
    return { composition, manifestSha256: hash(bytes), compositionSha256: hash(serialize(composition)), directory: destination }
  } finally {
    // Only this unpredictable, validated descendant belongs to this operation.
    if (dirname(temporary) !== output || !basename(temporary).startsWith('.game-packages-'))
      throw new Error('Invalid temporary package directory')
    try {
      await assertDirect(temporary, true)
      await rm(temporary, { recursive: true, force: true })
    } catch (error) { if (error.code !== 'ENOENT') throw error }
  }
}

async function main() {
  const { values, positionals } = parseArgs({ allowPositionals: true,
    options: { manifest: { type: 'string' }, store: { type: 'string' }, output: { type: 'string' } } })
  const [command, gameId, releaseId] = positionals
  const options = { manifestPath: values.manifest, storeRoot: values.store }
  let result
  if (command === 'pin' && positionals.length === 3 && values.output === undefined)
    result = { composition: await pinBackendPackage({ ...options, gameId, releaseId }) }
  else if (command === 'verify' && positionals.length === 1 && values.output === undefined)
    result = await verifyBackendComposition(options)
  else if (command === 'materialize' && positionals.length === 1 && values.output !== undefined)
    result = await materializeBackendComposition({ ...options, output: values.output })
  else if (command === 'verify-materialized' && positionals.length === 1 && values.output !== undefined)
    result = await verifyMaterializedBackendComposition({ ...options, output: values.output })
  else throw new Error('Usage: shared-backend-packages.mjs pin <gameId> <releaseId> [--manifest path] [--store path] | verify [--manifest path] [--store path] | materialize --output <publish-root> [--manifest path] [--store path] | verify-materialized --output <publish-root> [--manifest path]')
  console.log(JSON.stringify({ command, packages: result.composition.packages.length,
    ...(result.manifestSha256 ? { manifestSha256: result.manifestSha256 } : {}),
    ...(result.compositionSha256 ? { compositionSha256: result.compositionSha256 } : {}),
    ...(result.directory ? { directory: result.directory } : {}) }, null, 2))
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  await main()
