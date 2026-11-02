export const currentMinesHouseEdgeBasisPoints = 1000
export const legacyMinesHouseEdgeBasisPoints = 100
export const currentMinesPayoutPolicyVersion = 2
export const legacyMinesPayoutPolicyVersion = 1
export const currentMinesMaximumMultiplier = 50

export const isSupportedMinesHouseEdge = (value: unknown): value is number =>
  value === legacyMinesHouseEdgeBasisPoints || value === currentMinesHouseEdgeBasisPoints

export const isSupportedMinesPayoutPolicy = (value: unknown): value is number =>
  value === legacyMinesPayoutPolicyVersion || value === currentMinesPayoutPolicyVersion

const choose = (n: number, k: number): bigint => {
  let value = 1n
  const depth = Math.min(k, n - k)
  for (let i = 1; i <= depth; i++) value = value * BigInt(n - depth + i) / BigInt(i)
  return value
}
const rawFraction = (mines: number, picks: number, edge: number) => ({
  numerator: BigInt(10000 - edge) * choose(25, picks),
  denominator: 10000n * choose(25 - mines, picks),
})

// Informational quotes only. All wallet amounts and mine returns come from the server.
// Integer rational interpolation avoids intermediate quote-rounding before the final division.
export function minesMultiplierQuote(mines: number, picks: number,
  houseEdgeBasisPoints = currentMinesHouseEdgeBasisPoints,
  payoutPolicyVersion = currentMinesPayoutPolicyVersion): number {
  const original = rawFraction(mines, picks, houseEdgeBasisPoints)
  if (payoutPolicyVersion === legacyMinesPayoutPolicyVersion)
    return Number(original.numerator) / Number(original.denominator)
  const safe = 25 - mines
  let anchor = 0
  let fraction = rawFraction(mines, 0, houseEdgeBasisPoints)
  for (let depth = 1; depth <= safe; depth++) {
    const value = rawFraction(mines, depth, houseEdgeBasisPoints)
    if (value.numerator > BigInt(currentMinesMaximumMultiplier) * value.denominator) break
    anchor = depth; fraction = value
  }
  if (picks <= anchor || anchor === safe)
    return Number(original.numerator) / Number(original.denominator)
  const steps = BigInt(safe - anchor), progress = BigInt(picks - anchor)
  const numerator = fraction.numerator * steps
    + (BigInt(currentMinesMaximumMultiplier) * fraction.denominator - fraction.numerator) * progress
  return Number(numerator) / Number(fraction.denominator * steps)
}
