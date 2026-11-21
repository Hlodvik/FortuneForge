import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createHash, createHmac, randomBytes, randomUUID } from 'node:crypto'
import { createWriteStream } from 'node:fs'
import { writeFile } from 'node:fs/promises'
import { createServer } from 'node:net'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const artifact = new URL('../../../artifacts/backend-completion/', import.meta.url)
const worker = new URL('../../shared/FortuneForge.Games.PackagedGame.Server/bin/Asteroids/Release/net10.0/FortuneForge.Games.PackagedGame.Server.dll', import.meta.url)
const emulator = process.env.FIRESTORE_EMULATOR_HOST ?? '127.0.0.1:8085'
assert.match(emulator, /^(127\.0\.0\.1|localhost):\d+$/)
const results = []

for (const mode of ['emulator', 'hosted']) {
  const projectId = `demo-asteroids-raw-${mode}-${randomUUID().replaceAll('-', '')}`
  const signingKey = randomBytes(32)
  const listener = createServer()
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve))
  const port = listener.address().port
  await new Promise(resolve => listener.close(resolve))
  const origin = `http://127.0.0.1:${port}`
  const env = { ...process.env }
  for (const name of Object.keys(env)) {
    if (/^(GameWallet|GameWorker|GoogleCloud|PORT)/i.test(name)) delete env[name]
  }
  Object.assign(env, {
    FIRESTORE_EMULATOR_HOST: emulator, GoogleCloud__ProjectId: projectId,
    ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: origin, GameWorker__Mode: mode,
    GameWallet__Url: '', GameWallet__SigningKey: '', GameSecurityRequests__Enabled: 'false',
    ...(mode === 'hosted'
      ? { GameWorker__SigningKey: signingKey.toString('base64'), GameWorker__AllowDevelopmentLoopback: 'true' }
      : { GameWorker__DemoUserId: 'asteroids-smoke-account' }),
  })
  const log = createWriteStream(new URL(`asteroids-worker-${mode}.log`, artifact))
  const child = spawn('dotnet', [fileURLToPath(worker)], { cwd: root, env, windowsHide: true })
  child.stdout.pipe(log, { end: false })
  child.stderr.pipe(log, { end: false })
  let exited = false
  const stopped = new Promise(resolve => child.once('exit', () => { exited = true; resolve() }))
  const result = { mode, projectId, checks: [], walletConfigured: false, workerStopped: false }
  const check = (name) => result.checks.push(name)
  try {
    let healthy = false
    for (let attempt = 0; attempt < 120 && !exited; attempt++) {
      try { healthy = (await fetch(`${origin}/healthz`, { signal: AbortSignal.timeout(1000) })).ok } catch {}
      if (healthy) break
      await new Promise(resolve => setTimeout(resolve, 250))
    }
    assert.equal(healthy, true, `${mode} worker did not become healthy`)
    assert.equal((await (await fetch(`${origin}/healthz`)).json()).gameId, 'asteroids')
    check('selected worker health identifies asteroids')

    async function call(target, { method = 'GET', body, key, game = 'asteroids', unsigned = false, headers = {} } = {}) {
      const bytes = Buffer.from(body ?? '', 'utf8')
      const signedHeaders = key ? { 'Idempotency-Key': key } : null
      if (key) headers['Idempotency-Key'] = key
      if (body !== undefined) headers['Content-Type'] = 'application/json'
      if (mode === 'hosted' && !unsigned) {
        const payload = Buffer.from(JSON.stringify({ GameId: game, PlayerId: 'asteroids-smoke-account', Method: method,
          Target: target, IssuedAt: Math.floor(Date.now() / 1000), BodySha256: createHash('sha256').update(bytes).digest('hex').toUpperCase(),
          Observation: null, Headers: signedHeaders }))
        headers['X-FortuneForge-Worker-Assertion'] = `${payload.toString('base64url')}.${createHmac('sha256', signingKey).update(payload).digest('base64url')}`
      }
      return fetch(origin + target, { method, body, headers, signal: AbortSignal.timeout(15000) })
    }

    const prefix = '/api/arcade-competitions/asteroids'
    if (mode === 'hosted') {
      assert.equal((await call(`${prefix}/daily`, { unsigned: true })).status, 403)
      assert.equal((await call(`${prefix}/daily`, { game: 'flappy' })).status, 403)
      check('unsigned and wrong-game assertions rejected')
    }
    const standings = await call(`${prefix}/daily`)
    assert.equal(standings.status, 200)
    const window = await standings.json()
    check('daily standings available')
    const freeKey = `asteroids-raw-free-${randomUUID()}`
    const free = await call(`${prefix}/free/runs`, { method: 'POST', key: freeKey })
    assert.equal(free.status, 200)
    const started = await free.json()
    const freeRetry = await call(`${prefix}/free/runs`, { method: 'POST', key: freeKey })
    const repeatedStart = await freeRetry.json()
    assert.equal(repeatedStart.runId, started.runId)
    assert.equal(repeatedStart.seedHex, started.seedHex)
    assert.equal(repeatedStart.wasReplay, true)
    check('free start retry preserves run and seed')
    const replay = JSON.stringify({ rulesVersion: 2, totalSteps: 3600, commands: [] })
    const completion = await call(`${prefix}/free/runs/${started.runId}/replay`, { method: 'POST', body: replay })
    assert.equal(completion.status, 200)
    const completed = await completion.json()
    const repeatedCompletion = await call(`${prefix}/free/runs/${started.runId}/replay`, { method: 'POST', body: replay })
    assert.deepEqual(await repeatedCompletion.json(), { ...completed, wasReplay: true })
    const recovered = await call(`${prefix}/free/runs/${started.runId}`)
    assert.equal((await recovered.json()).status, 'completed')
    check('free completion retry and recovery preserve outcome')
    assert.equal((await call(`${prefix}/daily`, { headers: { 'X-FortuneForge-Practice': 'true' } })).status, 403)
    check('practice header rejected')

    const key = `asteroids-raw-paid-${randomUUID()}`
    const body = JSON.stringify({ expectedWindowStartsAtUtc: window.startsAtUtc, expectedWindowEndsAtUtc: window.endsAtUtc }, null, 2) + '\r\n'
    const digest = createHash('sha256').update(body).digest('hex')
    assert.equal((await call(`${prefix}/daily/attempts`, { method: 'POST', body, key })).status, 503)
    const documents = async (collection) => {
      const response = await fetch(`http://${emulator}/v1/projects/${projectId}/databases/(default)/documents/${collection}`)
      assert.equal(response.status, 200)
      return (await response.json()).documents ?? []
    }
    let prepared = await documents('arcadeCompetitionPreparedEntries')
    assert.equal(prepared.length, 1)
    const intent = JSON.parse(prepared[0].fields.intentJson.stringValue)
    assert.equal(intent.request.requestBodySha256, digest)
    check('exact original whitespace and CRLF digest persisted before unavailable wallet')
    assert.equal((await call(`${prefix}/daily/attempts?recoverOnly=true`, { method: 'POST', body: body + ' ', key })).status, 409)
    check('changed raw bytes conflict with same pending operation key')
    assert.equal((await call(`${prefix}/daily/attempts?recoverOnly=true`, { method: 'POST', body, key })).status, 503)
    prepared = await documents('arcadeCompetitionPreparedEntries')
    assert.equal(prepared.length, 1)
    assert.equal(prepared[0].fields.status.stringValue, 'pending')
    assert.equal((await documents('arcadeCompetitionAttempts')).length, 0)
    assert.equal((await documents('asteroidsRuns')).length, 0)
    check('identical-byte recovery keeps one pending intent and no accepted run without wallet')
    result.requestBodySha256 = digest
    result.status = 'passed'
  } finally {
    if (!exited) child.kill()
    await Promise.race([stopped, new Promise((_, reject) => setTimeout(() => reject(new Error('worker did not stop')), 5000))])
    result.workerStopped = exited
    log.end()
  }
  results.push(result)
}
await writeFile(new URL('asteroids-worker-smoke.json', artifact), JSON.stringify({ checkedAtUtc: new Date().toISOString(), results }, null, 2) + '\n')
console.log(JSON.stringify(results, null, 2))
