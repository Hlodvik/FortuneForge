// Edited: 2026-11-18T19:29:04-06:00 | Computer: Vinnabok
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { verifySlotContracts } from '../../tools/verify-slot-contracts.mjs'

if (!process.argv[2]) throw new Error('Usage: node fortuneforge.client/scripts/export-slot-guide-contracts.mjs <fresh-engine-export>')
const { directory: source, index, provenance } = await verifySlotContracts(process.argv[2], { requireCurrent: true })
const games = []
const engineFixtures = []
let basePaytable
for (const item of index.games) {
  const contract = JSON.parse(await readFile(resolve(source, item.route.id, 'contract.json'), 'utf8'))
  // Keep independently maintained paytables in the evidence without using
  // them to price the shared guide.
  const isArcane = contract.gameId === 'arcane-archives-v1'
  const isGoldFoundry = contract.gameId === 'gold-foundry-v1'
  const isNeonNights = contract.gameId === 'neon-nights-v1'
  if (!isArcane && !isGoldFoundry && !isNeonNights && !['dragon-hoard-v1', 'rainbow-realm-fruits-v1'].includes(contract.gameId)) {
    if (basePaytable && JSON.stringify(basePaytable) !== JSON.stringify(contract.paytable)) throw new Error('Per-game paytable needs explicit guide support')
    basePaytable = contract.paytable
  }
  const nativeWildMatchLengths = isGoldFoundry ? '[3,4,5]' : isArcane || isNeonNights ? '[]' : '[3,5]'
  const substitutionMatchLengths = isArcane ? '[]' : isNeonNights ? '[3,5]' : '[5]'
  if (JSON.stringify(contract.symbols.nativeWildMatchLengths) !== nativeWildMatchLengths ||
      JSON.stringify(contract.symbols.wildSubstitutionMatchLengths) !== substitutionMatchLengths) throw new Error(`Wild-match rules need guide support: ${contract.gameId}`)
  if (contract.gameId !== 'rainbow-realm-fruits-v1' && contract.math.paylinePayoutSteps.some(step => step !== 0)) throw new Error('Non-uniform payline prices need guide support')
  const p = contract.profile
  engineFixtures.push({ gameId: contract.gameId, profile: p, layout: contract.layout,
    symbols: contract.symbols, wagering: contract.wagering, math: contract.math,
    specialPoints: contract.specialPoints, paylines: contract.paylines, paytable: contract.paytable })
  games.push({
    gameId: contract.gameId, payoutMultiplier: contract.math.payoutMultiplier,
    pointValueInCents: contract.wagering.pointValueInCents,
    paylinePatternIds: p.paylinePatternIds ?? Array.from({ length: contract.layout.paylineCount }, (_, i) => i + 1),
    scatterRequiredSymbols: p.scatterRequiredSymbols, scatterAwardedSpins: p.scatterAwardedSpins,
    collectionTarget: p.collectionTarget, collectionAwardedSpins: p.collectionAwardedSpins,
    usesCollections: p.usesCollections, usesEnergy: p.usesEnergy, usesDirectValueTokens: p.usesDirectValueTokens,
    scatterFeatureMode: p.scatterFeatureMode, collectionFeatureMode: p.collectionFeatureMode,
  })
}
if (games.length !== 19) throw new Error('The engine export must contain all nineteen playable slots')
const basePayouts = {}
for (const rule of basePaytable) {
  basePayouts[rule.symbolId] ??= {}
  basePayouts[rule.symbolId][rule.matchLength === 3 ? 'three' : 'five'] = rule.multiplier
}
const output = fileURLToPath(new URL('../../packages/slot-game-runtime/src/contracts/engine-guide-contracts.json', import.meta.url))
const inlineObject = object => `{ ${Object.entries(object).map(([key, value]) =>
  `${JSON.stringify(key)}: ${JSON.stringify(value)}`).join(', ')} }`
const payouts = Object.entries(basePayouts).map(([id, values]) =>
  `    ${JSON.stringify(id)}: ${inlineObject(values)}`).join(',\n')
const guideRows = games.map(game => `    ${inlineObject(game)}`).join(',\n')
await writeFile(output, `{\n  "basePayouts": {\n${payouts}\n  },\n  "games": [\n${guideRows}\n  ]\n}\n`, 'utf8')
const fixtureDirectory = fileURLToPath(new URL('../src/games/slots/shared/__fixtures__/', import.meta.url))
await mkdir(fixtureDirectory, { recursive: true })
const fixture = {
  source: 'Actual in-process OptionsSlotsDefinitionProvider, not client manifests',
  exportedUtc: index.exportedUtc, assemblySha256: provenance.assemblySha256,
  sourceSha256: provenance.sourceSha256,
  games: engineFixtures,
}
await writeFile(resolve(fixtureDirectory, 'engineGuide-contracts.json'), `${JSON.stringify(fixture, null, 2)}\n`, 'utf8')
console.log(`Exported ${games.length} engine guide contracts to ${output}`)
