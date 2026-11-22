import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { dirname, resolve, relative, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'
import { realpathSync } from 'node:fs'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const json = async path => JSON.parse(await readFile(path, 'utf8'))
const safeId = value => typeof value === 'string' && /^[a-z0-9-]+$/.test(value)

async function verifyCurrentSourceHashes(provenance) {
  const algorithm = provenance.sourceHashAlgorithm ?? 'sha256-raw-bytes-v1'
  assert.ok(['sha256-raw-bytes-v1', 'sha256-utf8-lf-v1'].includes(algorithm), 'Unknown source hash algorithm')
  assert.ok(Object.keys(provenance.sourceSha256).length > 0)
  for (const [name, expected] of Object.entries(provenance.sourceSha256)) {
    const filename = resolve(repository, name), rel = relative(repository, filename)
    assert.ok(rel && !isAbsolute(rel) && !rel.startsWith('..'), 'Source provenance escaped repository')
    const bytes = await readFile(filename)
    const source = algorithm === 'sha256-utf8-lf-v1'
      ? new TextDecoder('utf-8', { fatal: true }).decode(bytes).replace(/\r\n?/g, '\n') : bytes
    const actual = createHash('sha256').update(source).digest('hex')
    assert.equal(actual, expected, `Capture is stale for ${name}; rebuild and export current source`)
  }
}

export async function verifySlotGuideContracts(directory) {
  const root = resolve(directory)
  const index = await json(resolve(root, 'index.json'))
  const provenance = await json(resolve(root, 'source-provenance.json'))
  assert.equal(index.kind, 'slot-guide-definitions-v1', 'Expected a provider-only guide export')
  assert.equal(provenance.sourceHashAlgorithm, 'sha256-utf8-lf-v1', 'Guide source hashes require versioned canonical UTF8/LF text')
  assert.equal(index.gameCount, 19)
  assert.equal(index.games.length, index.gameCount)
  assert.equal(new Set(index.games.map(game => game.route.id)).size, index.gameCount)
  assert.equal(new Set(index.games.map(game => game.route.gameId)).size, index.gameCount)
  assert.equal(provenance.sourceProjects.length, 20, 'Retain all independently owned slot projects')
  assert.equal(new Set(provenance.sourceProjects).size, 20)
  for (const project of provenance.sourceProjects) {
    assert.ok(/^games\/[^/]+\/src\/[^/]+\/[^/]+\.csproj$/.test(project), 'Expected a package-owned slot project')
    assert.ok(Object.hasOwn(provenance.sourceSha256, project), `Missing slot owner provenance: ${project}`)
  }
  assert.equal(provenance.hashesUnchangedDuringExport, true)
  assert.equal(provenance.recordedSourceCount, Object.keys(provenance.sourceSha256).length)
  assert.match(provenance.assemblySha256, /^[a-f0-9]{64}$/)
  for (const game of index.games) {
    assert.ok(safeId(game.route.id), 'Unsafe guide contract path')
    const contract = await json(resolve(root, game.route.id, 'contract.json'))
    assert.equal(contract.gameId, game.route.gameId)
    assert.equal(contract.profile.gameId, contract.gameId)
    assert.ok(contract.paytable.length > 0)
  }
  await verifyCurrentSourceHashes(provenance)
  return { directory: root, games: index.gameCount, index, provenance }
}

export async function verifySlotContracts(directory, { requireCurrent = false } = {}) {
  const root = resolve(directory)
  const index = await json(resolve(root, 'index.json'))
  const provenance = await json(resolve(root, 'source-provenance.json'))
  // Jade Dragon has a dedicated guide outside the shared demo registry.
  assert.equal(index.gameCount, 19)
  assert.equal(index.games.length, index.gameCount)
  assert.equal(new Set(index.games.map(game => game.route.id)).size, index.gameCount)
  assert.equal(new Set(index.games.map(game => game.route.gameId)).size, index.gameCount)
  const files = ['index.json', 'source-provenance.json']
  let scenarios = 0, responses = 0
  for (const game of index.games) {
    assert.ok(safeId(game.route.id), 'Unsafe game fixture path')
    const contractPath = `${game.route.id}/contract.json`
    const contract = await json(resolve(root, contractPath))
    files.push(contractPath)
    assert.equal(contract.gameId, game.route.gameId)
    const usesCharmHarvest = contract.gameId === 'rainbow-realm-fruits-v1' && !contract.profile.usesDirectValueTokens
    assert.equal(contract.statusEndpoint, 204)
    assert.deepEqual(contract.missingScenarios, [])
    const names = new Set(contract.scenarios.map(item => item.scenario))
    assert.ok(names.has('base-win') && names.has('base-loss'))
    assert.equal(names.size, contract.scenarios.length)
    for (const scenario of contract.scenarios) {
      assert.ok(safeId(scenario.scenario), 'Unsafe scenario fixture path')
      const filename = `${game.route.id}/${scenario.scenario}.json`
      const capture = await json(resolve(root, filename))
      files.push(filename)
      assert.equal(capture.steps.length, scenario.responses)
      assert.equal(capture.steps.at(-1).response.freeSpinsRemaining, 0)
      for (const { request, response } of capture.steps) {
        assert.equal(request.gameId, game.route.gameId)
        assert.equal(response.gameId, game.route.gameId)
        assert.equal(response.wagerPoints, request.wagerPoints)
        assert.equal(response.slotsCreditsBalance, null)
        assert.ok(!Object.hasOwn(response, 'engineAudit'))
        const usesFoundryCollector = response.gameId === 'gold-foundry-v1' &&
          response.paytableId === 'gold-foundry-paytable-v1'
        const offPaylinePoints = usesCharmHarvest || usesFoundryCollector ? response.moneyGrabPoints : 0
        assert.equal(response.payout.totalPoints, response.payout.paylines.reduce((sum, line) => sum + line.amountPoints, offPaylinePoints))
        for (const line of response.payout.paylines)
          assert.equal(line.amountPoints, line.matches.reduce((sum, match) => sum + match.amountPoints, 0))
      }
      scenarios++
      responses += capture.steps.length
    }
  }
  assert.equal(scenarios, index.scenarioCount)
  assert.equal(responses, index.responseCount)
  if (requireCurrent) {
    await verifyCurrentSourceHashes(provenance)
  }
  return { directory: root, games: index.gameCount, scenarios, responses, files, sourceHashesCurrent: requireCurrent, index, provenance }
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const directory = process.argv.slice(2).find(argument => !argument.startsWith('--'))
    ?? resolve(repository, 'tools/fixtures/slot-contracts')
  const result = await verifySlotContracts(directory, { requireCurrent: process.argv.includes('--require-current') })
  console.log(JSON.stringify({ games: result.games, scenarios: result.scenarios, responses: result.responses,
    files: result.files.length, sourceHashesCurrent: result.sourceHashesCurrent }))
}
