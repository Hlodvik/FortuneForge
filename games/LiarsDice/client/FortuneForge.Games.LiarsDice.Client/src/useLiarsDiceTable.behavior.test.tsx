// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mocked } from 'vitest'
import { LiarsDiceGatewayError, type LiarsDiceBid, type LiarsDiceGateway, type LiarsDiceMatch, type LiarsDiceStatus } from './contracts'
import { useLiarsDiceTable } from './useLiarsDiceTable'

const player = 'liars player/one'
const id = '00000000-0000-4000-8000-000000000001'
const otherId = '00000000-0000-4000-8000-000000000002'
const status: LiarsDiceStatus = { available: true, startingDicePerPlayer: 5, mode: 'free-play' }
const key = (account = player, mode = status.mode) => `fortuneforge:liars-dice:match:${encodeURIComponent(account)}:${encodeURIComponent(mode)}`
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const opening = (matchId = id, dice = 5): LiarsDiceMatch => ({
  matchId, phase: 'bidding', roundNumber: 1, currentPlayerId: 'you', currentBid: null, currentBidderId: null,
  totalDice: dice * 4, hand: Array.from({ length: dice }, (_, index) => index % 6 + 1),
  players: ['you', 'opponent-1', 'opponent-2', 'opponent-3'].map((name, index) => ({ id: name, displayName: index === 0 ? 'You' : `Player ${index}`, diceCount: dice, active: true, isHuman: index === 0 })),
  outcome: null, winner: null, opponentsThinking: false, message: 'Your turn.',
})
const bidding = (matchId = id): LiarsDiceMatch => ({ ...opening(matchId), currentBid: { quantity: 5, face: 3 }, currentBidderId: 'opponent-3' })
const resolved = (matchId = id): LiarsDiceMatch => resolveCall(bidding(matchId), 'liar')
const opponentTurn = (): LiarsDiceMatch => ({ ...bidding(), currentPlayerId: 'opponent-1', currentBidderId: 'you', opponentsThinking: true })
const won = (): LiarsDiceMatch => ({
  ...bidding(), phase: 'resolved', roundNumber: 12, totalDice: 2, hand: [1], currentBid: { quantity: 2, face: 6 }, currentBidderId: 'opponent-1',
  players: opening().players.map(p => ({ ...p, diceCount: p.isHuman ? 1 : 0, active: p.isHuman })),
  outcome: { challengerId: 'you', bidderId: 'opponent-1', loserId: 'opponent-1', quantity: 2, face: 6, matchingDice: 0, callType: 'liar' }, winner: 'you', message: 'You win.',
})

