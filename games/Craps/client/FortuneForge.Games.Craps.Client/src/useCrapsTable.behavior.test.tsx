// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mocked } from 'vitest'
import { CrapsGatewayError, type CrapsExtraBetRequest, type CrapsGateway, type CrapsOutcome, type CrapsRound, type CrapsStatus } from './contracts'
import { useCrapsTable } from './useCrapsTable'

const player = 'craps player/one'
const id = '00000000-0000-4000-8000-000000000001'
const otherId = '00000000-0000-4000-8000-000000000002'
const status: CrapsStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'free-play-pass-line' }
const key = (account = player, mode = status.mode) => `fortuneforge:craps:round:${encodeURIComponent(account)}:${encodeURIComponent(mode)}`
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const ready = (roundId = id): CrapsRound => ({ roundId, stake: 10, phase: 'come-out', point: null, rolls: [], lastOutcome: null, extraBets: [] })
const pointHand = (roundId = id): CrapsRound => advance(ready(roundId), { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null })
const wonHand = (roundId = id): CrapsRound => advance(pointHand(roundId), { first: 2, second: 4, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 })

beforeEach(() => setReducedMotion(true))
afterEach(() => { cleanup(); sessionStorage.clear(); localStorage.clear(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('Craps controller wager and turn guards', () => {
  it('gates repeated starts, rolls and odds immediately before React disables controls', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(10); result.current.start(10) })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    act(() => { result.current.roll(); result.current.roll() })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(result.current.round?.point).toBe(6)
    act(() => { result.current.placeOdds(1); result.current.placeOdds(1) })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
    expect(result.current.round?.extraBets).toEqual([{ kind: 'odds', stake: 1, resolved: false, won: false, totalReturn: null }])
  })

  it('rejects invalid entered main and extra stakes without silently changing them', async () => {
    const server = fakeServer()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => {
      for (const stake of [0, -1, .5, 10.5, 101, NaN, Infinity]) result.current.start(stake)
      for (const stake of [0, 1.5, 101, NaN]) result.current.start(10, [{ kind: 'field', stake }])
    })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    act(() => result.current.start(100, [{ kind: 'field', stake: 100 }, { kind: 'any-seven', stake: 100 }]))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(100)
    expect(server.gateway.startRound.mock.calls[0]?.[2]).toEqual([{ kind: 'field', stake: 100 }, { kind: 'any-seven', stake: 100 }])
  })

  it('rejects odds drafts outside exact current limits but permits R1 odds at point 6', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { for (const stake of [0, .5, 10.5, 101, NaN, Infinity]) result.current.placeOdds(stake) })
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    act(() => result.current.placeOdds(1))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeOdds.mock.calls[0]?.slice(0, 2)).toEqual([id, 1])
  })

  it('uses status increments rather than accepting any whole-rand draft', async () => {
    const server = fakeServer({ ...status, minimumStake: 3, maximumStake: 18, stakeIncrement: 4 })
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(10); result.current.start(18) })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    act(() => result.current.start(15))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.startRound.mock.calls[0]?.[0]).toBe(15)
  })

  it('rejects all writes while watching another shooter, including odds after a point', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result, rerender } = renderHook(({ mine }) => useCrapsTable(server.gateway, player, mine), { initialProps: { mine: false } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(10); result.current.roll(); result.current.placeOdds(1); result.current.clear() })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    expect(result.current.round?.roundId).toBe(id)
    rerender({ mine: true })
    act(() => result.current.placeOdds(1))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
  })

  it('does not clear a finished hand or start its replacement during another shooter’s turn', async () => {
    const server = fakeServer()
    server.state = wonHand()
    sessionStorage.setItem(key(), id)
    const callback = vi.fn()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, false, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    act(() => { result.current.clear(); result.current.start(10); result.current.roll(); result.current.placeOdds(1) })
    expect(result.current.round?.phase).toBe('resolved')
    expect(callback).not.toHaveBeenCalled()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBe(id)
  })

  it('blocks writes until status loads and when the table later becomes unavailable', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const initial = deferred<CrapsStatus>()
    server.gateway.getStatus.mockImplementationOnce(() => initial.promise)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    act(() => { result.current.start(10); result.current.roll(); result.current.placeOdds(1) })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    await act(async () => initial.resolve(status))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, available: false })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => { result.current.start(10); result.current.roll(); result.current.placeOdds(1) })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
  })
})

