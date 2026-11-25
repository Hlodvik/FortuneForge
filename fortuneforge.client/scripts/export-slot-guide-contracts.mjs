// Edited: 2026-11-18T19:29:04-06:00 | Computer: Vinnabok
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { verifySlotGuideContracts } from '../../tools/verify-slot-contracts.mjs'

if (!process.argv[2]) throw new Error('Usage: node fortuneforge.client/scripts/export-slot-guide-contracts.mjs <fresh-engine-export>')
const { directory: source, index, provenance } = await verifySlotGuideContracts(process.argv[2])
const fixtureOnly = process.argv[3] === '--fixture-only'
if (process.argv[3] && !fixtureOnly) throw new Error('Expected optional --fixture-only')
const games = []
const engineFixtures = []
let basePaytable
function createPaylinePayoutMap(paytable) {
  const payouts = {}
  for (const rule of paytable) {
    payouts[rule.symbolId] ??= {}
    payouts[rule.symbolId][rule.matchLength === 3 ? 'three' : 'five'] = rule.multiplier
  }
  return payouts
}
for (const item of index.games) {
  const contract = JSON.parse(await readFile(resolve(source, item.route.id, 'contract.json'), 'utf8'))
  engineFixtures.push({ gameId: contract.gameId, profile: contract.profile, layout: contract.layout,
    symbols: contract.symbols, wagering: contract.wagering, math: contract.math,
    specialPoints: contract.specialPoints, paylines: contract.paylines, paytable: contract.paytable })
  if (fixtureOnly) continue
  // Keep independently maintained paytables in the evidence without using
  // them to price the shared guide.
  const isArcane = contract.gameId === 'arcane-archives-v1'
  const isGoldFoundry = contract.gameId === 'gold-foundry-v1'
  const isNeonNights = contract.gameId === 'neon-nights-v1'
  const isFifthSun = contract.gameId === 'robot-revolution-v1'
  if (!isArcane && !isGoldFoundry && !isNeonNights && !isFifthSun && !['dragon-hoard-v1', 'rainbow-realm-fruits-v1'].includes(contract.gameId)) {
    if (basePaytable && JSON.stringify(basePaytable) !== JSON.stringify(contract.paytable)) throw new Error('Per-game paytable needs explicit guide support')
    basePaytable = contract.paytable
  }
  const nativeWildMatchLengths = isGoldFoundry ? '[3,4,5]' : isArcane || isNeonNights ? '[]' : '[3,5]'
  const substitutionMatchLengths = isArcane ? '[]' : isNeonNights || isFifthSun ? '[3,5]' : '[5]'
  if (JSON.stringify(contract.symbols.nativeWildMatchLengths) !== nativeWildMatchLengths ||
      JSON.stringify(contract.symbols.wildSubstitutionMatchLengths) !== substitutionMatchLengths) throw new Error(`Wild-match rules need guide support: ${contract.gameId}`)
  if (contract.gameId !== 'rainbow-realm-fruits-v1' && contract.math.paylinePayoutSteps.some(step => step !== 0)) throw new Error('Non-uniform payline prices need guide support')
  const p = contract.profile
  games.push({
    gameId: contract.gameId, payoutMultiplier: contract.math.payoutMultiplier,
    pointValueInCents: contract.wagering.pointValueInCents,
    paylinePatternIds: p.paylinePatternIds ?? Array.from({ length: contract.layout.paylineCount }, (_, i) => i + 1),
    scatterRequiredSymbols: p.scatterRequiredSymbols, scatterAwardedSpins: p.scatterAwardedSpins,
    collectionTarget: p.collectionTarget, collectionAwardedSpins: p.collectionAwardedSpins,
    usesCollections: p.usesCollections, usesEnergy: p.usesEnergy, usesDirectValueTokens: p.usesDirectValueTokens,
    scatterFeatureMode: p.scatterFeatureMode, collectionFeatureMode: p.collectionFeatureMode,
    ...((isArcane || isFifthSun) && { paylinePayouts: createPaylinePayoutMap(contract.paytable) }),
  })
}
if (engineFixtures.length !== 17) throw new Error('The engine export must contain every current classic descriptor')
if (!fixtureOnly) {
const basePayouts = createPaylinePayoutMap(basePaytable)
const output = fileURLToPath(new URL('../../packages/slot-game-runtime/src/contracts/engine-guide-contracts.json', import.meta.url))
const inlineObject = object => `{ ${Object.entries(object).map(([key, value]) =>
  `${JSON.stringify(key)}: ${JSON.stringify(value)}`).join(', ')} }`
const payouts = Object.entries(basePayouts).map(([id, values]) =>
  `    ${JSON.stringify(id)}: ${inlineObject(values)}`).join(',\n')
const guideRows = games.map(game => `    ${inlineObject(game)}`).join(',\n')
await writeFile(output, `{\n  "basePayouts": {\n${payouts}\n  },\n  "games": [\n${guideRows}\n  ]\n}\n`, 'utf8')
}
const fixtureDirectory = fileURLToPath(new URL('../src/games/slots/shared/__fixtures__/', import.meta.url))
await mkdir(fixtureDirectory, { recursive: true })
const fixture = {
  source: 'Actual in-process OptionsSlotsDefinitionProvider, not client manifests',
  exportedUtc: index.exportedUtc, assemblySha256: provenance.assemblySha256,
  sourceHashAlgorithm: provenance.sourceHashAlgorithm,
  sourceSha256: provenance.sourceSha256,
  sourceProjects: provenance.sourceProjects,
  games: engineFixtures,
}
await writeFile(resolve(fixtureDirectory, 'engineGuide-contracts.json'), `${JSON.stringify(fixture, null, 2)}\n`, 'utf8')
console.log(`Exported ${engineFixtures.length} engine guide fixtures${fixtureOnly ? ' without changing runtime guide data' : ' and runtime guide data'}`)
