import { describe, expect, it, vi } from 'vitest'
import { LiarsDiceGatewayError, type LiarsDiceMatch, type LiarsDicePlayer, type LiarsDiceStatus } from './contracts'
import { HttpLiarsDiceGateway } from './httpLiarsDiceGateway'

const matchId = '00000000-0000-4000-8000-000000000001'
const status: LiarsDiceStatus = { available: true, startingDicePerPlayer: 5, mode: 'free-play-bots' }
const players: readonly LiarsDicePlayer[] = [
  { id: 'you', displayName: 'You', diceCount: 5, active: true, isHuman: true },
  { id: 'bot-1', displayName: 'Amber Badger', diceCount: 5, active: true, isHuman: false },
  { id: 'bot-2', displayName: 'Copper Finch', diceCount: 5, active: true, isHuman: false },
  { id: 'bot-3', displayName: 'Silver Otter', diceCount: 5, active: true, isHuman: false },
]
const opening: LiarsDiceMatch = {
  matchId, phase: 'bidding', roundNumber: 1, currentPlayerId: 'you', currentBid: null, currentBidderId: null,
  totalDice: 20, hand: [1, 1, 2, 4, 6], players, outcome: null, winner: null, opponentsThinking: false, message: 'Place a bid.',
}
const bidding: LiarsDiceMatch = { ...opening, currentBid: { quantity: 5, face: 3 }, currentBidderId: 'bot-3', message: 'Your turn.' }
const loseDie = (id: string, counts = players) => counts.map(player => player.id === id ? { ...player, diceCount: player.diceCount - 1, active: player.diceCount > 1 } : player)
const humanLost: LiarsDiceMatch = {
  ...bidding, phase: 'resolved', players: loseDie('you'),
  outcome: { challengerId: 'you', bidderId: 'bot-3', loserId: 'you', quantity: 5, face: 3, matchingDice: 6, callType: 'liar' },
  message: 'you loses a die. Start the next round.',
}
const bidderLost: LiarsDiceMatch = {
  ...bidding, phase: 'resolved', players: loseDie('bot-3'),
  outcome: { challengerId: 'you', bidderId: 'bot-3', loserId: 'bot-3', quantity: 5, face: 3, matchingDice: 3, callType: 'liar' },
  message: 'bot-3 loses a die. Start the next round.',
}
const nextResolved: LiarsDiceMatch = { ...bidderLost, roundNumber: 2, totalDice: 19, hand: [1, 2, 4, 6], players: loseDie('bot-3', humanLost.players) }
const eliminatedHuman: LiarsDiceMatch = {
  ...humanLost, roundNumber: 9, totalDice: 16, hand: [3], players: players.map(player => player.isHuman ? { ...player, diceCount: 0, active: false } : player),
}
const twoPlayers = players.map(player => ({ ...player, diceCount: player.id === 'you' || player.id === 'bot-1' ? 1 : 0, active: player.id === 'you' || player.id === 'bot-1' }))
const humanWinner: LiarsDiceMatch = {
  ...opening, phase: 'resolved', roundNumber: 15, totalDice: 2, hand: [1], currentBid: { quantity: 2, face: 6 }, currentBidderId: 'bot-1',
  players: loseDie('bot-1', twoPlayers),
  outcome: { challengerId: 'you', bidderId: 'bot-1', loserId: 'bot-1', quantity: 2, face: 6, matchingDice: 0, callType: 'liar' },
  winner: 'you', message: 'you wins the match!',
}
const botWinner: LiarsDiceMatch = {
  ...humanWinner, hand: [6], players: loseDie('you', twoPlayers),
  outcome: { ...humanWinner.outcome!, loserId: 'you', matchingDice: 2 }, winner: 'bot-1', message: 'bot-1 wins the match!',
}
const response = (value: unknown, code = 200) => new Response(JSON.stringify(value), { status: code, headers: { 'content-type': 'application/json' } })
const gatewayFor = (value: unknown) => new HttpLiarsDiceGateway(undefined, vi.fn().mockResolvedValue(response(value)))
const mutatePlayer = (value: LiarsDiceMatch, patch: Record<string, unknown>, index = 0) => ({ ...value, players: value.players.map((player, position) => position === index ? { ...player, ...patch } : player) })

