import { spawnSync } from 'node:child_process'
import { cp, mkdir, mkdtemp, readFile, stat, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { assertArtifactPath, isGameId, isReleaseId } from './game-release-activation.mjs'

export const gameWorkerOptions = {
  'publish-worker': { type: 'string' },
  'worker-project': { type: 'string' },
  'worker-region': { type: 'string' },
  'worker-repository': { type: 'string' },
  'worker-service-account': { type: 'string' },
  'worker-caller-service-account': { type: 'string' },
  'worker-secret': { type: 'string' },
  'worker-service': { type: 'string' },
  'worker-settings': { type: 'string' },
  'worker-plan': { type: 'boolean' },
  'enroll-worker': { type: 'boolean' },
  'activate-worker': { type: 'boolean' },
}

const readJson = async path => JSON.parse(await readFile(path, 'utf8'))
const signingVariable = 'GameWorker__SigningKey'
const gameLabel = 'fortuneforge-game-worker'
const serviceAccount = /^[a-z0-9][a-z0-9-]*@[a-z0-9][a-z0-9.-]*\.gserviceaccount\.com$/
const digestPattern = /^sha256:[a-f0-9]{64}$/
const secretReference = /^[a-zA-Z0-9_-]{1,255}:[1-9][0-9]*$/
const paidRoundSettings = new Map([
  ['arcane-archives', 'Arcane'], ['neon-nights', 'Neon'], ['jade-dragon', 'JadeDragon'],
  ['gold-foundry', 'GoldFoundry'], ['midnight-vault', 'MidnightVault'], ['wukong', 'Wukong'],
  ['rainbow-realm', 'RainbowRealm'], ['pirates-fortune', 'PiratesFortune'],
  ['gods-of-olympus', 'GodsOfOlympus'], ['high-noon-fortune', 'FrontierFortune'],
  ['reel-riches', 'ReelRiches'], ['royal-draw', 'RoyalDraw'], ['pharaohs-ascent', 'PharaohsAscent'],
  ['ocean-odyssey', 'OceanOdyssey'], ['crooked-road', 'CrookedRoad'],
  ['candy-carnival', 'CandyCarnival'], ['phantom-manor', 'PhantomManor'],
  ['nordic-legends', 'NordicLegends'], ['desert-treasures', 'DesertTreasures'],
  ['robot-revolution', 'RobotRevolution'], ['solitaire', 'Solitaire'], ['casino-war', 'CasinoWar'],
])
const walletGames = new Set([...paidRoundSettings.keys(), 'andar-bahar', 'baccarat', 'blackjack', 'craps',
  'asteroids', 'dragon-tiger', 'flappy', 'hearts', 'hi-lo', 'horse-flight', 'house-holdem', 'keno', 'mines', 'roulette',
  'sic-bo', 'texas-holdem', 'three-card-poker', 'video-poker'])
const batchWalletGames = new Set(['hearts', 'solitaire', 'texas-holdem'])
const rosterGames = new Set(['blackjack', 'hearts', 'liars-dice', 'roulette', 'solitaire', 'texas-holdem', 'three-card-poker'])
const workerPolicies = new Map([
  ['texas-holdem', ['Cards__CreditTexasHoldem__AllowSingleHumanBotFill']],
  ['solitaire', ['Cards__CompetitiveSolitaire__AllowSingleHumanBotFill']],
])
const packagedWorkerFeatures = new Map([
  ['2048', []],
  ['andar-bahar', ['AndarBaharEnabled']],
  ['arcane-archives', []],
  ['gold-foundry', []],
  ['gods-of-olympus', []],
  ['high-noon-fortune', []],
  ['jade-dragon', []],
  ['midnight-vault', []],
  ['pirates-fortune', []],
  ['rainbow-realm', []],
  ['reel-riches', []],
  ['royal-draw', []],
  ['pharaohs-ascent', []],
  ['ocean-odyssey', []],
  ['crooked-road', []],
  ['candy-carnival', []],
  ['phantom-manor', []],
  ['nordic-legends', []],
  ['desert-treasures', []],
  ['robot-revolution', []],
  ['wukong', []],
  ['neon-nights', []],
  ['craps', ['CrapsEnabled']],
  ['baccarat', ['BaccaratEnabled']],
  ['roulette', ['RouletteEnabled']],
  ['dragon-tiger', ['DragonTigerEnabled']],
  ['drop-merge', []],
  ['free-solitaire', []],
  ['solitaire', ['CompetitiveSolitaireEnabled']],
  ['blackjack', ['BlackjackEnabled', 'BlackjackMultiplayerEnabled', 'BlackjackTableEnabled']],
  ['texas-holdem', ['CreditTexasHoldemEnabled']],
  ['three-card-poker', ['ThreeCardPokerTableEnabled']],
  ['hearts', ['HeartsEnabled']],
  ['house-holdem', []],
  ['video-poker', []],
  ['casino-war', []],
  ['asteroids', []],
  ['flappy', []],
  ['horse-flight', ['HorseFlightEnabled']],
  ['snake', []],
  ['liars-dice', ['LiarsDiceEnabled']],
  ['sic-bo', ['SicBoEnabled']],
  ['keno', ['KenoEnabled']],
  ['hi-lo', ['HiLoEnabled']],
  ['mines', ['MinesEnabled', 'MinesPaidEnabled']],
])

function workerSettings(gameId, options) {
  const releaseId = options['publish-worker']
  const project = options['worker-project']
  const region = options['worker-region']
  const repositoryName = options['worker-repository']
  const runtimeAccount = options['worker-service-account']
  const callerAccount = options['worker-caller-service-account']
  const secret = options['worker-secret']
  const service = options['worker-service'] ?? 'fortuneforge-game-' + gameId
  if (!isGameId(gameId) || gameId.length > 63 || !isReleaseId(releaseId) || !/^[a-z][a-z0-9-]{4,28}[a-z0-9]$/.test(project ?? '') ||
    !/^[a-z][a-z0-9-]{1,28}[0-9]$/.test(region ?? '') ||
    !/^[a-z][a-z0-9-]{0,62}$/.test(repositoryName ?? '') ||
    !serviceAccount.test(runtimeAccount ?? '') || !serviceAccount.test(callerAccount ?? '') ||
    !secretReference.test(secret ?? '') || !/^[a-z](?:[a-z0-9-]{0,47}[a-z0-9])?$/.test(service) ||
    options['enroll-worker'] && options['activate-worker']) {
    throw new Error('Worker publication requires a staged release ID, project, region, repository, runtime and caller service accounts, and a same-project secret name with a numeric version. Enrollment and explicit activation are separate modes.')
  }
  const revisionSuffix = 'g' + releaseId.replaceAll('-', '').slice(0, 12)
  const image = `${region}-docker.pkg.dev/${project}/${repositoryName}/${service}`
  return { releaseId, project, region, runtimeAccount, callerAccount, secret, service, revisionSuffix, image,
    revision: `${service}-${revisionSuffix}`, tag: `${image}:${releaseId}` }
}

async function readRuntimeSettings(gameId, options, signingSecret) {
  const path = options['worker-settings'] === undefined ? undefined : resolve(options['worker-settings'])
  if (path === undefined) return { environment: {}, secrets: {}, assertUnchanged: async () => {} }
  await assertArtifactPath(path)
  const snapshot = await readFile(path)
  if (snapshot.length > 64 * 1024) throw new Error('Worker settings must fit within 64 KiB')
  let document
  try { document = JSON.parse(snapshot.toString('utf8')) }
  catch { throw new Error('Worker settings must be a JSON object with environment and secrets objects') }
  const record = value => value !== null && typeof value === 'object' && !Array.isArray(value)
  if (!record(document) || Object.keys(document).sort().join(',') !== 'environment,secrets' ||
    !record(document.environment) || !record(document.secrets)) {
    throw new Error('Worker settings require only environment and secrets objects')
  }
  const environment = {}
  const paidVariable = paidRoundSettings.has(gameId) ? paidRoundSettings.get(gameId) + '__AllowNewPaidRounds' : undefined
  const booleanVariables = new Set([
    ...(packagedWorkerFeatures.get(gameId) ?? []).map(feature => 'Features__' + feature),
    ...(workerPolicies.get(gameId) ?? []), ...(paidVariable ? [paidVariable] : []),
  ])
  for (const [name, value] of Object.entries(document.environment)) {
    if (name === 'GameWallet__Url' && walletGames.has(gameId) || name === 'ManagedPlayerRoster__Url' && rosterGames.has(gameId)) {
      if (typeof value !== 'string' || value.length > 261 ||
        !/^https:\/\/(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\/?$/.test(value)) {
        throw new Error(`Worker setting ${name} requires an HTTPS origin`)
      }
    } else if (!booleanVariables.has(name) || ![true, false, 'true', 'false'].includes(value)) {
      throw new Error(`Worker environment setting is not allowed: ${name}`)
    }
    environment[name] = String(value)
  }
  const secrets = {}
  const allowedSecrets = new Set([
    ...(walletGames.has(gameId) ? ['GameWallet__SigningKey'] : []),
    ...(batchWalletGames.has(gameId) ? ['GameWallet__BatchSigningKey'] : []),
    ...(rosterGames.has(gameId) ? ['ManagedPlayerRoster__SigningKey'] : []),
  ])
  const secretNames = new Set([signingSecret.split(':')[0]])
  for (const [name, value] of Object.entries(document.secrets)) {
    if (!allowedSecrets.has(name) || typeof value !== 'string' || !secretReference.test(value)) {
      throw new Error(`Worker secret setting requires an allowed name and pinned same-project reference: ${name}`)
    }
    const secretName = value.split(':')[0]
    if (secretNames.has(secretName)) throw new Error('Worker, wallet, batch, and roster signing secrets must be separate secrets')
    secretNames.add(secretName)
    secrets[name] = value
  }
  const hasWalletKey = secrets.GameWallet__SigningKey !== undefined || secrets.GameWallet__BatchSigningKey !== undefined
  if ((environment.GameWallet__Url !== undefined) !== hasWalletKey ||
    (environment.ManagedPlayerRoster__Url !== undefined) !== (secrets.ManagedPlayerRoster__SigningKey !== undefined)) {
    throw new Error('Worker wallet and roster origins require their corresponding signing-secret references')
  }
  const assertUnchanged = async () => {
    await assertArtifactPath(path)
    if (!(await readFile(path)).equals(snapshot)) throw new Error('Worker settings changed during publication')
  }
  return { environment, secrets, assertUnchanged }
}

async function createContext({ repository, backendRoot, gameId, expectedEntry }, releaseId) {
  const source = join(backendRoot, releaseId)
  await assertArtifactPath(source, true)
  const descriptorPath = join(source, 'release.json')
  await assertArtifactPath(descriptorPath)
  const descriptor = await readJson(descriptorPath)
  if (descriptor.gameId !== gameId || descriptor.releaseId !== releaseId || descriptor.entry !== expectedEntry ||
    !/^FortuneForge\.Games\.[A-Za-z0-9]+\.Server\.dll$/.test(expectedEntry)) {
    throw new Error('Staged worker descriptor does not match the requested game, release, and executable')
  }
  const assembly = expectedEntry.slice(0, -4)
  const runtimeConfig = join(source, assembly + '.runtimeconfig.json')
  const dependencies = join(source, assembly + '.deps.json')
  for (const file of [join(source, expectedEntry), runtimeConfig, dependencies]) await assertArtifactPath(file)
  const { runtimeOptions } = await readJson(runtimeConfig)
  const { runtimeTarget } = await readJson(dependencies)
  if (runtimeOptions?.tfm !== 'net10.0' ||
    !/^\.NETCoreApp,Version=v10\.0(?:\/linux-x64)?$/.test(runtimeTarget?.name ?? '')) {
    throw new Error('Worker publication requires a portable or linux-x64 .NET 10 backend artifact')
  }

  const artifacts = join(repository, 'artifacts')
  await assertArtifactPath(artifacts, true)
  const contexts = join(artifacts, 'game-worker-contexts')
  await mkdir(contexts, { recursive: true })
  await assertArtifactPath(contexts, true)
  const directory = await mkdtemp(join(contexts, `${gameId}-${releaseId}-`))
  await cp(source, join(directory, 'worker'), {
    recursive: true,
    filter: async path => {
      const info = await stat(path)
      await assertArtifactPath(path, info.isDirectory())
      return path !== descriptorPath && path !== join(source, assembly + '.exe')
    },
  })
  await writeFile(join(directory, 'Dockerfile'), [
    'FROM mcr.microsoft.com/dotnet/aspnet:10.0', 'WORKDIR /app', 'COPY worker/ ./',
    'ENV ASPNETCORE_ENVIRONMENT=Production', 'ENV ASPNETCORE_HTTP_PORTS=8080',
    'EXPOSE 8080', 'USER $APP_UID', `ENTRYPOINT ["dotnet", "${expectedEntry}"]`, '',
  ].join('\n'), { flag: 'wx' })
  await writeFile(join(directory, '.gcloudignore'), '.gcloudignore\n', { flag: 'wx' })
  return directory
}

function cloud(args, { project, region }, { directory, json = false } = {}) {
  // The Windows Cloud SDK launcher is a cmd shim. Only validated identifiers and
  // fixed arguments reach it. The context directory is passed as cwd, not as shell text.
  const result = spawnSync('gcloud', [...args, `--project=${project}`, `--region=${region}`, '--quiet',
    ...(json ? ['--format=json'] : [])], {
    cwd: directory, encoding: 'utf8', shell: process.platform === 'win32', windowsHide: true, maxBuffer: 4 * 1024 * 1024,
  })
  if (result.error) throw result.error
  if (result.status !== 0) throw new Error(`gcloud ${args.slice(0, 3).join(' ')} failed: ${result.stderr.trim()}`)
  return json ? JSON.parse(result.stdout) : undefined
}

function workerEnvironment({ expectedEntry, gameId }, project) {
  if (expectedEntry !== 'FortuneForge.Games.PackagedGame.Server.dll') return {}
  const features = packagedWorkerFeatures.get(gameId)
  if (!features) throw new Error(`Hosted feature settings are missing for ${gameId}`)
  return {
    GameWorker__Mode: 'hosted',
    GoogleCloud__ProjectId: project,
    ...Object.fromEntries(features.map(feature => [`Features__${feature}`, 'true'])),
    ...(['hi-lo', 'mines'].includes(gameId) ? {
      GameWorker__AllowedOrigins__0: `https://${project}.web.app`,
      GameWorker__AllowedOrigins__1: `https://${project}.firebaseapp.com`,
    } : {}),
  }
}

function checkRuntime(runtime, settings, environment, secrets) {
  const containers = runtime?.containers ?? []
  const variables = containers[0]?.env ?? []
  if (runtime?.serviceAccountName !== settings.runtimeAccount || containers.length !== 1 ||
    Object.entries(environment).some(([name, value]) => variables.filter(variable => variable.name === name).length !== 1 ||
      variables.find(variable => variable.name === name)?.value !== value) ||
    Object.entries({ [signingVariable]: settings.secret, ...secrets }).some(([name, reference]) => {
      const matches = variables.filter(variable => variable.name === name)
      const secret = matches[0]?.valueFrom?.secretKeyRef
      return matches.length !== 1 || matches[0].value !== undefined || `${secret?.name}:${secret?.key}` !== reference
    })) {
    throw new Error('The worker runtime identity, environment, or pinned signing-secret configuration differs from the required configuration')
  }
}

function checkWorker(service, settings, gameId, environment, secrets) {
  const annotations = service.metadata?.annotations ?? {}
  if (service.metadata?.name !== settings.service || service.metadata?.labels?.[gameLabel] !== gameId ||
    annotations['run.googleapis.com/invoker-iam-disabled'] === 'true') {
    throw new Error('Cloud Run service is not the expected private game worker with the configured runtime identity and signing secret')
  }
  checkRuntime(service.spec?.template?.spec, settings, environment, secrets)
}

function checkPrivatePolicy(settings, requireCaller = true, runCloud = cloud) {
  const policy = runCloud(['run', 'services', 'get-iam-policy', settings.service], settings, { json: true })
  const bindings = policy.bindings ?? []
  if (bindings.some(binding => binding.members?.some(member => member === 'allUsers' || member === 'allAuthenticatedUsers'))) {
    throw new Error('Refusing to publish to a service with a public IAM binding')
  }
  if (requireCaller && !bindings.some(binding => binding.role === 'roles/run.invoker' && !binding.condition &&
    binding.members?.includes('serviceAccount:' + settings.callerAccount))) {
    throw new Error('The configured host service account is missing its service-level Run Invoker binding')
  }
}

export async function publishGameWorker(target, options, { runCloud = cloud, log = console.log } = {}) {
  const { gameId } = target
  const settings = workerSettings(gameId, options)
  const runtimeSettings = await readRuntimeSettings(gameId, options, settings.secret)
  const { secrets } = runtimeSettings
  const environment = { ...workerEnvironment(target, settings.project), ...runtimeSettings.environment }
  const environmentFlags = Object.entries(environment).map(([name, value]) => `${name}=${value}`)
  const secretFlags = Object.entries({ [signingVariable]: settings.secret, ...secrets }).map(([name, value]) => `${name}=${value}`)
  const enroll = options['enroll-worker'] === true
  const activate = options['activate-worker'] === true
  const directory = await createContext(target, settings.releaseId)
  await runtimeSettings.assertUnchanged()
  if (options['worker-plan']) {
    log(JSON.stringify({ ...settings, directory, enroll, activate, environment, secrets, mode: 'local worker plan' }, null, 2))
    return
  }

  const describe = () => runCloud(['run', 'services', 'describe', settings.service], settings, { json: true })
  const preflight = () => {
    if (enroll) {
      const existing = runCloud(['run', 'services', 'list', `--filter=metadata.name=${settings.service}`], settings, { json: true })
      if (!Array.isArray(existing) || existing.length !== 0) throw new Error('Enrollment requires a new worker service name')
    } else {
      checkWorker(describe(), settings, gameId, environment, secrets)
      checkPrivatePolicy(settings, true, runCloud)
    }
  }
  preflight()

  const build = runCloud(['builds', 'submit', '.', `--tag=${settings.tag}`, '--suppress-logs'], settings, { directory, json: true })
  const digest = build.results?.images?.find(image => image.name === settings.tag)?.digest
  if (build.status !== 'SUCCESS' || !digestPattern.test(digest ?? '')) throw new Error('Cloud Build did not return the expected worker image digest')
  const image = settings.image + '@' + digest
  await runtimeSettings.assertUnchanged()
  preflight()
  runCloud(['run', 'deploy', settings.service, `--image=${image}`, `--revision-suffix=${settings.revisionSuffix}`,
    ...(enroll ? ['--no-allow-unauthenticated', '--invoker-iam-check', '--port=8080',
      `--service-account=${settings.runtimeAccount}`, `--update-labels=${gameLabel}=${gameId}`,
      ...(environmentFlags.length ? [`--update-env-vars=${environmentFlags.join(',')}`] : []),
      `--update-secrets=${secretFlags.join(',')}`] : ['--no-traffic'])], settings)

  const revision = runCloud(['run', 'revisions', 'describe', settings.revision], settings, { json: true })
  if (revision.metadata?.name !== settings.revision || revision.spec?.containers?.[0]?.image !== image ||
    !revision.status?.conditions?.some(condition => condition.type === 'Ready' && condition.status === 'True')) {
    throw new Error('The worker revision did not become ready with the pinned image')
  }
  checkRuntime(revision.spec, settings, environment, secrets)
  const service = describe()
  checkWorker(service, settings, gameId, environment, secrets)
  checkPrivatePolicy(settings, !enroll, runCloud)
  if (!/^https:\/\/[a-z0-9.-]+\.run\.app\/?$/.test(service.status?.url ?? '')) {
    throw new Error('The ready worker did not report a Cloud Run HTTPS origin')
  }
  await runtimeSettings.assertUnchanged()
  if (enroll) {
    runCloud(['run', 'services', 'add-iam-policy-binding', settings.service,
      `--member=serviceAccount:${settings.callerAccount}`, '--role=roles/run.invoker'], settings)
    checkPrivatePolicy(settings, true, runCloud)
  } else if (activate) {
    runCloud(['run', 'services', 'update-traffic', settings.service, `--to-revisions=${settings.revision}=100`], settings)
  }
  const serving = enroll || activate ? describe() : service
  const candidatePercent = (serving.status?.traffic ?? []).filter(traffic => traffic.revisionName === settings.revision ||
    traffic.latestRevision && serving.status?.latestReadyRevisionName === settings.revision)
    .reduce((sum, traffic) => sum + (traffic.percent ?? 0), 0)
  if (candidatePercent !== (enroll || activate ? 100 : 0)) throw new Error('Worker traffic does not match the requested release mode')
  await runtimeSettings.assertUnchanged()
  log(JSON.stringify({ gameId, releaseId: settings.releaseId, service: settings.service, url: serving.status?.url,
    revision: settings.revision, image, traffic: enroll || activate ? 'active' : 'staged' }, null, 2))
}
