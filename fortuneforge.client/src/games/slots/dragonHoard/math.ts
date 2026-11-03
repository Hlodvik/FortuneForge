import type { SlotSymbolGuideEntry, SlotSymbolSet } from '../../../features/slots/config/symbolSets'
import { formatCalibratedMultiplier } from '../shared/engineGuide'

// Presentation snapshot checked against the independently exported server game.
export const MIDNIGHT_VAULT_PAYOUT_MULTIPLIER = 1
export const midnightVaultMultiplier = (value: number) =>
  formatCalibratedMultiplier(value, MIDNIGHT_VAULT_PAYOUT_MULTIPLIER)

export const MIDNIGHT_VAULT_LINE_VALUES = [
  ['3', 1, 1], ['2', 1, 2], ['4', 1, 4], ['5', 1, 4],
  ['6', 1, 4], ['7', 2, 6], ['8', 2, 8], ['ACE', 3, 10], ['POWER', 1, 2],
] as const

const LEGACY_LINE_VALUES = [
  ['3', 1, 2], ['2', 1, 4], ['4', 1, 7], ['5', 2, 6],
  ['6', 2, 8], ['7', 3, 11], ['8', 4, 14], ['ACE', 5, 18], ['POWER', 2, 4],
] as const

const PREVIOUS_LINE_VALUES = [
  ['3', 1, 2], ['2', 1, 4], ['4', 1, 7], ['5', 2, 7],
  ['6', 2, 8], ['7', 3, 11], ['8', 4, 14], ['ACE', 5, 18], ['POWER', 2, 4],
] as const

export const MIDNIGHT_VAULT_LINE_GUIDE: readonly SlotSymbolGuideEntry[] = MIDNIGHT_VAULT_LINE_VALUES.map(
  ([symbol, three, five]) => ({
    symbol, firstLabel: symbol === 'ACE' ? '3' : '3–4', firstValue: midnightVaultMultiplier(three),
    secondLabel: '5', secondValue: midnightVaultMultiplier(five),
  }),
)

export function midnightVaultGuideForModel(symbols: SlotSymbolSet, modelId?: string): SlotSymbolSet {
  const legacy = modelId === 'midnight-vault-paytable-v1'
  const previous = modelId === 'midnight-vault-paytable-v2'
  if (!legacy && !previous) return symbols
  const valuesForModel = legacy ? LEGACY_LINE_VALUES : PREVIOUS_LINE_VALUES
  const multiplier = legacy ? 1.09 : 1
  return {
    ...symbols,
    guideNote: legacy
      ? 'This previously earned round retains its original payout rules. Each award is rounded to the nearest R0.25, with half points rounded up.'
      : 'This previously earned round retains its original payout rules. Payouts use whole R0.25 points.',
    guideEntries: symbols.guideEntries.map(entry => {
      const values = valuesForModel.find(([symbol]) => symbol === entry.symbol)
      if (values) return { ...entry, firstValue: formatCalibratedMultiplier(values[1], multiplier),
        secondValue: formatCalibratedMultiplier(values[2], multiplier) }
      if (entry.symbol === 'BANANA') return { ...entry, secondValue: formatCalibratedMultiplier(3, multiplier) }
      if (legacy && entry.symbol === 'FREE') return { ...entry, secondValue: '6 Vault Breach spins' }
      if (legacy && entry.symbol.startsWith('SEAL_')) return { ...entry, secondValue: '7 enhanced Vault Breach spins' }
      return entry
    }),
  }
}