describe('HttpLiarsDiceGateway public requests and recovery', () => {
  it('does not bind the gateway as the native fetch receiver', async () => {
    const fetcher = vi.fn(function (this: unknown) {
      if (this !== undefined) throw new TypeError('Illegal invocation')
      return Promise.resolve(response(status))
    })
    vi.stubGlobal('fetch', fetcher)
    try {
      await expect(new HttpLiarsDiceGateway().getStatus()).resolves.toEqual(status)
      expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/liars-dice/status', { signal: undefined })
    } finally { vi.unstubAllGlobals() }
  })

  it('reads status through an injected transport and forwards cancellation', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(status))
    const signal = new AbortController().signal
    await expect(new HttpLiarsDiceGateway('/table/', fetcher).getStatus(signal)).resolves.toEqual(status)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/table/status', { signal })
  })

  it('reads the private match once without an automatic mutation', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(bidding))
    const signal = new AbortController().signal
    await expect(new HttpLiarsDiceGateway('/table/', fetcher).getMatch(matchId, signal)).resolves.toEqual(bidding)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/table/matches/${matchId}`, { signal })
  })

  it('encodes the identifier rather than interpreting it as another path or query', async () => {
    const id = 'private/match?x=1'
    const fetcher = vi.fn().mockResolvedValue(response({ ...opening, matchId: id }))
    await new HttpLiarsDiceGateway(undefined, fetcher).getMatch(id)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/liars-dice/matches/private%2Fmatch%3Fx%3D1', { signal: undefined })
  })

  it.each([{ dicePerPlayer: 5 }, { dicePerPlayer: 3, seed: 42 }])('posts the exact start options without inventing or deriving a seed', async options => {
    const fetcher = vi.fn().mockResolvedValue(response(opening))
    const signal = new AbortController().signal
    await new HttpLiarsDiceGateway(undefined, fetcher).startMatch(options, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/liars-dice/matches', {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(options), signal,
    })
  })

  it('posts the exact quantity and face without a hidden-hand or idempotency payload', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(bidding))
    const signal = new AbortController().signal
    const bid = { quantity: 20, face: 6 }
    await new HttpLiarsDiceGateway(undefined, fetcher).bid(matchId, bid, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/liars-dice/matches/${matchId}/bid`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(bid), signal,
    })
  })

  it.each([
    ['challenge', 'challenge'], ['spotOn', 'spot-on'], ['advance', 'advance'],
  ] as const)('posts %s once with no invented request body', async (method, path) => {
    const fetcher = vi.fn().mockResolvedValue(response(bidderLost))
    const signal = new AbortController().signal
    await new HttpLiarsDiceGateway(undefined, fetcher)[method](matchId, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/liars-dice/matches/${matchId}/${path}`, { method: 'POST', signal })
  })

  it.each([undefined, 0, 42])('posts next-round with only the caller-provided optional seed', async seed => {
    const fetcher = vi.fn().mockResolvedValue(response(nextResolved))
    const signal = new AbortController().signal
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).nextRound(matchId, seed, signal)).resolves.toMatchObject({ phase: 'resolved', roundNumber: 2 })
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/liars-dice/matches/${matchId}/next-round`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ seed }), signal,
    })
  })

  it.each(['getMatch', 'bid', 'challenge', 'spotOn', 'advance', 'nextRound'] as const)('rejects another match returned by %s', async method => {
    const gateway = gatewayFor({ ...bidding, matchId: 'other-match' })
    const result = method === 'bid' ? gateway.bid(matchId, { quantity: 6, face: 2 }) : gateway[method](matchId)
    await expect(result).rejects.toBeInstanceOf(LiarsDiceGatewayError)
  })

  it('preserves an expired private match error for controller recovery', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ code: 'liars-dice-match-not-found', message: 'That table expired.' }, 404))
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).getMatch(matchId)).rejects.toMatchObject({
      name: 'LiarsDiceGatewayError', code: 'liars-dice-match-not-found', message: 'That table expired.', status: 404,
    })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('preserves a rejected action without replaying it', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ code: 'liars-dice-invalid-action', message: 'A new bid must be higher.' }, 400))
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).bid(matchId, { quantity: 5, face: 3 })).rejects.toMatchObject({ code: 'liars-dice-invalid-action', status: 400 })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('keeps a non-JSON service error HTTP status', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('Unavailable', { status: 503 }))
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).getStatus()).rejects.toMatchObject({ code: 'liars-dice-request-failed', status: 503 })
  })

  it('rejects malformed successful JSON without retrying the write', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{', { status: 200 }))
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).challenge(matchId)).rejects.toBeInstanceOf(LiarsDiceGatewayError)
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('passes through transport failure and does not replay a possibly accepted action', async () => {
    const failure = new TypeError('Connection interrupted')
    const fetcher = vi.fn().mockRejectedValue(failure)
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).spotOn(matchId)).rejects.toBe(failure)
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('passes through the original cancellation error and forwards the signal', async () => {
    const error = new DOMException('Cancelled', 'AbortError')
    const fetcher = vi.fn().mockRejectedValue(error)
    const controller = new AbortController()
    controller.abort()
    await expect(new HttpLiarsDiceGateway(undefined, fetcher).getMatch(matchId, controller.signal)).rejects.toBe(error)
    expect(fetcher.mock.calls[0][1].signal).toBe(controller.signal)
  })
})

