// Edited: 2026-11-18T14:56:58-06:00 | Computer: Vinnabok
export function getPaylineWinTimings(tier: 'win' | 'big' | 'jackpot', fast: boolean) {
  const speed = fast ? 1.35 : 1
  const hold = (tier === 'jackpot' ? 2400 : tier === 'big' ? 1500 : 820) / speed
  return {
    hold,
    count: tier === 'win' ? hold * 0.72 : hold,
    flight: 540 / speed,
    balanceCount: (tier === 'win' ? 420 : 720) / speed,
  }
}
