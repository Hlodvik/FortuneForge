import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { verifySlotContracts } from './verify-slot-contracts.mjs'

if (!process.argv[2]) throw new Error('Usage: node tools/import-slot-contracts.mjs <fresh-engine-export> [--apply]')
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const capture = await verifySlotContracts(process.argv[2], { requireCurrent: true })
const records = []
const sha = value => createHash('sha256').update(value).digest('hex')
for (const filename of capture.files) {
  const bytes = await readFile(resolve(capture.directory, filename))
  records.push({ path: `tools/fixtures/slot-contracts/${filename}`, bytes, sha256: sha(bytes) })
}
const midnight = capture.index.games.find(game => game.route.gameId === 'dragon-hoard-v1')
assert.ok(midnight, 'Actual Midnight Vault engine contract is required')
for (const scenario of ['base-loss', 'base-win', 'scatter', 'collection-sync', 'collection-rows', 'collection-paw', 'collection-rand']) {
  assert.ok(midnight.scenarios.some(item => item.scenario === scenario), `Missing actual Midnight scenario ${scenario}`)
  const bytes = await readFile(resolve(capture.directory, midnight.route.id, `${scenario}.json`))
  records.push({ path: `tools/fixtures/midnight-vault/${scenario}.json`, bytes, sha256: sha(bytes) })
}
const contract = JSON.parse(await readFile(resolve(capture.directory, midnight.route.id, 'contract.json'), 'utf8'))
assert.equal(contract.symbolSet?.id, contract.symbols.symbolSetId, 'Actual Midnight symbol-set metadata is required')
assert.equal(contract.symbolSet.symbols.length, 15, 'Midnight requires all fifteen actual symbol definitions')
const presentationBytes = Buffer.from(JSON.stringify({ ...contract, schemaVersion: 2,
  sourceSha256: capture.provenance.sourceSha256, assemblySha256: capture.provenance.assemblySha256,
}, null, 2) + '\n')
records.push({ path: 'fortuneforge.client/src/games/slots/dragonHoard/evidence/current-engine-contract.json',
  bytes: presentationBytes, sha256: sha(presentationBytes) })
for (const [name, value] of Object.entries({
  'engine-contract': { schemaVersion: 1, sourceSha256: capture.provenance.sourceSha256,
    assemblySha256: capture.provenance.assemblySha256, contract },
  index: { schemaVersion: 1, route: midnight.route, scenarios: midnight.scenarios,
    source: 'Actual current in-process server demo capture; see engine-contract.json' },
})) {
  const bytes = Buffer.from(JSON.stringify(value, null, 2) + '\n')
  records.push({ path: `tools/fixtures/midnight-vault/${name}.json`, bytes, sha256: sha(bytes) })
}
if (process.argv.includes('--apply')) {
  // All source identities, paths, public money sums and required scenarios have
  // been checked before any versioned input is replaced. Original captures stay.
  for (const record of records) {
    const filename = resolve(repository, record.path)
    await mkdir(dirname(filename), { recursive: true })
    await writeFile(filename, record.bytes)
  }
}
console.log(JSON.stringify({ applied: process.argv.includes('--apply'), sourceHashesCurrent: true,
  games: capture.games, scenarios: capture.scenarios, files: records.map(({ path, sha256 }) => ({ path, sha256 })) }, null, 2))