beforeEach(() => setReducedMotion(true))
afterEach(() => { cleanup(); vi.restoreAllMocks(); sessionStorage.clear(); localStorage.clear(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('Liar’s Dice controller action guards', () => {
  it('waits for the player to start rather than automatically opening a match on mount', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match).toBeNull()
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    expect(server.gateway.getMatch).not.toHaveBeenCalled()
  })

  it('gates same-tick repeated starts and omits a client-selected seed', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(5); result.current.start(5) })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
    expect(server.gateway.startMatch.mock.calls[0]?.[0]).toEqual({ dicePerPlayer: 5 })
    expect(server.gateway.startMatch.mock.calls[0]?.[0]).not.toHaveProperty('seed')
    expect(result.current.match?.matchId).toBe(id)
  })

  it('rejects malformed starting counts without rounding them', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { for (const dice of [0, -1, 1.5, NaN, Infinity, Number.MAX_SAFE_INTEGER + 1]) result.current.start(dice) })
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    act(() => result.current.start(1))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.hand).toHaveLength(1)
  })

  it('preserves valid positive starting counts beyond the visible preset choices', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(7))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.startMatch.mock.calls[0]?.[0]).toEqual({ dicePerPlayer: 7 })
    expect(result.current.match?.hand).toHaveLength(7)
  })

  it('rejects illegal bid drafts without normalizing or submitting any of them', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    const drafts: LiarsDiceBid[] = [
      { quantity: 0, face: 3 }, { quantity: -1, face: 3 }, { quantity: 21, face: 3 }, { quantity: 5.5, face: 3 },
      { quantity: NaN, face: 3 }, { quantity: Infinity, face: 3 }, { quantity: 5, face: 0 }, { quantity: 5, face: 7 },
      { quantity: 5, face: 3.5 }, { quantity: 5, face: NaN }, { quantity: 5, face: 3 }, { quantity: 4, face: 6 },
    ]
    act(() => { for (const draft of drafts) result.current.bid(draft) })
    expect(server.gateway.bid).not.toHaveBeenCalled()
    act(() => result.current.bid({ quantity: 5, face: 4 }))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.bid.mock.calls[0]?.slice(0, 2)).toEqual([id, { quantity: 5, face: 4 }])
  })

  it('allows the top exact-face bid including an ordinary face one', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.bid({ quantity: 20, face: 1 }))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.bid.mock.calls[0]?.[1]).toEqual({ quantity: 20, face: 1 })
  })

  it('gates same-tick bids immediately before React renders a disabled control', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.bid({ quantity: 5, face: 4 }); result.current.bid({ quantity: 5, face: 4 }); result.current.call('liar') })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.bid).toHaveBeenCalledTimes(1)
    expect(server.gateway.challenge).not.toHaveBeenCalled()
  })

  it.each(['liar', 'spot-on'] as const)('gates same-tick repeated %s calls and prevents a competing bid', async kind => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.call(kind); result.current.call(kind); result.current.bid({ quantity: 6, face: 1 }) })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(kind === 'liar' ? server.gateway.challenge : server.gateway.spotOn).toHaveBeenCalledTimes(1)
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(result.current.match?.phase).toBe('resolved')
  })

  it('requires a prior bid before either kind of call and rejects next-round/advance on a human opening', async () => {
    const server = fakeServer()
    server.state = opening(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.call('liar'); result.current.call('spot-on'); result.current.nextRound(); result.current.advance() })
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    expect(server.gateway.nextRound).not.toHaveBeenCalled()
    expect(server.gateway.advance).not.toHaveBeenCalled()
  })

  it('rejects human bids and calls during a opponent turn and gates repeated opponent advancement', async () => {
    const server = fakeServer()
    server.state = opponentTurn(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar'); result.current.call('spot-on'); result.current.nextRound() })
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    expect(server.gateway.nextRound).not.toHaveBeenCalled()
    act(() => { result.current.advance(); result.current.advance() })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.advance).toHaveBeenCalledTimes(1)
    expect(result.current.match?.currentPlayerId).toBe('you')
  })

  it('rejects actions by an eliminated human even when a resolved DTO retains their dice', async () => {
    const server = fakeServer()
    server.state = { ...resolved(), hand: [3], totalDice: 16, players: opening().players.map(p => p.isHuman ? { ...p, diceCount: 0, active: false } : p) }
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar'); result.current.call('spot-on'); result.current.advance() })
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    expect(server.gateway.advance).not.toHaveBeenCalled()
  })

  it('gates repeated next rounds, omits the seed, and accepts immediate opponent resolution', async () => {
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    server.gateway.nextRound.mockImplementation(async () => {
      const next = nextHand(server.state)
      server.state = resolveCall({ ...next, currentBid: { quantity: 3, face: 4 }, currentBidderId: 'opponent-3' }, 'liar')
      return clone(server.state)
    })
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.nextRound(); result.current.nextRound() })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.nextRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.nextRound.mock.calls[0]?.[1]).toBeUndefined()
    expect(server.gateway.nextRound.mock.calls[0]?.[2]).toBeInstanceOf(AbortSignal)
    expect(result.current.match?.roundNumber).toBe(2)
    expect(result.current.match?.phase).toBe('resolved')
    expect(result.current.history.map(entry => entry.round)).toEqual([2, 1])
  })

  it('rejects next round, advance, bids and calls after a winner is declared', async () => {
    const server = fakeServer()
    server.state = won(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.nextRound(); result.current.advance(); result.current.bid({ quantity: 2, face: 6 }); result.current.call('liar'); result.current.call('spot-on') })
    expect(server.gateway.nextRound).not.toHaveBeenCalled()
    expect(server.gateway.advance).not.toHaveBeenCalled()
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
  })

  it('blocks every write before status arrives and when availability becomes false', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const pending = deferred<LiarsDiceStatus>()
    server.gateway.getStatus.mockImplementationOnce(() => pending.promise)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    act(() => { result.current.start(5); result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar'); result.current.call('spot-on'); result.current.nextRound(); result.current.advance() })
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    await act(async () => pending.resolve(status))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, available: false })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(5); result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar'); result.current.call('spot-on'); result.current.nextRound(); result.current.advance() })
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    expect(server.gateway.nextRound).not.toHaveBeenCalled()
    expect(server.gateway.advance).not.toHaveBeenCalled()
    expect(result.current.error).toContain('unavailable')
  })

  it('recovers a status failure using reads only', async () => {
    const server = fakeServer()
    server.gateway.getStatus.mockRejectedValueOnce(new LiarsDiceGatewayError('Disabled', 'liars-dice-disabled', 503))
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.status).toBeNull()
    expect(result.current.error).toBe('Disabled')
    act(() => result.current.start(5))
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.status?.available).toBe(true))
    expect(server.gateway.getStatus).toHaveBeenCalledTimes(2)
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
  })
})

