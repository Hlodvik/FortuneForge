import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, readdir, realpath, rm, writeFile } from 'node:fs/promises'
import { writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { publishGameWorker } from '../game-worker-release.mjs'

const releaseId = '00000000-0000-0000-0000-000000000001'
const digest = 'sha256:' + 'a'.repeat(64)
const runtimeAccount = 'worker@example-project.iam.gserviceaccount.com'
const callerAccount = 'host@example-project.iam.gserviceaccount.com'
const connections = {
  environment: { GameWallet__Url: 'https://wallet.example.test', ManagedPlayerRoster__Url: 'https://roster.example.test' },
  secrets: { GameWallet__SigningKey: 'wallet-key:1', GameWallet__BatchSigningKey: 'batch-key:2', ManagedPlayerRoster__SigningKey: 'roster-key:3' },
}

async function fixture(t, { gameId = 'hearts', document = connections, existing = false, alterRevision, afterBuild } = {}) {
  const temp = await realpath(tmpdir())
  const repository = await mkdtemp(join(temp, 'ff-worker-publish-test-'))
  t.after(async () => {
    const path = relative(temp, await realpath(repository))
    assert.ok(path.startsWith('ff-worker-publish-test-') && !path.includes(sep))
    await rm(repository, { recursive: true, force: true })
  })
  const backendRoot = join(repository, 'artifacts', 'game-engine-releases', 'games', gameId, 'releases')
  const source = join(backendRoot, releaseId)
  await mkdir(source, { recursive: true })
  const assembly = 'FortuneForge.Games.PackagedGame.Server'
  await Promise.all([
    writeFile(join(source, 'release.json'), JSON.stringify({ gameId, releaseId, entry: assembly + '.dll' })),
    writeFile(join(source, assembly + '.dll'), 'fixture assembly'),
    writeFile(join(source, assembly + '.runtimeconfig.json'), JSON.stringify({ runtimeOptions: { tfm: 'net10.0' } })),
    writeFile(join(source, assembly + '.deps.json'), JSON.stringify({ runtimeTarget: { name: '.NETCoreApp,Version=v10.0' } })),
  ])
  const settingsPath = join(repository, 'worker-settings.json')
  await writeFile(settingsPath, JSON.stringify(document))
  const options = {
    'publish-worker': releaseId, 'worker-project': 'example-project', 'worker-region': 'us-east4',
    'worker-repository': 'workers', 'worker-service-account': runtimeAccount,
    'worker-caller-service-account': callerAccount, 'worker-secret': 'worker-key:4',
    'worker-service': 'fortuneforge-game-' + gameId + '-v2', 'worker-settings': settingsPath,
    ...(existing ? { 'activate-worker': true } : { 'enroll-worker': true }),
  }
  const expectedEnvironment = { GameWorker__Mode: 'hosted', GoogleCloud__ProjectId: 'example-project',
    ...(gameId === 'hearts' ? { Features__HeartsEnabled: 'true' } : {}),
    ...Object.fromEntries(Object.entries(document.environment ?? {}).map(([name, value]) => [name, String(value)])) }
  const variables = [
    ...Object.entries(expectedEnvironment).map(([name, value]) => ({ name, value })),
    ...Object.entries({ GameWorker__SigningKey: 'worker-key:4', ...document.secrets }).map(([name, value]) => {
      const [secret, key] = value.split(':')
      return { name, valueFrom: { secretKeyRef: { name: secret, key } } }
    }),
  ]
  let service = { metadata: { name: options['worker-service'], labels: { 'fortuneforge-game-worker': gameId } },
    spec: { template: { spec: { serviceAccountName: runtimeAccount, containers: [{ env: variables, image: 'old-image' }] } } },
    status: { url: 'https://candidate.example.run.app', traffic: [{ revisionName: 'old-revision', percent: 100 }] } }
  let revision
  let callerGranted = existing
  const calls = []
  const logs = []
  function runCloud(args, settings) {
    calls.push(args)
    if (args[0] === 'builds') {
      afterBuild?.(settingsPath)
      return { status: 'SUCCESS', results: { images: [{ name: settings.tag, digest }] } }
    }
    if (args[1] === 'deploy') {
      if (!existing) {
        assert.ok(args.includes('--invoker-iam-check'))
        assert.ok(args.includes('--no-allow-unauthenticated'))
        const env = args.find(arg => arg.startsWith('--update-env-vars='))?.slice('--update-env-vars='.length).split(',') ?? []
        const secrets = args.find(arg => arg.startsWith('--update-secrets=')).slice('--update-secrets='.length).split(',')
        service.spec.template.spec.containers[0].env = [
          ...env.map(item => { const index = item.indexOf('='); return { name: item.slice(0, index), value: item.slice(index + 1) } }),
          ...secrets.map(item => { const [name, reference] = item.split('='); const [secret, key] = reference.split(':'); return { name, valueFrom: { secretKeyRef: { name: secret, key } } } }),
        ]
      } else assert.ok(args.includes('--no-traffic'))
      const image = args.find(arg => arg.startsWith('--image=')).slice('--image='.length)
      service.spec.template.spec.containers[0].image = image
      service.status.latestReadyRevisionName = settings.revision
      if (!existing) service.status.traffic = [{ revisionName: settings.revision, percent: 100 }]
      revision = { metadata: { name: settings.revision }, spec: structuredClone(service.spec.template.spec),
        status: { conditions: [{ type: 'Ready', status: 'True' }] } }
      alterRevision?.(revision)
      return
    }
    if (args[1] === 'revisions') return structuredClone(revision)
    if (args[2] === 'list') return existing ? [structuredClone(service)] : []
    if (args[2] === 'describe') return structuredClone(service)
    if (args[2] === 'get-iam-policy') return { bindings: callerGranted ? [{ role: 'roles/run.invoker', members: ['serviceAccount:' + callerAccount] }] : [] }
    if (args[2] === 'add-iam-policy-binding') { callerGranted = true; return }
    if (args[2] === 'update-traffic') { service.status.traffic = [{ revisionName: settings.revision, percent: 100 }]; return }
    throw new Error('Unexpected fake cloud command: ' + args.join(' '))
  }
  const publish = overrides => publishGameWorker({ repository, backendRoot, gameId, expectedEntry: assembly + '.dll' },
    { ...options, ...overrides }, { runCloud, log: value => logs.push(JSON.parse(value)) })
  return { publish, calls, logs, settingsPath, service }
}

test('enrolls a separate private candidate with pinned wallet, batch, and roster connections', async t => {
  const f = await fixture(t)
  await f.publish()
  assert.equal(f.logs[0].service, 'fortuneforge-game-hearts-v2')
  assert.equal(f.logs[0].traffic, 'active')
  assert.match(f.logs[0].image, /@sha256:a{64}$/)
  const args = f.calls.find(args => args[1] === 'deploy')
  assert.ok(args.includes('--update-secrets=GameWorker__SigningKey=worker-key:4,GameWallet__SigningKey=wallet-key:1,GameWallet__BatchSigningKey=batch-key:2,ManagedPlayerRoster__SigningKey=roster-key:3'))
  assert.ok(f.calls.some(args => args[2] === 'add-iam-policy-binding'))
})

test('preserves explicit per-game feature admission policy and reports only secret references in local plans', async t => {
  const f = await fixture(t, { gameId: 'blackjack', document: {
    environment: { Features__BlackjackEnabled: false, Features__BlackjackTableEnabled: 'true' }, secrets: {},
  } })
  await f.publish({ 'worker-plan': true })
  assert.equal(f.calls.length, 0)
  assert.equal(f.logs[0].environment.Features__BlackjackEnabled, 'false')
  assert.equal(f.logs[0].environment.Features__BlackjackMultiplayerEnabled, 'true')
  assert.equal(f.logs[0].secret, 'worker-key:4')
  assert.equal(await readFile(join(f.logs[0].directory, 'Dockerfile'), 'utf8').then(value => value.includes('worker-key')), false)
})

test('keeps the canonical service and hosted settings when the new options are omitted', async t => {
  const f = await fixture(t, { gameId: '2048', document: { environment: {}, secrets: {} } })
  await f.publish({ 'worker-service': undefined, 'worker-settings': undefined, 'worker-plan': true })
  assert.equal(f.logs[0].service, 'fortuneforge-game-2048')
  assert.deepEqual(f.logs[0].environment, { GameWorker__Mode: 'hosted', GoogleCloud__ProjectId: 'example-project' })
  assert.deepEqual(f.logs[0].secrets, {})
})

test('accepts only the selected card game bot-fill policy', async t => {
  const f = await fixture(t, { gameId: 'texas-holdem', document: {
    environment: { Cards__CreditTexasHoldem__AllowSingleHumanBotFill: true }, secrets: {},
  } })
  await f.publish({ 'worker-plan': true })
  assert.equal(f.logs[0].environment.Cards__CreditTexasHoldem__AllowSingleHumanBotFill, 'true')
  const other = await fixture(t, { gameId: 'hearts', document: {
    environment: { Cards__CreditTexasHoldem__AllowSingleHumanBotFill: true }, secrets: {},
  } })
  await assert.rejects(other.publish(), /not allowed/)
  assert.equal(other.calls.length, 0)
})

test('rejects protected overrides, unrelated game policy, raw keys, and reused signing secrets before cloud calls', async t => {
  for (const document of [
    { environment: { GameWorker__Mode: 'development' }, secrets: {} },
    { environment: { Features__MinesEnabled: true }, secrets: {} },
    { environment: { Neon__AllowNewPaidRounds: true }, secrets: {} },
    { environment: { GameWallet__Url: 'https://wallet.example.test/path' }, secrets: { GameWallet__SigningKey: 'wallet-key:1' } },
    { environment: { GameWallet__Url: 'https://wallet.example.test' }, secrets: { GameWallet__SigningKey: 'unversioned' } },
    { environment: { GameWallet__Url: 'https://wallet.example.test' }, secrets: { GameWallet__SigningKey: 'worker-key:5' } },
    { environment: {}, secrets: { GameWallet__SigningKey: 'wallet-key:1' } },
    { environment: { GameWallet__Url: 'https://wallet.example.test' }, secrets: { GameWallet__SigningKey: 'same-key:1', GameWallet__BatchSigningKey: 'same-key:2' } },
  ]) {
    const f = await fixture(t, { document })
    await assert.rejects(f.publish())
    assert.equal(f.calls.length, 0)
  }
  const free = await fixture(t, { gameId: '2048', document: { environment: { GameWallet__Url: 'https://wallet.example.test' }, secrets: { GameWallet__SigningKey: 'wallet-key:1' } } })
  await assert.rejects(free.publish(), /not allowed/)
  assert.equal(free.calls.length, 0)
})

test('validates candidate service identifiers before cloud calls', async t => {
  const f = await fixture(t)
  for (const name of ['UPPERCASE', 'worker;echo', 'worker-', 'a'.repeat(50)]) {
    await assert.rejects(f.publish({ 'worker-service': name }))
  }
  assert.equal(f.calls.length, 0)
})

test('refuses existing runtime configuration mismatch before building', async t => {
  const f = await fixture(t, { existing: true })
  f.service.spec.template.spec.containers[0].env.find(item => item.name === 'GameWallet__BatchSigningKey').valueFrom.secretKeyRef.key = '99'
  await assert.rejects(f.publish(), /configuration differs/)
  assert.equal(f.calls.some(args => args[0] === 'builds'), false)
})

test('checks candidate revision runtime settings before granting host access or switching traffic', async t => {
  for (const existing of [false, true]) {
    const f = await fixture(t, { existing, alterRevision: revision => {
      revision.spec.containers[0].env.find(item => item.name === 'GameWallet__SigningKey').valueFrom.secretKeyRef.key = '99'
    } })
    await assert.rejects(f.publish(), /configuration differs/)
    assert.equal(f.calls.some(args => ['add-iam-policy-binding', 'update-traffic'].includes(args[2])), false)
  }
})

test('detects settings file changes during a build before deploying', async t => {
  const f = await fixture(t, { afterBuild: path => writeFileSync(path, JSON.stringify({ environment: {}, secrets: {} })) })
  await assert.rejects(f.publish(), /settings changed/)
  assert.equal(f.calls.some(args => args[1] === 'deploy'), false)
})

test('verifies existing candidates before activating their exact pinned revision', async t => {
  const f = await fixture(t, { existing: true })
  await f.publish()
  assert.equal(f.logs[0].traffic, 'active')
  assert.ok(f.calls.find(args => args[2] === 'update-traffic').includes('--to-revisions=fortuneforge-game-hearts-v2-g000000000000=100'))
})

test('accepts connection registrations and paid admission names from every actual packaged backend', async t => {
  const gamesRoot = fileURLToPath(new URL('../../games/', import.meta.url))
  async function sourceText(directory) {
    const entries = await readdir(directory, { withFileTypes: true })
    return (await Promise.all(entries.map(async entry => {
      if (entry.isDirectory()) return ['bin', 'obj'].includes(entry.name) ? '' : sourceText(join(directory, entry.name))
      return entry.name.endsWith('.cs') ? readFile(join(directory, entry.name), 'utf8') : ''
    }))).join('\n')
  }
  let checked = 0
  for (const entry of await readdir(gamesRoot, { withFileTypes: true })) {
    if (!entry.isDirectory()) continue
    let manifest
    try { manifest = JSON.parse(await readFile(join(gamesRoot, entry.name, 'game-release.json'), 'utf8')) }
    catch (error) { if (error.code === 'ENOENT') continue; throw error }
    if (manifest.backend?.kind !== 'packaged-game') continue
    const source = await sourceText(join(gamesRoot, entry.name, 'server'))
    const environment = Object.fromEntries([...source.matchAll(/"([A-Za-z0-9]+):AllowNewPaidRounds"/g)]
      .map(match => [match[1] + '__AllowNewPaidRounds', true]))
    const secrets = {}
    if (/\.Add(?:GameWalletClient|ArcadeCompetitionWallet)\(/.test(source)) {
      environment.GameWallet__Url = 'https://wallet.example.test'
      secrets.GameWallet__SigningKey = 'wallet-key:1'
    }
    if (/\.AddGameWalletBatchClient\(/.test(source)) {
      environment.GameWallet__Url = 'https://wallet.example.test'
      secrets.GameWallet__BatchSigningKey = 'batch-key:1'
    }
    if (/\.AddManagedPlayerRosterClient\(/.test(source)) {
      environment.ManagedPlayerRoster__Url = 'https://roster.example.test'
      secrets.ManagedPlayerRoster__SigningKey = 'roster-key:1'
    }
    const f = await fixture(t, { gameId: manifest.id, document: { environment, secrets } })
    await f.publish({ 'worker-plan': true })
    assert.equal(f.calls.length, 0, manifest.id)
    assert.deepEqual(f.logs[0].secrets, secrets, manifest.id)
    checked++
  }
  assert.equal(checked, 45, 'all packaged release manifests were checked against their source registrations')
})
