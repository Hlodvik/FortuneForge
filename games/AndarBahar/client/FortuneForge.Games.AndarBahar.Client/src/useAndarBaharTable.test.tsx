// @vitest-environment jsdom
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AndarBaharGatewayError, type AndarBaharGateway, type AndarBaharRound, type AndarBaharStatus } from './contracts'
import { readPending, writePending, type PendingWager } from './andarBaharSession'
import { useAndarBaharTable } from './useAndarBaharTable'
import { andarBaharRequestTimeoutMs } from './andarBaharRequest'
import { playAndarBaharAudio } from './andarBaharAudio'

vi.mock('./andarBaharAudio', () => ({ playAndarBaharAudio: vi.fn(), stopAndarBaharAudio: vi.fn() }))

const player = 'review-player'
const pendingKey = (user = player) => `fortuneforge:andar-bahar:pending:${user}`
const status: AndarBaharStatus = {
  available: true, minimumStake: .5, maximumStake: 100, stakeIncrement: .5,
  balance: 100, mode: 'development-practice-andar-first',
}
const settled: AndarBaharRound = {
  roundId: 'settled-review-round', balance: 104.5, phase: 'settled',
  joker: { rank: 'ace', suit: 'clubs' },
  deals: [{ number: 1, side: 'andar', card: { rank: 'ace', suit: 'hearts' } }],
  winner: 'andar', side: 'andar', stake: 5, disposition: 'win', profit: 4.5, totalReturn: 9.5,
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(complete => { resolve = complete })
  return { promise, resolve }
}

