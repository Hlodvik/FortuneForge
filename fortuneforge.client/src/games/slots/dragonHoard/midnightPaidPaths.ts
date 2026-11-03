import type { GridPosition, PaylinePayout } from '../../../features/slots/types/slots'

export type MidnightPaidPath = Readonly<{ id: string; paylineId: number; amountPoints: number; positions: readonly GridPosition[] }>

/** Presentation only: separate paid matches never gain a connecting unpaid segment. */
export function getMidnightPaidPaths(paylines: readonly PaylinePayout[]): MidnightPaidPath[] {
  const paths: MidnightPaidPath[] = []
  const seen = new Map<string, number>()
  for (const line of paylines) {
    if (!Number.isFinite(line.amountPoints) || line.amountPoints <= 0) continue
    line.matches.forEach((paid, index) => {
      const positions = paid.match.positions
      if (!Number.isFinite(paid.amountPoints) || paid.amountPoints <= 0 || positions.length < 2 ||
        positions.some(position => !Number.isInteger(position.reel) || !Number.isInteger(position.row) || position.reel < 0 || position.row < 0)) return
      const key = positions.map(position => `${position.reel}:${position.row}`).join('|')
      const existing = seen.get(key)
      if (existing !== undefined) {
        paths[existing] = { ...paths[existing], amountPoints: paths[existing].amountPoints + paid.amountPoints }
        return
      }
      seen.set(key, paths.length)
      paths.push({ id: `${line.paylineId}:${index}:${key}`, paylineId: line.paylineId, amountPoints: paid.amountPoints, positions })
    })
  }
  return paths
}
