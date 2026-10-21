import type { HeartsCard, HeartsGateway, HeartsMatch, HeartsPaidSession, HeartsPaidTable, HeartsSeat } from '../contracts'
import { seatsClockwise } from '../heartsHelpers'

/** Development-only interactive table. Never imported by the package or production route. */
export function createPreviewGateway(): HeartsGateway {
  let match: HeartsMatch | null = null
  let hands = {} as Record<HeartsSeat, HeartsCard[]>
  let wallet = 250
  let paid: HeartsPaidSession = { kind: 'idle', version: 0, balanceCredits: wallet, entryOptions: [1, 2, 5, 10, 25], queue: null, table: null }
  let joined = 0
  let lastTurn = 0
  let onboardingPending = true
  let trialActive = false
  const names = ['You', 'Rowan', 'NightOwl', 'Clover']
  const past: HeartsPaidTable[] = []
  const delay = async () => new Promise<void>(resolve => setTimeout(resolve, 180))
  const rank = (card: HeartsCard) => ({ A: 14, K: 13, Q: 12, J: 11 } as Record<string, number>)[card.label.slice(0, -1)] ?? Number(card.label.slice(0, -1))
  const points = (card: HeartsCard) => card.suit === 'hearts' ? 1 : card.code === 'Q|spades' ? 13 : 0
  function legal(seat: HeartsSeat): HeartsCard[] {
    const hand = hands[seat]
    if (!match || !hand) return []
    const led = match.currentTrick.plays[0]?.card.suit
    if (led) {
      const suited = hand.filter(card => card.suit === led)
      if (suited.length) return suited
      const safe = hand.filter(card => points(card) === 0)
      return match.completedTricks.length === 0 && safe.length ? safe : hand
    }
    if (match.completedTricks.length === 0) return hand.filter(card => card.code === '2|clubs')
    const safe = hand.filter(card => card.suit !== 'hearts')
    return !match.heartsBroken && safe.length ? safe : hand
  }
  function snapshot() {
    if (!match) throw new Error('Open a table first.')
    match = { ...match, hand: [...hands.north], legalCards: match.phase === 'playing' && match.turn === 'north' ? legal('north') : [], yourTurn: match.phase === 'playing' && match.turn === 'north', opponentsThinking: match.phase === 'playing' && match.turn !== 'north', players: seatsClockwise.map((seat, index) => ({ seat, displayName: trialActive && index > 0 ? `Trial player ${index}` : names[index], handCount: hands[seat].length, hasPassed: match!.submittedPasses.includes(seat), roundScore: match!.roundScore[seat], matchScore: match!.score[seat] })) }
    return structuredClone(match)
  }
  function start(round = 1, scores = { north: 0, east: 0, south: 0, west: 0 }) {
    const suits = ['clubs', 'diamonds', 'spades', 'hearts']
    const symbols = ['♣', '♦', '♠', '♥']
    const ranks = ['2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K', 'A']
    const deck = suits.flatMap((suit, index) => ranks.map(rank => ({ code: `${rank}|${suit}`, rank: ({ A: 'ace', K: 'king', Q: 'queen', J: 'jack' } as Record<string, string>)[rank] ?? rank, suit, label: rank + symbols[index] })))
    for (let index = deck.length - 1; index > 0; index--) { const target = Math.floor(Math.random() * (index + 1)); [deck[index], deck[target]] = [deck[target], deck[index]] }
    seatsClockwise.forEach((seat, index) => { hands[seat] = deck.slice(index * 13, index * 13 + 13).sort((a, b) => suits.indexOf(a.suit) - suits.indexOf(b.suit) || rank(a) - rank(b)) })
    const passDirection = (['left', 'right', 'across', 'hold'] as const)[(round - 1) % 4]
    const turn = seatsClockwise.find(seat => hands[seat].some(card => card.code === '2|clubs'))!
    match = { matchId: round === 1 ? crypto.randomUUID() : match!.matchId, targetScore: 100, roundNumber: round, phase: passDirection === 'hold' ? 'playing' : 'passing', passDirection, turn, humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: [], hand: hands.north, legalCards: [], currentTrick: { leader: turn, plays: [] }, recentTrick: null, completedTricks: [], score: scores, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '', isTrial: trialActive }
    return snapshot()
  }
  function pass(cards: readonly string[]) {
    if (!match || match.phase !== 'passing' || cards.length !== 3 || new Set(cards).size !== 3 || !cards.every(code => hands.north.some(card => card.code === code))) throw new Error('Choose three cards from your hand.')
    const passes = seatsClockwise.map(seat => seat === 'north' ? hands[seat].filter(card => cards.includes(card.code)) : [...hands[seat]].sort((a, b) => points(b) - points(a) || rank(b) - rank(a)).slice(0, 3))
    const distance = match.passDirection === 'left' ? 1 : match.passDirection === 'right' ? 3 : 2
    seatsClockwise.forEach((seat, index) => { hands[seat] = hands[seat].filter(card => !passes[index].includes(card)) })
    seatsClockwise.forEach((_, index) => { hands[seatsClockwise[(index + distance) % 4]].push(...passes[index]) })
    const turn = seatsClockwise.find(seat => hands[seat].some(card => card.code === '2|clubs'))!
    match = { ...match, phase: 'playing', turn, currentTrick: { leader: turn, plays: [] }, submittedPasses: [...seatsClockwise] }
    lastTurn = Date.now()
    return snapshot()
  }
  function play(card: string) {
    if (!match || match.phase !== 'playing') throw new Error('The table is not ready for a card.')
    const seat = match.turn
    const selected = legal(seat).find(item => item.code === card)
    if (!selected) throw new Error('Follow the led suit when you can.')
    hands[seat] = hands[seat].filter(item => item.code !== card)
    const plays = [...match.currentTrick.plays, { seat, card: selected }]
    match = { ...match, heartsBroken: match.heartsBroken || selected.suit === 'hearts', currentTrick: { ...match.currentTrick, plays }, turn: seatsClockwise[(seatsClockwise.indexOf(seat) + 1) % 4] }
    if (plays.length === 4) {
      const winning = plays.filter(item => item.card.suit === plays[0].card.suit).sort((a, b) => rank(b.card) - rank(a.card))[0]
      const trick = { number: match.completedTricks.length + 1, leader: match.currentTrick.leader, plays, winner: winning.seat, points: plays.reduce((total, item) => total + points(item.card), 0) }
      const roundScore = { ...match.roundScore, [winning.seat]: match.roundScore[winning.seat] + trick.points }
      match = { ...match, roundScore, recentTrick: trick, completedTricks: [...match.completedTricks, trick], currentTrick: { leader: winning.seat, plays: [] }, turn: winning.seat }
      if (hands.north.length === 0) {
        const moon = seatsClockwise.find(seat => roundScore[seat] === 26)
        const score = { ...match.score }
        for (const seat of seatsClockwise) score[seat] += moon ? seat === moon ? 0 : 26 : roundScore[seat]
        const winner = seatsClockwise.some(seat => score[seat] >= 100) ? [...seatsClockwise].sort((a, b) => score[a] - score[b])[0] : null
        match = { ...match, phase: 'complete', score, winner }
        if (trialActive) onboardingPending = false
      }
    }
    lastTurn = Date.now()
    return snapshot()
  }
  function updatePaid(): HeartsPaidSession {
    if (paid.kind === 'queue') {
      const count = Math.min(4, 1 + Math.floor((Date.now() - joined) / 2400))
      paid = { ...paid, queue: { ...paid.queue!, players: seatsClockwise.slice(0, count).map((seat, index) => ({ seat, displayName: names[index], isCurrentPlayer: index === 0 })), potCredits: paid.queue!.entryCredits * count } }
      if (count === 4) {
        trialActive = false
        const current = start(), entryCredits = paid.queue!.entryCredits
        paid = { ...paid, kind: 'match', version: paid.version + 1, queue: null, table: { match: current, entryCredits, potCredits: entryCredits * 4, payoutCredits: 0, winnerSeats: [], players: seatsClockwise.map((seat, index) => ({ seat, displayName: names[index], isCurrentPlayer: index === 0, hasLeft: false, entryCredits, payoutCredits: 0 })), actionDeadlineAtUtc: null, completedAtUtc: null } }
      }
    } else if (paid.kind === 'match' && match) {
      if (match.opponentsThinking && Date.now() - lastTurn > 1500) { const cards = legal(match.turn); play(cards[Math.floor(Math.random() * cards.length)].code); paid = { ...paid, version: paid.version + 1 } }
      paid = { ...paid, table: { ...paid.table!, match: snapshot(), actionDeadlineAtUtc: match.yourTurn || match.phase === 'passing' ? new Date(Date.now() + 45000).toISOString() : null } }
      if (match.winner) {
        const best = Math.min(...seatsClockwise.map(seat => match!.score[seat]))
        const winnerSeats = seatsClockwise.filter(seat => match!.score[seat] === best)
        const payout = paid.table!.potCredits / winnerSeats.length
        wallet += winnerSeats.includes('north') ? payout : 0
        paid = { ...paid, kind: 'result', balanceCredits: wallet, table: { ...paid.table!, winnerSeats, payoutCredits: winnerSeats.includes('north') ? payout : 0, completedAtUtc: new Date().toISOString(), players: paid.table!.players.map(player => ({ ...player, payoutCredits: winnerSeats.includes(player.seat) ? payout : 0 })) } }
        past.unshift(paid.table!)
      }
    }
    return structuredClone(paid)
  }
  const gateway: HeartsGateway = {
    onboarding: {
      getState: async () => { await delay(); return { shouldAskPlayedBefore: onboardingPending } },
      confirmExperience: async () => { await delay(); onboardingPending = false; return { shouldAskPlayedBefore: false } },
    },
    getStatus: async () => { await delay(); return { available: true, defaultTargetScore: 100, mode: 'free-play' } },
    startMatch: async () => { await delay(); trialActive = false; return start() },
    startTrial: async () => { await delay(); trialActive = true; return start() },
    cancelTrial: async () => { await delay(); trialActive = false; match = null; return { shouldAskPlayedBefore: onboardingPending } },
    pass: async (_, cards) => { await delay(); return pass(cards) },
    playCard: async (_, card) => { await delay(); return play(card) },
    advance: async () => { await delay(); if (match?.opponentsThinking) { const cards = legal(match.turn); return play(cards[Math.floor(Math.random() * cards.length)].code) } return snapshot() },
    nextRound: async () => { await delay(); if (trialActive) throw new Error('The trial is one hand only.'); return start(match!.roundNumber + 1, match!.score) },
    paid: {
      getSession: async () => { await delay(); return updatePaid() }, getMatch: async () => { await delay(); return updatePaid() },
      join: async entryCredits => { await delay(); if (paid.kind === 'queue' || paid.kind === 'match') return updatePaid(); wallet -= entryCredits; joined = Date.now(); paid = { kind: 'queue', version: paid.version + 1, balanceCredits: wallet, entryOptions: [1, 2, 5, 10, 25], table: null, queue: { matchId: crypto.randomUUID(), entryCredits, potCredits: entryCredits, joinedAtUtc: new Date().toISOString(), players: [{ seat: 'north', displayName: 'You', isCurrentPlayer: true }], message: '' } }; return updatePaid() },
      pass: async (_, cards) => { await delay(); pass(cards); paid = { ...paid, version: paid.version + 1 }; return updatePaid() },
      play: async (_, card) => { await delay(); play(card); paid = { ...paid, version: paid.version + 1 }; return updatePaid() },
      advance: async () => updatePaid(),
      nextRound: async () => { await delay(); start(match!.roundNumber + 1, match!.score); paid = { ...paid, version: paid.version + 1 }; return updatePaid() },
      leave: async () => { await delay(); if (paid.kind === 'queue') wallet += paid.queue!.entryCredits; paid = { ...paid, kind: 'idle', version: paid.version + 1, balanceCredits: wallet, table: null, queue: null }; return updatePaid() },
      getHistory: async () => structuredClone(past),
    },
  }
  return gateway
}
