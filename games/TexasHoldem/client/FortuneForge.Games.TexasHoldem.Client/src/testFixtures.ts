import type { HoldemTable, HoldemTableRule } from './contracts'

export const testRule: HoldemTableRule = {
  id: 'standard', name: 'Standard', description: 'Automatic blinds · no ante',
  smallBlindCredits: .5, bigBlindCredits: 1, anteCredits: 0, maximumTableStackCredits: 100,
}
export const testTable: HoldemTable = {
  matchId: 'ui-test-table', status: 'active', street: 'flop', handNumber: 2,
  dealerSeat: 1, activeSeat: 0, pot: 1000, currentBet: 200,
  minimumRaiseTo: 400, maximumRaiseTo: 2500, shortAllInRaiseTo: null,
  communityCards: [], winningSeatIds: [], winningAmount: 0,
  startedAtUtc: '2026-10-24T00:00:00Z', matchDeadlineAtUtc: '2026-10-24T01:00:00Z',
  remainingActionMilliseconds: 30000, tableRule: testRule,
  seats: [{ seatId: 'viewer', displayName: 'Player', seat: 0, startingStack: 2500, stack: 2400,
    committed: 100, committedRound: 100, status: 'active', holeCards: [{ hidden: true }, { hidden: true }], isCurrentPlayer: true },
  { seatId: 'opponent', displayName: 'Opponent', seat: 1, startingStack: 2500, stack: 2300,
    committed: 200, committedRound: 200, status: 'active', holeCards: [{ hidden: true }, { hidden: true }], isCurrentPlayer: false }],
  legalActions: ['fold', 'call', 'raise'],
}
