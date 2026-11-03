import type { SlotSymbolDefinition } from './config/symbolSets'

export const creditFormatter = new Intl.NumberFormat('en-US')

export const formatRand = (amount: number) => `R${creditFormatter.format(amount)}`

export const slotPointsToRand = (points: number, pointValueInCents: number) =>
  points * pointValueInCents / 100

export const getSlotSymbolValueLabel = (
  definition: SlotSymbolDefinition,
  wager: number,
  pointValueInCents?: number,
) => {
  const multiplier = definition.wagerMultiplier
  if (multiplier === undefined) return definition.valueLabel
  if (pointValueInCents === undefined) return formatRand(wager * multiplier)

  const wagerPoints = Math.round(wager * 100 / pointValueInCents)
  return formatRand(slotPointsToRand(Math.round(wagerPoints * multiplier), pointValueInCents))
}
