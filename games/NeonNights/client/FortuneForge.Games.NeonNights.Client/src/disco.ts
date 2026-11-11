import type { SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { NEON_NIGHTS_SYMBOL_IMAGES as images } from './symbols.js'
import { NEON_DISCO_SYMBOL_SET_ID, type NeonDiscoSymbolId } from './types.js'

export const discoClusterValues = [
  ['2', 'Jack', 275], ['4', 'Queen', 280], ['5', 'King', 286], ['7', 'Ace', 291],
  ['DISCO_MARTINI', 'Martini', 302], ['DISCO_SEVEN', 'Neon seven', 313], ['DISCO_BALL', 'Neon disco ball', 324],
] as const
export const discoClusterSizes = Array.from({ length: 17 }, (_, index) => index + 4)
export const discoAwardPercent = (fourSymbolAwardPercent: number, size: number) =>
  Math.floor(fourSymbolAwardPercent * (100 + 5 * (size - 4)) / 100)
export const neonDiscoSymbols: SlotSymbolSet<NeonDiscoSymbolId> = {
  id: NEON_DISCO_SYMBOL_SET_ID, serverSymbolSetId: NEON_DISCO_SYMBOL_SET_ID,
  definitions: Object.fromEntries(discoClusterValues.map(([id, label]) => [id, { id, label, image: images[id] }])),
  animationSymbolIds: discoClusterValues.map(([id]) => id),
  guideSubtitle: '4 symbols · 8 symbols · at ×1',
  guideSymbolGroups: [['2', '4', '5', '7']],
  guideEntries: discoClusterValues.map(([symbol, , fourSymbolAwardPercent]) => ({
    symbol, compactInSidePanel: true, firstLabel: '4', firstValue: `${fourSymbolAwardPercent / 100}×`,
    secondLabel: '8', secondValue: `${discoAwardPercent(fourSymbolAwardPercent, 8) / 100}×`,
  })),
  guideNote: 'Match 4+ touching horizontally or vertically. Larger groups pay more. Cascade multiplier applies; see How to win for all sizes.',
}

export const discoInitialReels: NeonDiscoSymbolId[][] = [
  ['2', 'DISCO_MARTINI', '4', 'DISCO_BALL'],
  ['5', '4', 'DISCO_SEVEN', '7'],
  ['DISCO_BALL', '7', '2', 'DISCO_MARTINI'],
  ['4', 'DISCO_SEVEN', '5', '2'],
  ['7', 'DISCO_MARTINI', 'DISCO_BALL', '5'],
]
