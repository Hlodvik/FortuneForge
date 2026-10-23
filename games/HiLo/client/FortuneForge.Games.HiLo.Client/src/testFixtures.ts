import type { HiLoSession } from './contracts'

export const hiLoSessionFixture: HiLoSession = {
  practice: true,
  walletBalanceCents: 999900,
  limits: { minWagerCents: 10, maxWagerCents: 100000, maximumReturnCents: 10000000, maxMultiplier: 1000, maxSkips: 52, maxStreak: 100, houseEdge: .01 },
  round: {
    roundId: '00000000000000000000000000000001', revision: 0, status: 'active', wagerCents: 100,
    currentCard: { rank: 'seven', suit: 'hearts' },
    history: [{ card: { rank: 'seven', suit: 'hearts' }, choice: 'start', won: null }],
    currentReturnCents: 100, multiplier: 1, streak: 0, skips: 0,
    higher: { probability: 6 / 13, multiplier: 2.145, projectedReturnCents: 214, available: true },
    lower: { probability: 6 / 13, multiplier: 2.145, projectedReturnCents: 214, available: true },
    canCashOut: false, canSkip: true,
  },
}