describe('Craps uncertain-response recovery', () => {
  it('reads an accepted-but-lost roll without rolling again', async () => {
    const server = fakeServer()
    const callback = vi.fn()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(10))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    server.loseResponse('roll')
    act(() => { result.current.roll(); result.current.roll() })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
    expect(result.current.round?.rolls).toHaveLength(1)
    expect(result.current.round?.phase).toBe('point')
    expect(callback).toHaveBeenCalledTimes(1)
    expect(callback.mock.calls[0]?.[0].rolls).toHaveLength(1)
    expect(result.current.error).toContain('Hand refreshed')
  })

  it('reads committed odds after a lost response and prevents placing the same odds again', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('odds')
    act(() => result.current.placeOdds(1))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(2)
    expect(result.current.round?.extraBets?.filter(bet => bet.kind === 'odds')).toHaveLength(1)
    act(() => result.current.placeOdds(1))
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
  })

  it('locks mutations after a failed reconciliation and retries only status and GET', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('roll')
    server.readError = new TypeError('Still offline')
    act(() => result.current.roll())
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    expect(result.current.round?.rolls).toHaveLength(1)
    expect(server.state.rolls).toHaveLength(2)
    act(() => { result.current.roll(); result.current.placeOdds(1); result.current.start(10); result.current.clear() })
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.recovery).toBe('ready')
    expect(result.current.round?.rolls).toHaveLength(2)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(3)
    act(() => result.current.placeOdds(1))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(server.gateway.placeOdds).toHaveBeenCalledTimes(1)
  })

  it('keeps an offline saved hand locked until that exact hand is restored', async () => {
    const server = fakeServer()
    server.state = pointHand()
    server.readError = new TypeError('Offline')
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => { result.current.start(10); result.current.roll(); result.current.placeOdds(1) })
    expect(server.gateway.startRound).not.toHaveBeenCalled()
    expect(server.gateway.roll).not.toHaveBeenCalled()
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBe(id)
    server.readError = null
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round?.roundId).toBe(id)
    expect(result.current.round?.point).toBe(6)
    expect(server.gateway.getRound.mock.calls.map(call => call[0])).toEqual([id, id])
  })

  it.each(['restoration', 'reconciliation'] as const)('expires a missing hand during %s and permits a fresh bet', async source => {
    const server = fakeServer()
    const callback = vi.fn()
    const expired = new CrapsGatewayError('Expired', 'craps-round-not-found', 404)
    if (source === 'restoration') { sessionStorage.setItem(key(), id); server.readError = expired }
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    if (source === 'reconciliation') {
      act(() => result.current.start(10))
      await waitFor(() => expect(result.current.busy).toBe(false))
      server.loseResponse('roll'); server.readError = expired
      act(() => result.current.roll())
      await waitFor(() => expect(result.current.busy).toBe(false))
    }
    expect(result.current.round).toBeNull()
    expect(result.current.recovery).toBe('ready')
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(result.current.error).toContain('expired')
    expect(callback).toHaveBeenLastCalledWith(null)
    server.readError = null
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.round?.stake).toBe(5))
  })

  it('does not read an unrelated old hand after losing the new-hand response', async () => {
    const server = fakeServer()
    server.state = wonHand()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.startRound.mockRejectedValueOnce(new TypeError('Response lost'))
    act(() => result.current.start(5))
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.error).toContain('Bet response lost')
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
    expect(result.current.round?.phase).toBe('resolved')
  })
})

