import type { HoldemTable } from './contracts'

/** A pot-sized raise includes the viewer's call before measuring the pot. */
export function holdemPotRaiseTo(table: HoldemTable, fraction: 0.5 | 1): number {
  const maximum = Math.max(0, Math.round(table.maximumRaiseTo))
  const minimum = Math.min(maximum, Math.max(0, Math.round(table.shortAllInRaiseTo ?? table.minimumRaiseTo)))
  const viewer = table.seats.find(seat => seat.isCurrentPlayer)
  if (!viewer) return minimum

  const call = Math.max(0, Math.min(table.currentBet - viewer.committedRound, viewer.stack))
  const committedAfterCall = viewer.committedRound + call
  const potAfterCall = table.pot + call
  const target = committedAfterCall + Math.round(potAfterCall * fraction)
  return Math.max(minimum, Math.min(maximum, Number.isFinite(target) ? Math.round(target) : minimum))
}
