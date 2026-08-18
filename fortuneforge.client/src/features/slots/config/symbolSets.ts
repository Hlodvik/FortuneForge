import type { SlotSymbolId } from '../types/slots'

export type SlotSymbolDefinition = {
  id: SlotSymbolId
  label: string
  image: string
  animatedImage?: string
  valueLabel?: string
  wagerMultiplier?: number
}

export type SlotSymbolGuideEntry = {
  symbol: SlotSymbolId
  firstLabel: string
  firstValue: string
  secondLabel?: string
  secondValue?: string
}

export type SlotSymbolSet = {
  id: string
  serverSymbolSetId?: string
  definitions: Readonly<Partial<Record<SlotSymbolId, SlotSymbolDefinition>>>
  guideEntries: readonly SlotSymbolGuideEntry[]
}

export function getSlotSymbolDefinition(
  symbolSet: SlotSymbolSet,
  symbol: SlotSymbolId,
): SlotSymbolDefinition {
  const definition = symbolSet.definitions[symbol]
  if (!definition) {
    throw new Error(`Slot symbol '${symbol}' is not defined by '${symbolSet.id}'.`)
  }
  return definition
}
