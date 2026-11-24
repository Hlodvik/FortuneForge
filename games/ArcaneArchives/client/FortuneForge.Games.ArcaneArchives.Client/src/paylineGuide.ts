import type { SlotSymbolGuideEntry } from '@fortuneforge/slot-client-core'
import contract from './assets/rules/client-rules.json' with { type: 'json' }

export const ARCANE_PAYLINE_PATTERN_IDS = contract.paylinePatternIds

/** Display multipliers from the native Arcane contract, never a client payout evaluator. */
export const ARCANE_PAYLINE_GUIDE: SlotSymbolGuideEntry[] = Object.entries(contract.paylinePayouts).map(([symbol, payout]) => ({
  symbol,
  firstLabel: '3',
  firstValue: `${Number((payout.three * contract.payoutMultiplier).toFixed(6))}×`,
  secondLabel: '5',
  secondValue: `${Number((payout.five * contract.payoutMultiplier).toFixed(6))}×`,
}))