describe('Liar’s Dice uncertain-write reconciliation', () => {
  it.each(['bid', 'challenge', 'spotOn', 'advance', 'nextRound'] as const)('reads the accepted %s response without replaying the mutation', async operation => {
    const server = fakeServer()
    server.state = operation === 'advance' ? opponentTurn() : operation === 'nextRound' ? resolved() : bidding()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse(operation)
    act(() => invoke(result.current, operation))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway[operation]).toHaveBeenCalledTimes(1)
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(2)
    expect(server.gateway.getMatch.mock.calls.map(call => call[0])).toEqual([id, id])
    expect(result.current.match).toEqual(server.state)
    expect(result.current.recovery).toBe('ready')
    expect(result.current.error).toContain('Match refreshed')
  })

  it('locks all mutations when a reconciliation read fails and retries only status/private GET', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('challenge'); server.readError = new TypeError('Still offline')
    act(() => result.current.call('liar'))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    expect(result.current.match?.phase).toBe('bidding')
    expect(server.state.phase).toBe('resolved')
    act(() => { result.current.start(5); result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar'); result.current.call('spot-on'); result.current.nextRound(); result.current.advance() })
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).toHaveBeenCalledTimes(1)
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    expect(server.gateway.nextRound).not.toHaveBeenCalled()
    expect(server.gateway.advance).not.toHaveBeenCalled()
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.recovery).toBe('ready')
    expect(result.current.match?.phase).toBe('resolved')
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(3)
    expect(server.gateway.challenge).toHaveBeenCalledTimes(1)
    expect(result.current.history).toHaveLength(1)
  })

  it('keeps a saved match locked until that exact private match is restored', async () => {
    const server = fakeServer()
    server.state = bidding(); server.readError = new TypeError('Offline'); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => { result.current.start(5); result.current.bid({ quantity: 6, face: 1 }); result.current.call('liar') })
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBe(id)
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.matchId).toBe(id)
    expect(server.gateway.getMatch.mock.calls.map(call => call[0])).toEqual([id, id])
  })

  it.each(['restoration', 'reconciliation'] as const)('expires a 404 match during %s and permits an explicit new start', async source => {
    const server = fakeServer()
    const expired = new LiarsDiceGatewayError('Expired', 'liars-dice-match-not-found', 404)
    if (source === 'restoration') { sessionStorage.setItem(key(), id); server.readError = expired }
    else { server.state = bidding(); sessionStorage.setItem(key(), id) }
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    if (source === 'reconciliation') {
      server.loseResponse('challenge'); server.readError = expired
      act(() => result.current.call('liar'))
      await waitFor(() => expect(result.current.busy).toBe(false))
    }
    expect(result.current.match).toBeNull()
    expect(result.current.history).toEqual([])
    expect(result.current.recovery).toBe('ready')
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(result.current.error).toContain('expired')
    server.readError = null
    act(() => result.current.start(3))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.hand).toHaveLength(3)
  })

  it('does not restore an old ID after losing the response to an explicit replacement match', async () => {
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toHaveLength(1)
    server.gateway.startMatch.mockRejectedValueOnce(new TypeError('Response lost'))
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match).toBeNull()
    expect(result.current.history).toEqual([])
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(1)
    expect(result.current.error).toContain('Match response lost')
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(1)
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
  })

  it('preserves a rejected action message after verifying the unchanged private match', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.bid.mockRejectedValueOnce(new LiarsDiceGatewayError('A new bid must be higher.', 'liars-dice-invalid-action', 400))
    act(() => result.current.bid({ quantity: 6, face: 1 }))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.currentBid).toEqual({ quantity: 5, face: 3 })
    expect(result.current.error).toBe('A new bid must be higher.')
    expect(server.gateway.bid).toHaveBeenCalledTimes(1)
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(2)
  })
})

