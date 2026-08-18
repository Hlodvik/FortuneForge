import type {
  SlotSymbolDefinition,
  SlotSymbolGuideEntry,
  SlotSymbolSet,
} from '../../../features/slots/config/symbolSets'
import type { SlotSymbolId } from '../../../features/slots/types/slots'

const STATIC_THEME_SYMBOL_IDS = [
  '2',
  '3',
  '4',
  '5',
  '6',
  '7',
  'ACE',
  'FREE',
  'POWER',
  'BOLT',
  'BANANA',
  'PAW',
  'SEAL_SYNC',
  'SEAL_ROWS',
  'SEAL_PAW',
  'SEAL_RAND',
] as const satisfies readonly SlotSymbolId[]

type StaticThemeSymbolId = (typeof STATIC_THEME_SYMBOL_IDS)[number]
type CollectionSymbolId = 'SEAL_SYNC' | 'SEAL_ROWS' | 'SEAL_PAW' | 'SEAL_RAND'
type RandSymbolId = 'RAND_05' | 'RAND_1' | 'RAND_15' | 'RAND_2' | 'RAND_3' | 'RAND_4' | 'RAND_5'

type ThemedSymbolSpec = {
  image: string
  label: string
}

export type ThemedSymbolSetOptions = {
  id: string
  serverSymbolSetId: string
  symbols: Readonly<Record<StaticThemeSymbolId, ThemedSymbolSpec>>
  valueToken: ThemedSymbolSpec
  energyEarnLabel: string
  collectorFirstValue: string
  collectorSecondValue: string
  collectionAwardLabels: Readonly<Record<CollectionSymbolId, string>>
}

const randSymbols = [
  ['RAND_05', 0.5],
  ['RAND_1', 1],
  ['RAND_15', 1.5],
  ['RAND_2', 2],
  ['RAND_3', 3],
  ['RAND_4', 4],
  ['RAND_5', 5],
] as const satisfies readonly (readonly [RandSymbolId, number])[]

export function createThemedSymbolSet(options: ThemedSymbolSetOptions): SlotSymbolSet {
  const definitions: Partial<Record<SlotSymbolId, SlotSymbolDefinition>> = {}
  for (const id of STATIC_THEME_SYMBOL_IDS) {
    const symbol = options.symbols[id]
    definitions[id] = {
      id,
      label: symbol.label,
      image: symbol.image,
      animatedImage: symbol.image,
    }
  }
  for (const [id, wagerMultiplier] of randSymbols) {
    definitions[id] = {
      id,
      label: `${wagerMultiplier}× wager ${options.valueToken.label}`,
      image: options.valueToken.image,
      animatedImage: options.valueToken.image,
      wagerMultiplier,
    }
  }

  const guideEntries: SlotSymbolGuideEntry[] = [
    { symbol: '2', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '4×' },
    { symbol: '3', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '2×' },
    { symbol: '4', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '7×' },
    { symbol: '5', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '6×' },
    { symbol: '6', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '8×' },
    { symbol: '7', firstLabel: '3–4', firstValue: '3×', secondLabel: '5', secondValue: '11×' },
    { symbol: 'ACE', firstLabel: '3–4', firstValue: '5×', secondLabel: '5', secondValue: '18×' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '5 free games' },
    { symbol: 'POWER', firstLabel: '3–4', firstValue: '2× +1 point', secondLabel: '5', secondValue: '4× +2 points' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: options.energyEarnLabel },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: options.collectorFirstValue, secondLabel: '2 collectors', secondValue: options.collectorSecondValue },
    ...(['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const).map((symbol) => ({
      symbol,
      firstLabel: 'Any',
      firstValue: 'collect 40',
      secondLabel: 'Award',
      secondValue: options.collectionAwardLabels[symbol],
    })),
  ]

  return {
    id: options.id,
    serverSymbolSetId: options.serverSymbolSetId,
    definitions,
    guideEntries,
  }
}
