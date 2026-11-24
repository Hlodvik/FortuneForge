export type ArcaneWinPresentationTier = 'win' | 'big' | 'jackpot'

/** Celebrates the server's settled award without affecting its value. */
export function getArcaneWinPresentationTier(awardRand: number, wagerRand: number): ArcaneWinPresentationTier {
  if (awardRand >= Math.max(500, wagerRand * 50)) return 'jackpot'
  return awardRand >= Math.max(25, wagerRand * 10) ? 'big' : 'win'
}

export function getArcaneWinOutcomeKind(awardRand: number, wagerRand: number, freeSpinsAwarded: number) {
  if (freeSpinsAwarded > 0) return 'bonus'
  if (awardRand <= 0) return 'loss'
  if (awardRand >= Math.max(500, wagerRand * 50)) return 'big-win'
  return awardRand >= wagerRand * 10 ? 'great-win' : 'win'
}