describe('Craps scope and stale request isolation', () => {
  it('restores the account-and-mode-scoped hand after remount without making a new wager', async () => {
    const server = fakeServer()
    const first = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(first.result.current.busy).toBe(false))
    act(() => first.result.current.start(10))
    await waitFor(() => expect(first.result.current.busy).toBe(false))
    act(() => first.result.current.roll())
    await waitFor(() => expect(first.result.current.busy).toBe(false))
    expect(sessionStorage.getItem(key())).toBe(id)
    first.unmount()
    const restored = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(restored.result.current.busy).toBe(false))
    expect(restored.result.current.round?.point).toBe(6)
    expect(server.gateway.startRound).toHaveBeenCalledTimes(1)
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound.mock.calls[0]?.[0]).toBe(id)
  })

  it('does not restore another account’s hand and returns to the original scoped hand when switching back', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result, rerender } = renderHook(({ account }) => useCrapsTable(server.gateway, account), { initialProps: { account: player } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    rerender({ account: 'other-account' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round).toBeNull()
    expect(sessionStorage.getItem(key('other-account'))).toBeNull()
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
    rerender({ account: player })
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round?.point).toBe(6)
    expect(server.gateway.getRound).toHaveBeenCalledTimes(2)
  })

  it('restores only the changed mode’s stored hand when status changes', async () => {
    const server = fakeServer()
    const newMode = 'another-practice-mode'
    sessionStorage.setItem(key(), id)
    sessionStorage.setItem(key(player, newMode), otherId)
    server.gateway.getRound.mockImplementation(async roundId => roundId === id ? pointHand(id) : wonHand(otherId))
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, mode: newMode })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round?.roundId).toBe(otherId)
    expect(result.current.round?.phase).toBe('resolved')
    expect(server.gateway.getRound.mock.calls.map(call => call[0])).toEqual([id, otherId])
    expect(sessionStorage.getItem(key())).toBe(id)
  })

  it('drops an old mode’s current hand when the new mode has no saved hand', async () => {
    const server = fakeServer()
    server.state = pointHand()
    sessionStorage.setItem(key(), id)
    const { result } = renderHook(() => useCrapsTable(server.gateway, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.gateway.getStatus.mockResolvedValue({ ...status, mode: 'another-mode' })
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round).toBeNull()
    expect(server.gateway.getRound).toHaveBeenCalledTimes(1)
  })

  it.each(['account', 'gateway', 'unmount'] as const)('aborts and ignores a late start response after %s even when the gateway ignores abort', async change => {
    const server = fakeServer()
    const replacement = fakeServer()
    const pending = deferred<CrapsRound>()
    server.gateway.startRound.mockImplementationOnce(() => pending.promise)
    const callback = vi.fn()
    const { result, rerender, unmount } = renderHook(({ account, gateway }) => useCrapsTable(gateway, account, true, callback), { initialProps: { account: player, gateway: server.gateway } })
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(10))
    const signal = server.gateway.startRound.mock.calls[0]?.[1]
    if (change === 'unmount') unmount()
    else {
      rerender({ account: change === 'account' ? 'other-player' : player, gateway: change === 'gateway' ? replacement.gateway : server.gateway })
      await waitFor(() => expect(result.current.busy).toBe(false))
    }
    expect(signal?.aborted).toBe(true)
    await act(async () => pending.resolve(ready()))
    expect(callback).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBeNull()
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
    if (change !== 'unmount') expect(result.current.round).toBeNull()
  })

  it('ignores an old restoration response after changing account', async () => {
    const server = fakeServer()
    const pending = deferred<CrapsRound>()
    sessionStorage.setItem(key(), id)
    server.gateway.getRound.mockImplementationOnce(() => pending.promise)
    const callback = vi.fn()
    const { result, rerender } = renderHook(({ account }) => useCrapsTable(server.gateway, account, true, callback), { initialProps: { account: player } })
    await waitFor(() => expect(server.gateway.getRound).toHaveBeenCalledTimes(1))
    const signal = server.gateway.getRound.mock.calls[0]?.[1]
    rerender({ account: 'other-player' })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(pointHand()))
    expect(signal?.aborted).toBe(true)
    expect(result.current.round).toBeNull()
    expect(callback).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key('other-player'))).toBeNull()
  })

  it('ignores a late status response from a replaced gateway', async () => {
    const server = fakeServer()
    const replacement = fakeServer({ ...status, minimumStake: 5 })
    const pending = deferred<CrapsStatus>()
    server.gateway.getStatus.mockImplementationOnce(() => pending.promise)
    const { result, rerender } = renderHook(({ gateway }) => useCrapsTable(gateway, player), { initialProps: { gateway: server.gateway } })
    rerender({ gateway: replacement.gateway })
    await waitFor(() => expect(result.current.busy).toBe(false))
    await act(async () => pending.resolve(status))
    expect(result.current.status?.minimumStake).toBe(5)
    expect(server.gateway.getStatus.mock.calls[0]?.[0]?.aborted).toBe(true)
  })
})