function gateway() {
  return {
    getStatus: vi.fn<AndarBaharGateway['getStatus']>().mockResolvedValue(status),
    getLatestRound: vi.fn<AndarBaharGateway['getLatestRound']>().mockResolvedValue(null),
    getRound: vi.fn<AndarBaharGateway['getRound']>().mockResolvedValue(settled),
    createRound: vi.fn<AndarBaharGateway['createRound']>().mockResolvedValue(settled),
  } satisfies AndarBaharGateway
}

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('Andar Bahar wager recovery and response isolation', () => {
  it('does not let a delayed rejection balance refresh overwrite a later settled wager', async () => {
    const delayedStatus = deferred<AndarBaharStatus>()
    const api = gateway()
    api.getStatus.mockResolvedValueOnce(status).mockImplementationOnce(() => delayedStatus.promise)
    api.createRound
      .mockRejectedValueOnce(new AndarBaharGatewayError('The wager was rejected.', 'andar-bahar-invalid-request', 400))
      .mockResolvedValueOnce(settled)
    const onBalance = vi.fn()
    const { result } = renderHook(() => useAndarBaharTable(api, player, onBalance))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    expect(result.current.stake).toBe(5)

    await act(async () => { await result.current.submit() })
    expect(result.current.phase).toBe('betting')
    expect(api.getStatus).toHaveBeenCalledTimes(2)
    expect(result.current.status?.balance).toBe(100)

    await act(async () => { await result.current.submit() })
    expect(result.current.phase).toBe('dealing')
    act(() => result.current.skip())
    expect(result.current.phase).toBe('result')
    expect(result.current.status?.balance).toBe(104.5)
    expect(readPending(pendingKey())).toBeNull()
    expect(onBalance).toHaveBeenCalledExactlyOnceWith(104.5)

    // The rejected wager's earlier balance snapshot arrives only after the newer settlement.
    await act(async () => { delayedStatus.resolve({ ...status, balance: 100 }); await delayedStatus.promise })
    expect(result.current.status?.balance).toBe(104.5)
    expect(result.current.round?.roundId).toBe(settled.roundId)
    expect(api.createRound.mock.calls[0][2].idempotencyKey).not.toBe(api.createRound.mock.calls[1][2].idempotencyKey)
  })

  it('stores pending keys only in this tab and completion cannot erase a different request key', async () => {
    const delayedRound = deferred<AndarBaharRound>()
    const api = gateway()
    api.createRound.mockImplementationOnce(() => delayedRound.promise)
    const { result } = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    let submission!: Promise<void>
    act(() => { submission = result.current.submit() })
    expect(result.current.phase).toBe('submitting')
    const original = readPending(pendingKey())!
    expect(original).toEqual(expect.objectContaining({ side: 'andar', stake: 5 }))
    expect(sessionStorage.getItem(pendingKey())).not.toBeNull()
    expect(localStorage.getItem(pendingKey())).toBeNull()

    // A stale completion must only remove the exact pending request it owns.
    const newer: PendingWager = {
      key: 'andar-bahar-newer-request-12345678', side: 'bahar', stake: 1, chips: [1],
    }
    expect(writePending(pendingKey(), newer)).toBe(true)
    await act(async () => { delayedRound.resolve(settled); await submission })
    act(() => result.current.skip())
    expect(result.current.phase).toBe('result')
    expect(readPending(pendingKey())).toEqual(newer)
    expect(readPending(pendingKey())?.key).not.toBe(original.key)
    expect(localStorage.getItem(pendingKey())).toBeNull()
  })

  it('keeps an uncertain wager through remount and replays its original key and body exactly once', async () => {
    const api = gateway()
    api.createRound.mockRejectedValueOnce(new TypeError('The response was lost.')).mockResolvedValueOnce(settled)
    const first = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(first.result.current.phase).toBe('betting'))
    act(() => first.result.current.place('andar'))
    await act(async () => { await first.result.current.submit() })
    expect(first.result.current.phase).toBe('recover')
    expect(first.result.current.editable).toBe(false)
    const original = readPending(pendingKey())!
    expect(original).not.toBeNull()
    expect(localStorage.getItem(pendingKey())).toBeNull()
    first.unmount()
    expect(readPending(pendingKey())).toEqual(original)

    const restored = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(restored.result.current.phase).toBe('dealing'))
    expect(api.createRound).toHaveBeenCalledTimes(2)
    const [firstSide, firstStake, firstOptions] = api.createRound.mock.calls[0]
    const [restoredSide, restoredStake, restoredOptions] = api.createRound.mock.calls[1]
    expect([restoredSide, restoredStake, restoredOptions.idempotencyKey])
      .toEqual([firstSide, firstStake, firstOptions.idempotencyKey])
    expect(restoredOptions.signal).toBeInstanceOf(AbortSignal)
    act(() => restored.result.current.skip())
    expect(readPending(pendingKey())).toBeNull()
    expect(restored.result.current.status?.balance).toBe(104.5)
    expect(api.getLatestRound).toHaveBeenCalledTimes(1) // Initial clean mount only; pending recovery uses POST replay.
  })

  it('ignores an old player response after switching identity and keeps that player recovery key scoped', async () => {
    const oldResponse = deferred<AndarBaharRound>()
    const previousApi = gateway(), currentApi = gateway()
    previousApi.createRound.mockImplementationOnce(() => oldResponse.promise)
    currentApi.getStatus.mockResolvedValue({ ...status, balance: 200 })
    const onBalance = vi.fn()
    const { result, rerender } = renderHook(
      ({ api, user }) => useAndarBaharTable(api, user, onBalance),
      { initialProps: { api: previousApi, user: 'previous-player' } },
    )
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    let previousSubmission!: Promise<void>
    act(() => { previousSubmission = result.current.submit() })
    const previousPending = readPending(pendingKey('previous-player'))!
    expect(previousPending).not.toBeNull()
    await act(async () => { await Promise.resolve() })
    const previousSignal = previousApi.createRound.mock.calls[0][2].signal!
    expect(previousSignal.aborted).toBe(false)

    rerender({ api: currentApi, user: 'current-player' })
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    expect(result.current.status?.balance).toBe(200)
    expect(previousSignal.aborted).toBe(true)
    await act(async () => { oldResponse.resolve(settled); await previousSubmission })
    expect(result.current.round).toBeNull()
    expect(result.current.phase).toBe('betting')
    expect(result.current.status?.balance).toBe(200)
    expect(onBalance).not.toHaveBeenCalled()
    expect(readPending(pendingKey('previous-player'))).toEqual(previousPending)
    expect(readPending(pendingKey('current-player'))).toBeNull()
    expect(currentApi.createRound).not.toHaveBeenCalled()
  })

  it('times out an adapter that ignores abort and retries the original wager without accepting its late response', async () => {
    const late = deferred<AndarBaharRound>()
    const api = gateway()
    api.createRound.mockImplementationOnce(() => late.promise).mockResolvedValueOnce(settled)
    const onBalance = vi.fn()
    const { result } = renderHook(() => useAndarBaharTable(api, player, onBalance))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    vi.useFakeTimers()
    let submission!: Promise<void>
    act(() => { submission = result.current.submit() })
    const original = readPending(pendingKey())!
    await act(async () => { await vi.advanceTimersByTimeAsync(andarBaharRequestTimeoutMs); await submission })
    expect(result.current.phase).toBe('recover')
    expect(result.current.error).toMatch(/too long.*Restore your deal/)
    expect(result.current.editable).toBe(false)
    expect(readPending(pendingKey())).toEqual(original)
    expect(api.createRound.mock.calls[0][2].signal?.aborted).toBe(true)

    await act(async () => { await result.current.submit(true) })
    expect(result.current.phase).toBe('dealing')
    expect(api.createRound.mock.calls.map(([side, stake, options]) => [side, stake, options.idempotencyKey]))
      .toEqual([[original.side, original.stake, original.key], [original.side, original.stake, original.key]])
    act(() => result.current.skip())
    expect(result.current.status?.balance).toBe(settled.balance)
    expect(readPending(pendingKey())).toBeNull()
    await act(async () => { late.resolve({ ...settled, balance: 1 }); await late.promise })
    expect(result.current.status?.balance).toBe(settled.balance)
    expect(result.current.phase).toBe('result')
    expect(onBalance).toHaveBeenCalledExactlyOnceWith(settled.balance)
  })

  it('bounds automatic reload restoration and keeps the original key through another reload', async () => {
    const request: PendingWager = { key: 'andar-bahar-timeout-reload-12345678', side: 'andar', stake: 5, chips: [5] }
    writePending(pendingKey(), request)
    const api = gateway()
    api.createRound.mockReturnValueOnce(new Promise(() => undefined)).mockResolvedValueOnce(settled)
    vi.useFakeTimers()
    const first = renderHook(() => useAndarBaharTable(api, player))
    await act(async () => { await vi.advanceTimersByTimeAsync(andarBaharRequestTimeoutMs) })
    expect(first.result.current.phase).toBe('recover')
    expect(first.result.current.status?.available).toBe(true)
    expect(readPending(pendingKey())).toEqual(request)
    expect(api.createRound.mock.calls[0][2].signal?.aborted).toBe(true)
    first.unmount()
    vi.useRealTimers()

    const second = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(second.result.current.phase).toBe('dealing'))
    expect(api.createRound.mock.calls[1][2].idempotencyKey).toBe(request.key)
    expect(api.getLatestRound).not.toHaveBeenCalled()
    act(() => second.result.current.skip())
    expect(second.result.current.status?.balance).toBe(settled.balance)
    expect(readPending(pendingKey())).toBeNull()
  })

  it('blocks repeated restore submissions and completes one settlement despite repeated skip clicks', async () => {
    const response = deferred<AndarBaharRound>()
    const api = gateway()
    api.createRound.mockRejectedValueOnce(new TypeError('Connection lost')).mockImplementationOnce(() => response.promise)
    const onBalance = vi.fn()
    const { result } = renderHook(() => useAndarBaharTable(api, player, onBalance))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    await act(async () => { await result.current.submit() })
    expect(result.current.phase).toBe('recover')
    let restored!: Promise<void>
    act(() => { restored = result.current.submit(true); void result.current.submit(true) })
    await act(async () => { await Promise.resolve() })
    expect(api.createRound).toHaveBeenCalledTimes(2)
    expect(result.current.phase).toBe('submitting')
    await act(async () => { response.resolve(settled); await restored })
    vi.mocked(playAndarBaharAudio).mockClear()
    act(() => { result.current.skip(); result.current.skip() })
    expect(result.current.phase).toBe('result')
    expect(onBalance).toHaveBeenCalledExactlyOnceWith(settled.balance)
    expect(vi.mocked(playAndarBaharAudio).mock.calls.filter(([cue]) => cue === 'win')).toHaveLength(1)
    expect(result.current.history).toHaveLength(1)
    expect(readPending(pendingKey())).toBeNull()
  })

  it('aborts an outstanding wager on unmount without discarding its recovery record', async () => {
    const api = gateway()
    api.createRound.mockReturnValueOnce(new Promise(() => undefined))
    const onBalance = vi.fn()
    const { result, unmount } = renderHook(() => useAndarBaharTable(api, player, onBalance))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    let submission!: Promise<void>
    act(() => { submission = result.current.submit() })
    await act(async () => { await Promise.resolve() })
    const signal = api.createRound.mock.calls[0][2].signal!
    const request = readPending(pendingKey())
    unmount()
    await submission
    expect(signal.aborted).toBe(true)
    expect(readPending(pendingKey())).toEqual(request)
    expect(onBalance).not.toHaveBeenCalled()
  })

  it('finishes a normal animated deal once and selects a positive win cue only for profit', async () => {
    const api = gateway()
    const onBalance = vi.fn()
    const { result } = renderHook(() => useAndarBaharTable(api, player, onBalance))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('andar'))
    vi.useFakeTimers()
    await act(async () => { await result.current.submit() })
    expect(result.current.phase).toBe('dealing')
    expect(result.current.visible).toBe(0)
    await act(async () => { await vi.advanceTimersByTimeAsync(700) })
    expect(result.current.visible).toBe(1)
    expect(result.current.phase).toBe('dealing')
    vi.mocked(playAndarBaharAudio).mockClear()
    await act(async () => { await vi.advanceTimersByTimeAsync(650) })
    expect(result.current.phase).toBe('result')
    expect(onBalance).toHaveBeenCalledExactlyOnceWith(settled.balance)
    expect(playAndarBaharAudio).toHaveBeenCalledExactlyOnceWith('win', false)
    expect(readPending(pendingKey())).toBeNull()
    await act(async () => { await vi.advanceTimersByTimeAsync(andarBaharRequestTimeoutMs) })
    expect(onBalance).toHaveBeenCalledExactlyOnceWith(settled.balance)
    expect(result.current.history).toHaveLength(1)
  })

  it('uses the neutral result cue when the wager loses', async () => {
    const api = gateway()
    api.createRound.mockResolvedValue({ ...settled, side: 'bahar', disposition: 'loss', profit: -5, totalReturn: 0, balance: 95 })
    const { result } = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(result.current.phase).toBe('betting'))
    act(() => result.current.place('bahar'))
    await act(async () => { await result.current.submit() })
    vi.mocked(playAndarBaharAudio).mockClear()
    act(() => result.current.skip())
    expect(playAndarBaharAudio).toHaveBeenCalledExactlyOnceWith('result', false)
    expect(result.current.status?.balance).toBe(95)
  })

  it('automatically recovers an exact pending wager while the table is disabled for new bets', async () => {
    const request: PendingWager = { key: 'andar-bahar-disabled-reload-12345678', side: 'andar', stake: 5, chips: [5] }
    writePending(pendingKey(), request)
    const api = gateway()
    api.getStatus.mockResolvedValue({ ...status, available: false })
    const { result } = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(result.current.phase).toBe('dealing'))
    expect(api.createRound).toHaveBeenCalledTimes(1)
    expect(api.createRound.mock.calls[0][2].idempotencyKey).toBe(request.key)
    expect(api.getLatestRound).not.toHaveBeenCalled()
    act(() => result.current.skip())
    expect(result.current.status?.available).toBe(false)
    expect(result.current.editable).toBe(false)
    expect(readPending(pendingKey())).toBeNull()
    act(() => { result.current.clear(); result.current.place('bahar') })
    await act(async () => { await result.current.submit() })
    expect(api.createRound).toHaveBeenCalledTimes(1)
  })

  it('allows manual same-key recovery after a failed automatic replay on a disabled table', async () => {
    const request: PendingWager = { key: 'andar-bahar-disabled-retry-12345678', side: 'andar', stake: 5, chips: [5] }
    writePending(pendingKey(), request)
    const api = gateway()
    api.getStatus.mockResolvedValue({ ...status, available: false })
    api.createRound.mockRejectedValueOnce(new TypeError('The replay response was lost')).mockResolvedValueOnce(settled)
    const { result } = renderHook(() => useAndarBaharTable(api, player))
    await waitFor(() => expect(result.current.phase).toBe('recover'))
    expect(result.current.status?.available).toBe(false)
    expect(readPending(pendingKey())).toEqual(request)
    await act(async () => { await result.current.submit() })
    expect(api.createRound).toHaveBeenCalledTimes(1)
    await act(async () => { await result.current.submit(true) })
    expect(result.current.phase).toBe('dealing')
    expect(api.createRound.mock.calls.map(([side, stake, options]) => [side, stake, options.idempotencyKey]))
      .toEqual([[request.side, request.stake, request.key], [request.side, request.stake, request.key]])
    act(() => result.current.skip())
    expect(result.current.status?.balance).toBe(settled.balance)
    expect(result.current.editable).toBe(false)
    expect(readPending(pendingKey())).toBeNull()
  })
})
