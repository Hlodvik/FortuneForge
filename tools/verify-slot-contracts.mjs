import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { dirname, resolve, relative, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const json = async path => JSON.parse(await readFile(path, 'utf8'))
const safeId = value => typeof value === 'string' && /^[a-z0-9-]+$/.test(value)

export async function verifySlotContracts(directory, { requireCurrent = false } = {}) {
  const root = resolve(directory)
  const index = await json(resolve(root, 'index.json'))
  const provenance = await json(resolve(root, 'source-provenance.json'))
  assert.equal(index.gameCount, 20)
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
        const usesFoundryCollector = response.gameId === 'dino-dominion-v1' &&
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
    assert.ok(Object.keys(provenance.sourceSha256).length > 0)
    for (const [name, expected] of Object.entries(provenance.sourceSha256)) {
      const filename = resolve(repository, name), rel = relative(repository, filename)
      assert.ok(rel && !isAbsolute(rel) && !rel.startsWith('..'), 'Source provenance escaped repository')
      const actual = createHash('sha256').update(await readFile(filename)).digest('hex')
      assert.equal(actual, expected, `Capture is stale for ${name}; rebuild and export current source`)
    }
  }
  return { directory: root, games: index.gameCount, scenarios, responses, files, sourceHashesCurrent: requireCurrent, index, provenance }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const directory = process.argv.slice(2).find(argument => !argument.startsWith('--'))
    ?? resolve(repository, 'tools/fixtures/slot-contracts')
  const result = await verifySlotContracts(directory, { requireCurrent: process.argv.includes('--require-current') })
  console.log(JSON.stringify({ games: result.games, scenarios: result.scenarios, responses: result.responses,
    files: result.files.length, sourceHashesCurrent: result.sourceHashesCurrent }))
}
