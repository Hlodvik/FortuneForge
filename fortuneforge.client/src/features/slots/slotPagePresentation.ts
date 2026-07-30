import type { SlotSymbolDefinition } from './config/symbolSets'

export const creditFormatter = new Intl.NumberFormat('en-US')

export const formatRand = (amount: number) => `R${creditFormatter.format(amount)}`

export const getSlotSymbolValueLabel = (
  definition: SlotSymbolDefinition,
  wager: number,
) => definition.wagerMultiplier === undefined
  ? definition.valueLabel
  : formatRand(wager * definition.wagerMultiplier)