describe('Liar’s Dice account/mode scope and abort guards', () => {
  it('restores a match after remount without starting or advancing another game', async () => {
    const server = fakeServer()
    const initial = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(initial.result.current.busy).toBe(false))
    act(() => initial.result.current.start(5))
    await waitFor(() => expect(initial.result.current.busy).toBe(false))
    initial.unmount()
    const restored = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(restored.result.current.busy).toBe(false))
    expect(restored.result.current.match?.matchId).toBe(id)
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
    expect(server.gateway.advance).not.toHaveBeenCalled()
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(1)
  })

  it('does not restore another account’s match and restores the original account when switching back', async () => {
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    const { result, rerender } = renderHook(({ account }) => useLiarsDiceTable(server.gateway, account), { initialProps: { account: player } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    rerender({ account: 'other-account' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match).toBeNull(); expect(result.current.history).toEqual([])
    expect(sessionStorage.getItem(key('other-account'))).toBeNull()
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(1)
    rerender({ account: player })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.matchId).toBe(id)
    expect(result.current.history).toHaveLength(1)
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(2)
  })

  it('restores only the newly reported mode’s saved match and clears old history', async () => {
    const server = fakeServer()
    const newMode = 'another-practice-mode'
    sessionStorage.setItem(key(), id); sessionStorage.setItem(key(player, newMode), otherId)
    server.gateway.getMatch.mockImplementation(async matchId => matchId === id ? resolved(id) : bidding(otherId))
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toHaveLength(1)
    server.gateway.getStatus.mockResolvedValue({ ...status, mode: newMode })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.matchId).toBe(otherId)
    expect(result.current.history).toEqual([])
    expect(server.gateway.getMatch.mock.calls.map(call => call[0])).toEqual([id, otherId])
    expect(sessionStorage.getItem(key())).toBe(id)
  })

  it('drops the old mode’s state when the new mode has no saved match', async () => {
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, mode: 'other-mode' })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match).toBeNull()
    expect(result.current.history).toEqual([])
    expect(server.gateway.getMatch).toHaveBeenCalledTimes(1)
  })

  it.each(['account', 'gateway', 'unmount'] as const)('aborts and ignores a late start response after %s even when transport ignores abort', async change => {
    const server = fakeServer(), replacement = fakeServer()
    const pending = deferred<LiarsDiceMatch>()
    server.gateway.startMatch.mockImplementationOnce(() => pending.promise)
    const { result, rerender, unmount } = renderHook(({ account, gateway }) => useLiarsDiceTable(gateway, account), { initialProps: { account: player, gateway: server.gateway } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(5))
    const signal = server.gateway.startMatch.mock.calls[0]?.[1]
    if (change === 'unmount') unmount()
    else {
      rerender({ account: change === 'account' ? 'other-player' : player, gateway: change === 'gateway' ? replacement.gateway : server.gateway })
      await waitFor(() => expect(result.current.busy).toBe(false))
    }
    expect(signal?.aborted).toBe(true)
    await act(async () => pending.resolve(opening()))
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
    if (change !== 'unmount') expect(result.current.match).toBeNull()
  })

  it('aborts a late bid and never publishes it in the replacement account', async () => {
    const server = fakeServer()
    server.state = bidding(); sessionStorage.setItem(key(), id)
    const pending = deferred<LiarsDiceMatch>()
    server.gateway.bid.mockImplementationOnce(() => pending.promise)
    const { result, rerender } = renderHook(({ account }) => useLiarsDiceTable(server.gateway, account), { initialProps: { account: player } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.bid({ quantity: 6, face: 1 }))
    const signal = server.gateway.bid.mock.calls[0]?.[2]
    rerender({ account: 'other-player' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(resolved()))
    expect(signal?.aborted).toBe(true)
    expect(result.current.match).toBeNull()
    expect(result.current.history).toEqual([])
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
  })

  it('ignores an old private restoration response after switching account', async () => {
    const server = fakeServer()
    const pending = deferred<LiarsDiceMatch>()
    sessionStorage.setItem(key(), id)
    server.gateway.getMatch.mockImplementationOnce(() => pending.promise)
    const { result, rerender } = renderHook(({ account }) => useLiarsDiceTable(server.gateway, account), { initialProps: { account: player } })
    await waitFor(() => expect(server.gateway.getMatch).toHaveBeenCalledTimes(1))
    const signal = server.gateway.getMatch.mock.calls[0]?.[1]
    rerender({ account: 'other-player' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(resolved()))
    expect(signal?.aborted).toBe(true)
    expect(result.current.match).toBeNull()
    expect(result.current.history).toEqual([])
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
  })

  it('ignores a late status response from a replaced gateway', async () => {
    const server = fakeServer(), replacement = fakeServer({ ...status, startingDicePerPlayer: 3 })
    const pending = deferred<LiarsDiceStatus>()
    server.gateway.getStatus.mockImplementationOnce(() => pending.promise)
    const { result, rerender } = renderHook(({ gateway }) => useLiarsDiceTable(gateway, player), { initialProps: { gateway: server.gateway } })
    rerender({ gateway: replacement.gateway })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(status))
    expect(result.current.status?.startingDicePerPlayer).toBe(3)
    expect(server.gateway.getStatus.mock.calls[0]?.[0]?.aborted).toBe(true)
  })

  it('rejects handlers retained from a replaced gateway even after the replacement is ready', async () => {
    const server = fakeServer(), replacement = fakeServer()
    const { result, rerender } = renderHook(({ gateway }) => useLiarsDiceTable(gateway, player), { initialProps: { gateway: server.gateway } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    const oldStart = result.current.start
    rerender({ gateway: replacement.gateway })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => oldStart(5))
    expect(server.gateway.startMatch).not.toHaveBeenCalled()
    expect(replacement.gateway.startMatch).not.toHaveBeenCalled()
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(replacement.gateway.startMatch).toHaveBeenCalledTimes(1)
  })

  it('does not duplicate resolved history when the same round is refreshed repeatedly', async () => {
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toHaveLength(1)
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.history).toEqual([])
  })
})

describe('Liar’s Dice motion and optional gateway support', () => {
  it('locks a normal-motion new hand for 890 ms and ignores competing writes', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    vi.useFakeTimers()
    await act(async () => result.current.start(5))
    expect(result.current.busy).toBe(true)
    expect(result.current.rolling).toBe(true)
    expect(result.current.pendingMatch?.hand).toHaveLength(5)
    expect(result.current.match).toBeNull()
    act(() => { result.current.start(3); result.current.bid({ quantity: 1, face: 1 }); result.current.retry() })
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.getStatus).toHaveBeenCalledTimes(1)
    await act(async () => vi.advanceTimersByTimeAsync(889))
    expect(result.current.busy).toBe(true)
    await act(async () => vi.advanceTimersByTimeAsync(1))
    expect(result.current.busy).toBe(false)
    expect(result.current.rolling).toBe(false)
    expect(result.current.pendingMatch).toBeNull()
    expect(result.current.match?.matchId).toBe(id)
  })

  it('commits immediately without an 890 ms reveal timer when reduced motion is requested', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    vi.useFakeTimers()
    const delays = vi.spyOn(window, 'setTimeout')
    await act(async () => result.current.start(5))
    expect(result.current.match?.matchId).toBe(id)
    expect(result.current.busy).toBe(false)
    expect(result.current.pendingMatch).toBeNull()
    expect(delays.mock.calls.some(([, milliseconds]) => milliseconds === 890)).toBe(false)
  })

  it('shows the next authoritative hand while keeping the previous round committed until normal-motion reveal finishes', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    server.state = resolved(); sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    vi.useFakeTimers()
    await act(async () => result.current.nextRound())
    expect(result.current.match?.roundNumber).toBe(1)
    expect(result.current.match?.phase).toBe('resolved')
    expect(result.current.pendingMatch?.roundNumber).toBe(2)
    expect(result.current.pendingMatch?.hand).toEqual([2, 2, 2, 2])
    expect(result.current.busy).toBe(true)
    act(() => { result.current.nextRound(); result.current.bid({ quantity: 1, face: 1 }); result.current.call('liar') })
    await act(async () => vi.advanceTimersByTimeAsync(890))
    expect(result.current.match?.roundNumber).toBe(2)
    expect(result.current.match?.phase).toBe('bidding')
    expect(result.current.pendingMatch).toBeNull()
    expect(result.current.busy).toBe(false)
    expect(server.gateway.nextRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.bid).not.toHaveBeenCalled()
    expect(server.gateway.challenge).not.toHaveBeenCalled()
  })

  it('saves an acknowledged match during pending animation and restores it after unmount without another POST', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    const { result, unmount } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    vi.useFakeTimers()
    await act(async () => result.current.start(5))
    const signal = server.gateway.startMatch.mock.calls[0]?.[1]
    expect(result.current.pendingMatch?.matchId).toBe(id)
    expect(result.current.match).toBeNull()
    expect(result.current.busy).toBe(true)
    expect(sessionStorage.getItem(key())).toBe(id)
    unmount()
    const restored = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await act(async () => {})
    expect(restored.result.current.match?.matchId).toBe(id)
    expect(restored.result.current.busy).toBe(false)
    expect(server.gateway.getMatch).toHaveBeenCalledExactlyOnceWith(id, expect.any(AbortSignal))
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
    await act(async () => vi.advanceTimersByTimeAsync(890))
    expect(signal?.aborted).toBe(true)
    expect(restored.result.current.match?.matchId).toBe(id)
    expect(sessionStorage.getItem(key())).toBe(id)
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
  })

  it('plays through a successful legacy gateway without persistence or optional Spot On', async () => {
    const server = fakeServer()
    const { getMatch: _read, spotOn: _spot, ...legacy } = server.gateway
    const { result } = renderHook(() => useLiarsDiceTable(legacy, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.bid({ quantity: 1, face: 1 }))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.call('spot-on'))
    expect(server.gateway.spotOn).not.toHaveBeenCalled()
    act(() => result.current.call('liar'))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.phase).toBe('resolved')
    expect(result.current.recovery).toBe('ready')
    expect(server.gateway.getMatch).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBeNull()
  })

  it('cannot unlock an uncertain legacy match with a status-only retry', async () => {
    const server = fakeServer()
    const { getMatch: _read, ...legacy } = server.gateway
    const { result } = renderHook(() => useLiarsDiceTable(legacy, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.bid({ quantity: 1, face: 1 }))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('challenge')
    act(() => result.current.call('liar'))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.recovery).toBe('failed')
    act(() => { result.current.start(5); result.current.call('liar') })
    expect(server.gateway.startMatch).toHaveBeenCalledTimes(1)
    expect(server.gateway.challenge).toHaveBeenCalledTimes(1)
    expect(server.gateway.getMatch).not.toHaveBeenCalled()
  })

  it('continues practice play if optional storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Unavailable') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Unavailable') })
    const server = fakeServer()
    const { result } = renderHook(() => useLiarsDiceTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.match?.matchId).toBe(id)
    expect(result.current.error).toBeNull()
  })
})

