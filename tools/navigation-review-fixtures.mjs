import { readFileSync } from 'node:fs'

export const paths = { house: '/cards/house-holdem', texas: '/cards/texas-holdem', hearts: '/cards/hearts' }
export const account = { userId: 'navigation-local-player', playerName: 'Local player', email: 'local@example.test', role: 'player',
  createdAtUtc: '2026-01-01T00:00:00Z', balances: { slotsCredits: 1000, freeGames: 0 },
  slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } }
export const slotCapture = JSON.parse(readFileSync(new URL('./fixtures/slot-contracts/arcane-archives/base-win.json', import.meta.url), 'utf8')).steps[0]

export function houseRound(mode) {
  return { roundId: 'local-open-hand', balance: 998, phase: mode === 'result' ? 'settled' : 'flop', ante: 1, blind: 1,
    trips: 0, play: 3, totalWager: 5, payout: 0, playerCards: [{ rank: 'ace', suit: 'spades' }, { rank: 'king', suit: 'hearts' }],
    dealerCards: mode === 'result' ? [{ rank: 'queen', suit: 'spades' }, { rank: 'queen', suit: 'clubs' }] : null,
    board: [{ rank: '2', suit: 'clubs' }, { rank: '3', suit: 'clubs' }, { rank: '4', suit: 'hearts' }],
    playerHand: null, dealerHand: null, result: mode === 'result' ? 'dealer-win' : null,
    allowedActions: [], canAdvance: false, payoutBreakdown: mode === 'result' ? { ante: 0, blind: 0, trips: 0, play: 0 } : null }
}
export function texasSession(mode, seatStatus = 'folded') {
  const base = { contractVersion: 'cards.texas-holdem.credit.v2', kind: 'idle', version: 1 }
  if (mode === 'idle') return base
  if (mode === 'queue') return { ...base, kind: 'queue', ticketId: 'reserved-entry', position: 1,
    joinedAtUtc: new Date().toISOString(), humanGraceEndsAtUtc: new Date(Date.now() + 60000).toISOString(), players: [] }
  const table = { matchId: 'accepted-match', status: mode === 'result' ? 'completed' : 'active', street: 'flop', handNumber: 2,
    dealerSeat: 1, activeSeat: mode === 'result' ? -1 : 1, pot: 100, currentBet: 100, minimumRaiseTo: 200, maximumRaiseTo: 3250,
    communityCards: [], legalActions: [], winningSeatIds: [], winningAmount: 0, startedAtUtc: '', matchDeadlineAtUtc: '',
    remainingActionMilliseconds: 30000, seats: [{ seatId: 'viewer', displayName: 'Local player', seat: 0, startingStack: 3250,
      stack: 3250, committed: 100, committedRound: 100, status: seatStatus, holeCards: [{ hidden: true }, { hidden: true }], isCurrentPlayer: true }] }
  return mode === 'result' ? { ...base, kind: 'result', matchId: table.matchId, handNumber: 2, humanCommittedCredits: 1,
    humanPayoutCredits: 0, houseNetCredits: 1, startedAtUtc: '', completedAtUtc: '', standings: [], finalTable: table }
    : { ...base, kind: 'match', table }
}
export function heartsSession(mode, betweenRounds = false) {
  const base = { kind: 'idle', version: 1, balanceCredits: 1000, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null }
  if (mode === 'idle') return base
  if (mode === 'queue') return { ...base, kind: 'queue', queue: { matchId: 'hearts-queue', entryCredits: 1, potCredits: 1,
    joinedAtUtc: '', players: [{ seat: 'north', displayName: 'Local player', isCurrentPlayer: true }], message: '' } }
  const seats = ['north', 'east', 'south', 'west']
  const card = { code: '2|clubs', rank: '2', suit: 'clubs', label: '2♣' }
  const match = { matchId: 'hearts-match', targetScore: 100, roundNumber: 1,
    phase: betweenRounds || mode === 'result' ? 'complete' : 'playing', passDirection: 'left', turn: 'east', humanSeat: 'north',
    yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: seats,
    players: seats.map(seat => ({ seat, displayName: seat, handCount: 1, roundScore: 0, matchScore: 0, hasPassed: true })),
    hand: [card], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [],
    score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 },
    difficulty: 'standard', winner: mode === 'result' ? 'north' : null, message: '' }
  return { ...base, kind: mode === 'result' ? 'result' : 'match', version: 2, table: { match, entryCredits: 1, potCredits: 4,
    payoutCredits: mode === 'result' ? 4 : 0, winnerSeats: mode === 'result' ? ['north'] : [],
    players: seats.map(seat => ({ seat, displayName: seat, isCurrentPlayer: seat === 'north', hasLeft: false, entryCredits: 1, payoutCredits: 0 })),
    actionDeadlineAtUtc: null, completedAtUtc: mode === 'result' ? new Date().toISOString() : null } }
}