describe('Liar’s Dice response consistency', () => {
  it.each([opening, bidding, humanLost, bidderLost, eliminatedHuman, humanWinner, botWinner])('accepts actual bidding and retained resolved hand/count relationships', async value => {
    await expect(gatewayFor(value).getMatch(matchId)).resolves.toEqual(value)
  })

  it('accepts a bid equal to total dice, including ordinary face one', async () => {
    const value = { ...bidding, currentBid: { quantity: 20, face: 1 } }
    await expect(gatewayFor(value).getMatch(matchId)).resolves.toEqual(value)
  })

  it('accepts a later bot-only bidding round with an eliminated human and an empty private hand', async () => {
    const value: LiarsDiceMatch = {
      ...eliminatedHuman, phase: 'bidding', roundNumber: 10, totalDice: 15, hand: [], currentPlayerId: 'bot-1',
      currentBidderId: 'bot-3', outcome: null, opponentsThinking: true, message: 'Opponents are acting.',
    }
    await expect(gatewayFor(value).getMatch(matchId)).resolves.toEqual(value)
  })

  it('accepts legacy outcomes without a call type as ordinary liar challenges', async () => {
    const value = { ...humanLost, outcome: { ...humanLost.outcome!, callType: undefined } }
    await expect(gatewayFor(value).getMatch(matchId)).resolves.toEqual(value)
  })

  it.each([
    { ...bidderLost, outcome: { ...bidderLost.outcome!, callType: 'spot-on', matchingDice: 5 } },
    { ...humanLost, outcome: { ...humanLost.outcome!, callType: 'spot-on', matchingDice: 4 } },
  ])('accepts spot-on exact success penalizing the bidder and failure penalizing the caller', async value => {
    await expect(gatewayFor(value).getMatch(matchId)).resolves.toEqual(value)
  })

  it.each([{ ...status, available: false }, { ...status, startingDicePerPlayer: 1 }])('accepts authoritative availability and positive starting dice counts', async value => {
    await expect(gatewayFor(value).getStatus()).resolves.toEqual(value)
  })

  it.each([
    { ...status, startingDicePerPlayer: 0 }, { ...status, startingDicePerPlayer: -1 }, { ...status, startingDicePerPlayer: 1.5 },
    { ...status, startingDicePerPlayer: Infinity }, { ...status, mode: '' }, { ...status, mode: '  ' }, { ...status, available: 1 },
  ])('rejects malformed status before enabling game controls', async value => {
    await expect(gatewayFor(value).getStatus()).rejects.toBeInstanceOf(LiarsDiceGatewayError)
  })

  it.each([
    { ...opening, matchId: '' }, { ...opening, phase: 'unknown' }, { ...opening, roundNumber: 0 }, { ...opening, roundNumber: 1.5 },
    { ...opening, currentPlayerId: 'unknown' }, { ...opening, totalDice: 0 }, { ...opening, totalDice: 19 },
    { ...opening, hand: [0, 1, 2, 3, 4] }, { ...opening, hand: [1, 2, 3, 4, 7] }, { ...opening, hand: [1, 2] },
    { ...opening, players: [] }, { ...opening, players: [players[0]] }, { ...opening, players: [players[0], players[0], players[2], players[3]] },
    mutatePlayer(opening, { id: '' }), mutatePlayer(opening, { displayName: '' }), mutatePlayer(opening, { diceCount: -1 }),
    mutatePlayer(opening, { diceCount: 1.5 }), mutatePlayer(opening, { active: false }), mutatePlayer(opening, { isHuman: 'true' }),
    mutatePlayer(opening, { isHuman: false }), mutatePlayer(opening, { isHuman: true }, 1),
    { ...opening, currentBidderId: 'bot-3' }, { ...bidding, currentBidderId: null },
    { ...bidding, currentBidderId: 'unknown' }, { ...bidding, currentBidderId: 'you' },
    { ...bidding, currentBid: { quantity: 0, face: 3 } }, { ...bidding, currentBid: { quantity: 21, face: 3 } },
    { ...bidding, currentBid: { quantity: 5, face: 0 } }, { ...bidding, currentBid: { quantity: 5, face: 7 } },
    { ...opening, outcome: humanLost.outcome }, { ...opening, winner: 'you' },
    { ...bidding, currentPlayerId: 'you', hand: [], totalDice: 15, players: players.map(player => player.isHuman ? { ...player, diceCount: 0, active: false } : player) },
    { ...humanLost, outcome: null }, { ...humanLost, currentBid: null, currentBidderId: null },
    { ...humanLost, totalDice: 19 }, { ...humanLost, hand: [1, 2, 3, 4] },
    { ...bidderLost, hand: [...bidderLost.hand, 2] },
    { ...humanLost, outcome: { ...humanLost.outcome!, challengerId: 'unknown' } },
    { ...humanLost, outcome: { ...humanLost.outcome!, bidderId: 'bot-1' } },
    { ...humanLost, outcome: { ...humanLost.outcome!, loserId: 'bot-2' } },
    { ...humanLost, outcome: { ...humanLost.outcome!, quantity: 4 } },
    { ...humanLost, outcome: { ...humanLost.outcome!, face: 4 } },
    { ...humanLost, outcome: { ...humanLost.outcome!, matchingDice: -1 } },
    { ...humanLost, outcome: { ...humanLost.outcome!, matchingDice: 21 } },
    { ...humanLost, outcome: { ...humanLost.outcome!, callType: 'unknown' } },
    { ...humanLost, outcome: { ...humanLost.outcome!, loserId: 'bot-3' } },
    { ...humanLost, outcome: { ...humanLost.outcome!, callType: 'spot-on', matchingDice: 5 } },
    { ...bidderLost, outcome: { ...bidderLost.outcome!, callType: 'spot-on', matchingDice: 3 } },
    { ...humanLost, winner: 'you' }, { ...humanWinner, winner: null }, { ...humanWinner, winner: 'bot-1' },
    { ...humanWinner, phase: 'bidding', outcome: null, totalDice: 1 },
    { ...opening, message: 123 },
  ])('rejects malformed or contradictory private match data', async value => {
    await expect(gatewayFor(value).getMatch(matchId)).rejects.toBeInstanceOf(LiarsDiceGatewayError)
  })
})