describe('Craps dice settlement and callbacks', () => {
  it('keeps normal-motion settlement locked for 780 ms and publishes the callback after reveal', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    server.state = ready()
    sessionStorage.setItem(key(), id)
    const callback = vi.fn()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    vi.useFakeTimers()
    await act(async () => result.current.roll())
    expect(result.current.motion).toBe('settling')
    expect(result.current.pendingRound?.phase).toBe('point')
    expect(result.current.round?.phase).toBe('come-out')
    expect(result.current.busy).toBe(true)
    expect(callback).not.toHaveBeenCalled()
    act(() => { result.current.roll(); result.current.placeOdds(1); result.current.start(10); result.current.clear() })
    await act(async () => vi.advanceTimersByTimeAsync(779))
    expect(callback).not.toHaveBeenCalled()
    expect(result.current.round?.phase).toBe('come-out')
    await act(async () => vi.advanceTimersByTimeAsync(1))
    expect(result.current.round?.phase).toBe('point')
    expect(result.current.motion).toBe('idle')
    expect(result.current.pendingRound).toBeNull()
    expect(result.current.busy).toBe(false)
    expect(callback).toHaveBeenCalledTimes(1)
    expect(callback.mock.calls[0]?.[0].phase).toBe('point')
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.placeOdds).not.toHaveBeenCalled()
    expect(server.gateway.startRound).not.toHaveBeenCalled()
  })

  it('reveals reduced-motion outcomes immediately without installing a dice delay', async () => {
    const server = fakeServer()
    sessionStorage.setItem(key(), id)
    const callback = vi.fn()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    vi.useFakeTimers()
    await act(async () => result.current.roll())
    expect(result.current.round?.phase).toBe('point')
    expect(result.current.busy).toBe(false)
    expect(callback).toHaveBeenCalledTimes(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('also waits for reveal before publishing an accepted-but-lost roll recovered by GET', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    sessionStorage.setItem(key(), id)
    const callback = vi.fn()
    const { result } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    server.loseResponse('roll')
    vi.useFakeTimers()
    await act(async () => result.current.roll())
    expect(server.gateway.getRound).toHaveBeenCalledTimes(2)
    expect(result.current.pendingRound?.phase).toBe('point')
    expect(callback).not.toHaveBeenCalled()
    await act(async () => vi.advanceTimersByTimeAsync(780))
    expect(callback).toHaveBeenCalledTimes(1)
    expect(result.current.round?.phase).toBe('point')
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
  })

  it('does not publish a pending reveal after unmount and aborts its mutation signal', async () => {
    setReducedMotion(false)
    const server = fakeServer()
    sessionStorage.setItem(key(), id)
    const callback = vi.fn()
    const { result, unmount } = renderHook(() => useCrapsTable(server.gateway, player, true, callback))
    await waitFor(() => expect(result.current.busy).toBe(false))
    callback.mockClear()
    vi.useFakeTimers()
    await act(async () => result.current.roll())
    const signal = server.gateway.roll.mock.calls[0]?.[1]
    unmount()
    await act(async () => vi.advanceTimersByTimeAsync(780))
    expect(signal?.aborted).toBe(true)
    expect(callback).not.toHaveBeenCalled()
  })
})

describe('Craps optional legacy gateway compatibility', () => {
  it('plays normally without getRound or persistence when the legacy gateway succeeds', async () => {
    const server = fakeServer()
    const { getRound: _read, ...legacy } = server.gateway
    const { result } = renderHook(() => useCrapsTable(legacy, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(10))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.roll())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.round?.point).toBe(6)
    expect(result.current.recovery).toBe('ready')
    expect(server.gateway.getRound).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(key())).toBeNull()
  })

  it('cannot unlock an uncertain legacy hand using status-only retry and roll it again', async () => {
    const server = fakeServer()
    const { getRound: _read, ...legacy } = server.gateway
    const { result } = renderHook(() => useCrapsTable(legacy, player))
    await waitFor(() => expect(result.current.busy).toBe(false))
    act(() => result.current.start(10))
    await waitFor(() => expect(result.current.busy).toBe(false))
    server.loseResponse('roll')
    act(() => result.current.roll())
    await waitFor(() => expect(result.current.recovery).toBe('failed'))
    act(() => result.current.retry())
    await waitFor(() => expect(result.current.busy).toBe(false))
    expect(result.current.recovery).toBe('failed')
    act(() => result.current.roll())
    expect(server.gateway.roll).toHaveBeenCalledTimes(1)
    expect(server.gateway.getRound).not.toHaveBeenCalled()
  })
})

