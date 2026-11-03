import { createShowcaseSlotGame } from '../shared/createShowcaseSlotGame'
import type { SlotSymbolGuideEntry } from '../../../features/slots/config/symbolSets'
import type { SlotSymbolId } from '../../../features/slots/types/slots'
import wordmark from '../../../assets/slots/games/neon-nights/optimized/neon-nights-wordmark-v1.webp'
import { NEON_NIGHTS_DEFINITION } from './definition'
import './neonNightsLayout.css'

const base = createShowcaseSlotGame(NEON_NIGHTS_DEFINITION)
const activeSymbols = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BANANA',
  'SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND',
] as const satisfies readonly SlotSymbolId[]
const animationWeights: Record<typeof activeSymbols[number], number> = {
  '2': 197, '3': 300, '4': 197, '5': 197, '6': 1, '7': 197,
  ACE: 80, FREE: 8, POWER: 1, BANANA: 101,
  SEAL_SYNC: 256, SEAL_ROWS: 256, SEAL_PAW: 256, SEAL_RAND: 1,
}
const animationPool = activeSymbols.flatMap(symbol => Array.from({ length: animationWeights[symbol] }, () => symbol))
const animationSymbols = animationPool.map((_, index) => animationPool[(index * 811 + 331) % animationPool.length])
const linePayouts = [
  ['2', 1, 4], ['3', 1, 2], ['4', 1, 7], ['5', 2, 6], ['7', 3, 11],
  ['ACE', 5, 18], ['SEAL_SYNC', 1, 4], ['SEAL_ROWS', 1, 5], ['SEAL_PAW', 2, 8],
] as const

// These IDs have Neon-specific roles; shared collection/scatter help does not apply.
const guideEntries: SlotSymbolGuideEntry[] = [
  ...linePayouts.map(([symbol, three, five]) => ({
    symbol, firstLabel: '3', firstValue: `${three}×`, secondLabel: '5', secondValue: `${five}×`,
  })),
  { symbol: 'FREE', firstLabel: 'Each BONUS', firstValue: '+25% to payline wins',
    secondLabel: 'Stacks', secondValue: '2 BONUS = +50%' },
  { symbol: 'BANANA', firstLabel: '3', firstValue: 'row / column / diagonal', secondLabel: 'Pays', secondValue: '3× per pattern' },
  ...(['6', 'POWER', 'SEAL_RAND'] as const).map(symbol => ({
    symbol, firstLabel: 'Special Plays', firstValue: '7 bonus spins',
    secondLabel: 'Trigger', secondValue: 'Land anywhere, including during Special Plays',
  })),
]

export const NEON_NIGHTS_SLOT_GAME = {
  ...base,
  experience: {
    ...base.experience,
    id: 'neon-nights-arcade-experience-v2',
    cabinet: {
      ...base.experience.cabinet,
      titleArtwork: { src: wordmark, width: 720, height: 415 },
    },
    rules: {
      ...base.experience.rules,
      pointValueInCents: 1,
      wagerOptions: Array.from({ length: 1_000 }, (_, index) => (index + 1) * 50),
    },
    symbols: {
      ...base.experience.symbols,
      id: 'neon-nights-arcade-symbols-v2',
      animationSymbolIds: animationSymbols,
      guideEntries,
      guideNote: 'BONUS symbols add 25% each to this spin’s payline wins. The combined boost is rounded once to the nearest cent; displayed line awards include it.',
    },
    help: {
      ...base.experience.help,
      freeGames: undefined,
      extraSections: [
        { badge: '+25%', title: 'BONUS boosts', body: 'Each BONUS sign anywhere on the board adds 25% to the spin’s payline wins. Two signs add 50%, three add 75%, and so on. The boost applies to paying lines only; it does not multiply triple-sevens pattern prizes or award free spins.' },
        { badge: '777', title: 'Triple sevens', body: 'Three triple-sevens symbols in a row, column or diagonal pay 3× the wager per qualifying pattern.' },
        { badge: '7', title: 'Special Plays', body: 'Land a sports car, disco ball or arcade cabinet anywhere to win 7 bonus spins. During Special Plays, those spins are added to the same round at the original wager. Each spin can award one set of 7.' },
      ],
    },
  },
}