function setReducedMotion(reduced: boolean) {
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({ matches: reduced && query === '(prefers-reduced-motion: reduce)', media: query, onchange: null, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() })))
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }
type Table = ReturnType<typeof useLiarsDiceTable>
type Operation = 'bid' | 'challenge' | 'spotOn' | 'advance' | 'nextRound'
function invoke(table: Table, operation: Operation) {
  if (operation === 'bid') table.bid({ quantity: 6, face: 1 })
  else if (operation === 'challenge') table.call('liar')
  else if (operation === 'spotOn') table.call('spot-on')
  else if (operation === 'advance') table.advance()
  else table.nextRound()
}
function resolveCall(match: LiarsDiceMatch, kind: 'liar' | 'spot-on'): LiarsDiceMatch {
  const bid = match.currentBid!
  const loserId = kind === 'spot-on' ? match.currentBidderId! : match.currentPlayerId
  return {
    ...match, phase: 'resolved', players: match.players.map(p => p.id === loserId ? { ...p, diceCount: p.diceCount - 1, active: p.diceCount > 1 } : p),
    outcome: { challengerId: match.currentPlayerId, bidderId: match.currentBidderId!, loserId, quantity: bid.quantity, face: bid.face, matchingDice: bid.quantity, callType: kind },
    opponentsThinking: false, message: `${loserId} loses a die.`,
  }
}
function nextHand(match: LiarsDiceMatch): LiarsDiceMatch {
  const human = match.players.find(p => p.isHuman)!
  const currentPlayerId = human.active ? human.id : match.players.find(p => p.active)!.id
  return { ...match, phase: 'bidding', roundNumber: match.roundNumber + 1, currentPlayerId,
    totalDice: match.players.reduce((sum, p) => sum + p.diceCount, 0), hand: Array.from({ length: human.diceCount }, () => 2),
    currentBid: null, currentBidderId: null, outcome: null, winner: null, opponentsThinking: currentPlayerId !== human.id, message: 'Next round.',
  }
}
function fakeServer(tableStatus: LiarsDiceStatus = status) {
  type TestGateway = Required<LiarsDiceGateway>
  let lost: Operation | null = null
  const server: { state: LiarsDiceMatch; readError: Error | null; loseResponse: (operation: Operation) => void; gateway: Mocked<TestGateway> } = {
    state: opening(), readError: null, loseResponse(operation) { lost = operation }, gateway: {} as Mocked<TestGateway>,
  }
  function answer(operation: Operation) {
    if (lost === operation) { lost = null; throw new TypeError('Response lost after server commit') }
    return clone(server.state)
  }
  server.gateway = {
    getStatus: vi.fn(async (_signal?: AbortSignal) => clone(tableStatus)),
    getMatch: vi.fn(async (_matchId: string, _signal?: AbortSignal) => { if (server.readError) throw server.readError; return clone(server.state) }),
    startMatch: vi.fn(async (options: { dicePerPlayer: number; seed?: number }, _signal?: AbortSignal) => { server.state = opening(id, options.dicePerPlayer); return clone(server.state) }),
    bid: vi.fn(async (_matchId: string, draft: LiarsDiceBid, _signal?: AbortSignal) => {
      server.state = { ...server.state, currentBid: { quantity: Math.max(6, draft.quantity), face: draft.quantity >= server.state.totalDice ? 6 : 3 }, currentBidderId: 'opponent-3', currentPlayerId: 'you' }
      return answer('bid')
    }),
    challenge: vi.fn(async (_matchId: string, _signal?: AbortSignal) => { server.state = resolveCall(server.state, 'liar'); return answer('challenge') }),
    spotOn: vi.fn(async (_matchId: string, _signal?: AbortSignal) => { server.state = resolveCall(server.state, 'spot-on'); return answer('spotOn') }),
    advance: vi.fn(async (_matchId: string, _signal?: AbortSignal) => { server.state = { ...server.state, currentPlayerId: 'you', currentBidderId: 'opponent-3', opponentsThinking: false }; return answer('advance') }),
    nextRound: vi.fn(async (_matchId: string, _seed?: number, _signal?: AbortSignal) => { server.state = nextHand(server.state); return answer('nextRound') }),
  }
  return server
}