function setReducedMotion(reduced: boolean) {
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({ matches: reduced && query === '(prefers-reduced-motion: reduce)', media: query, onchange: null, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() })))
}

function advance(round: CrapsRound, outcome: CrapsOutcome): CrapsRound {
  return {
    ...round,
    phase: outcome.isTerminal ? 'resolved' : 'point',
    point: outcome.result === 'point-established' ? outcome.total : round.point,
    rolls: [
      ...round.rolls.map(roll => ({ ...roll, result: null })),
      { rollNumber: round.rolls.length + 1, first: outcome.first, second: outcome.second, total: outcome.total, result: outcome.result },
    ],
    lastOutcome: outcome,
    extraBets: (round.extraBets ?? []).map(bet => bet.resolved ? bet : bet.kind !== 'odds'
      ? { ...bet, resolved: true, won: false, totalReturn: 0 }
      : outcome.isTerminal ? { ...bet, resolved: true, won: outcome.result === 'point-hit', totalReturn: outcome.result === 'point-hit' ? bet.stake * 2.2 : 0 } : bet),
  }
}

function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

function fakeServer(tableStatus: CrapsStatus = status) {
  type Operation = 'roll' | 'odds'
  type TestGateway = Required<CrapsGateway>
  let lost: Operation | null = null
  const server: { state: CrapsRound; readError: Error | null; loseResponse: (operation: Operation) => void; gateway: Mocked<TestGateway> } = {
    state: ready(), readError: null,
    loseResponse(operation: Operation) { lost = operation },
    gateway: {} as Mocked<TestGateway>,
  }
  function answer(operation: Operation): CrapsRound {
    if (lost === operation) { lost = null; throw new TypeError('Response lost after server commit') }
    return clone(server.state)
  }
  function gatewayMethods(): Mocked<TestGateway> {
    return {
      getStatus: vi.fn(async (_signal?: AbortSignal) => clone(tableStatus)),
      getRound: vi.fn(async (_roundId: string, _signal?: AbortSignal) => { if (server.readError) throw server.readError; return clone(server.state) }),
      startRound: vi.fn(async (stake: number, _signal?: AbortSignal, extras?: readonly CrapsExtraBetRequest[]) => {
        server.state = { ...ready(), stake, extraBets: (extras ?? []).map(bet => ({ ...bet, resolved: false, won: false, totalReturn: null })) }
        return clone(server.state)
      }),
      roll: vi.fn(async (_roundId: string, _signal?: AbortSignal) => {
        if (server.state.phase === 'resolved') throw new CrapsGatewayError('Hand already resolved', 'craps-invalid-action', 400)
        server.state = advance(server.state, server.state.phase === 'come-out'
          ? { first: 3, second: 3, total: 6, result: 'point-established', isTerminal: false, totalReturn: null }
          : { first: 2, second: 3, total: 5, result: 'no-decision', isTerminal: false, totalReturn: null })
        return answer('roll')
      }),
      placeOdds: vi.fn(async (_roundId: string, stake: number, _signal?: AbortSignal) => {
        if (server.state.phase !== 'point' || server.state.extraBets?.some(bet => bet.kind === 'odds' && !bet.resolved)) throw new CrapsGatewayError('Odds unavailable', 'craps-invalid-action', 400)
        server.state = { ...server.state, extraBets: [...server.state.extraBets ?? [], { kind: 'odds', stake, resolved: false, won: false, totalReturn: null }] }
        return answer('odds')
      }),
    }
  }
  server.gateway = gatewayMethods()
  return server
}
