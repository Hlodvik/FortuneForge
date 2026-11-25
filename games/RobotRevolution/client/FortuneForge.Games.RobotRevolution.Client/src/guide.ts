import document from './assets/contracts/guide.json?raw'
import type { SlotSymbolGuideEntry } from '@fortuneforge/slot-client-core'
/** Frozen presentation contract exported from Sunstone's server; never authorizes an award. */
export const guide = JSON.parse(document) as {
  gameId: string; payoutMultiplier: number; pointValueInCents: number; paylinePatternIds: number[]
  scatterRequiredSymbols: number; scatterAwardedSpins: number; collectionTarget: number; collectionAwardedSpins: number
  paylinePayouts: Record<string, { three: number; five: number }>
}
export const formatCalibratedMultiplier = (base: number, calibration: number) => `${Number((base * calibration).toFixed(6))}×`
export function createSunstonePaylineGuide(symbols: readonly string[]): SlotSymbolGuideEntry[] {
  return symbols.map(symbol => ({ symbol, firstLabel: symbol === 'WILD' ? '3' : '3–4',
    firstValue: formatCalibratedMultiplier(guide.paylinePayouts[symbol].three, guide.payoutMultiplier),
    secondLabel: '5', secondValue: formatCalibratedMultiplier(guide.paylinePayouts[symbol].five, guide.payoutMultiplier) }))
}
