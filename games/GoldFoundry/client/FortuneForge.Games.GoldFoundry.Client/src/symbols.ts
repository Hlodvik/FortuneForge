import type { SlotSymbolSet } from '@fortuneforge/slot-client-core'
import type { GoldFoundrySymbolId } from './types.js'
import { GOLD_FOUNDRY_VISUALS as art } from './visuals.js'

type SymbolArt = readonly [symbol: GoldFoundrySymbolId, label: string, image: string]
type PayingSymbol = readonly [...SymbolArt, three: number, four: number, five: number]

const ingots = [
  ['SEAL_ROWS', 'Iron ingot', art.ironIngot, 0.25, 0.5, 1.5],
  ['SEAL_SYNC', 'Bronze ingot', art.bronzeIngots, 0.3, 0.75, 2.5],
  ['SEAL_PAW', 'Copper ingot', art.copperIngots, 0.4, 1, 3.5],
  ['SEAL_RAND', 'Gold ingot', art.goldIngots, 0.6, 1.5, 5],
] as const satisfies readonly PayingSymbol[]

export const FOUNDRY_INGOT_IDS = ingots.map(([id]) => id)

const collectorWagerMultipliers: Partial<Record<GoldFoundrySymbolId, number>> = {
  SEAL_ROWS: 0.5,
  SEAL_SYNC: 1,
  SEAL_PAW: 1.5,
  SEAL_RAND: 2,
}

const payingSymbols = [
  ...ingots,
  ['2', 'Steel queen', art.queen, 0.1, 0.3, 1],
  ['3', 'Copper jack', art.jack, 0.1, 0.35, 1.1],
  ['4', 'Bronze king', art.king, 0.15, 0.4, 1.3],
  ['5', 'Golden ace', art.ace, 0.15, 0.45, 1.5],
  ['6', 'Smithing tongs', art.tongs, 0.2, 0.5, 1.8],
  ['7', 'Forge bellows', art.bellows, 0.25, 0.6, 2],
  ['FREE', 'Anvil', art.anvil, 0.25, 0.65, 2.2],
  ['POWER', 'Gold ore', art.ore, 0.3, 0.75, 2.4],
  ['ACE', 'Master Smith wild', art.smithMask, 0.8, 2, 7],
] as const satisfies readonly PayingSymbol[]

const featureSymbols = [
  ['BOLT', 'Payback ingot bundle', art.paybackIngotBundle],
  ['BANANA', 'Smithing hammer', art.hammer],
  ['PAW', 'Reel crucible', art.crucible],
] as const satisfies readonly SymbolArt[]

export const GOLD_FOUNDRY_SYMBOLS: SlotSymbolSet<GoldFoundrySymbolId> = {
  id: 'gold-foundry-symbols-v1',
  animationSymbolIds: [...payingSymbols, ...featureSymbols].map(([id]) => id),
  guideSubtitle: '3, 4, and 5 in a row',
  definitions: Object.fromEntries([...payingSymbols, ...featureSymbols].map(([id, label, image]) => {
    const wagerMultiplier = collectorWagerMultipliers[id]
    return [id, {
      id,
      label,
      image,
      animatedImage: image,
      ...(wagerMultiplier === undefined ? {} : { wagerMultiplier }),
    }]
  })),
  guideEntries: [
    ...payingSymbols.map(([symbol, , , three, four, five]) => ({
      symbol, firstLabel: '3 / 4', firstValue: `${three}× / ${four}×`, secondLabel: '5', secondValue: `${five}×`,
    })),
    { symbol: 'PAW', firstLabel: '1 crucible', firstValue: 'collects every visible ingot', secondLabel: '2+ crucibles', secondValue: 'double the haul' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: 'charge for the filling crucible' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row, column, or diagonal', secondLabel: 'Pays', secondValue: '3× the wager' },
  ],
  guideNote: 'Line multipliers apply to your wager. Each line match is rounded to the nearest R0.25, with halfway amounts rounded up. The reel crucible pays for visible ingots separately, including ingots that also win on a line. The Master Smith substitutes only in five-symbol matches.',
}
