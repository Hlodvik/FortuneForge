import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SnakeDirection, SnakeGameState, SnakeGateway, SnakeOptions, SnakeStatus } from './contracts'
import { SnakeGatewayError } from './httpSnakeGateway'
import { SnakeRunController } from './snakeRunController'

const status: SnakeStatus = { available: true, width: 20, height: 20, foodScore: 10, tickMilliseconds: 115, mode: 'arcade' }

function state(patch: Partial<SnakeGameState> = {}): SnakeGameState {
  return { gameId: 'snake-1', width: 20, height: 20, body: [{ x: 10, y: 10 }, { x: 9, y: 10 }, { x: 8, y: 10 }], food: { x: 1, y: 2 }, direction: 'right', score: 0, bestScore: 0, moves: 0, length: 3, phase: 'playing', lastEvent: 'started', scoreGained: 0, message: 'Ready.', ...patch }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}

async function settle() { for (let index = 0; index < 12; index++) await Promise.resolve() }

function fixture(canRun: () => boolean = () => true) {
  let current = state()
  const calls: Array<{ kind: string; at: number; direction?: SnakeDirection }> = []
  const gateway = {
    getStatus: vi.fn(async (_signal?: AbortSignal) => { calls.push({ kind: 'status', at: Date.now() }); return status }),
    startGame: vi.fn(async (_seed?: number, _signal?: AbortSignal, options?: SnakeOptions) => {
      calls.push({ kind: 'start', at: Date.now() }); current = state({ width: options?.width ?? 20, height: options?.height ?? 20 }); return current
    }),
    turn: vi.fn(async (_gameId: string, direction: SnakeDirection, _signal?: AbortSignal) => {
      calls.push({ kind: 'turn', direction, at: Date.now() }); current = { ...current, direction, lastEvent: 'turned' }; return current
    }),
    tick: vi.fn(async (_gameId: string, _signal?: AbortSignal) => {
      calls.push({ kind: 'tick', at: Date.now() }); current = { ...current, moves: current.moves + 1, lastEvent: 'moved' }; return current
    }),
    reset: vi.fn(async (_gameId: string, _seed?: number, _signal?: AbortSignal, options?: SnakeOptions) => {
      calls.push({ kind: 'reset', at: Date.now() }); current = state({ gameId: 'snake-2', width: options?.width ?? 20, height: options?.height ?? 20 }); return current
    }),
  } satisfies SnakeGateway
  const controller = new SnakeRunController(gateway, canRun)
  active.add(controller)
  return { controller, gateway, calls }
}

const active = new Set<SnakeRunController>()
beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(0) })
afterEach(() => { active.forEach(controller => controller.dispose()); active.clear(); vi.useRealTimers() })

describe('Snake cabinet movement and queued input', () => {
  it('opens a ready run without moving or starting a background loop', async () => {
    const { controller, gateway } = fixture()
    controller.activate()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'loading', busy: true })
    await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', busy: false, game: { gameId: 'snake-1', moves: 0 } })
    expect(gateway.getStatus).toHaveBeenCalledTimes(1)
    expect(gateway.startGame).toHaveBeenCalledWith(undefined, expect.any(AbortSignal), { width: 20, height: 20 })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
  })

  it('keeps the original 115ms movement deadline while serializing one queued turn per step', async () => {
    const { controller, gateway, calls } = fixture()
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(50)
    controller.turn('up')
    await vi.advanceTimersByTimeAsync(50)
    controller.turn('left')
    await vi.advanceTimersByTimeAsync(14)
    expect(gateway.turn).not.toHaveBeenCalled()
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.turn.mock.calls.map(call => call[1])).toEqual(['up'])
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    expect(calls.filter(call => call.kind === 'turn' || call.kind === 'tick')).toEqual([{ kind: 'turn', direction: 'up', at: 115 }, { kind: 'tick', at: 115 }])
    expect(controller.getSnapshot().pendingDirection).toBe('left')
    await vi.advanceTimersByTimeAsync(115)
    expect(gateway.turn.mock.calls.map(call => call[1])).toEqual(['up', 'left'])
    expect(gateway.tick).toHaveBeenCalledTimes(2)
    expect(controller.getSnapshot().pendingDirection).toBeNull()
  })

  it('bounds queued turns to two and discards repeated headings and reversals against the planned heading', async () => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle()
    controller.turn('left')
    expect(controller.getSnapshot().phase).toBe('ready')
    controller.turn('up'); controller.turn('up'); controller.turn('down'); controller.turn('left'); controller.turn('down')
    expect(controller.getSnapshot()).toMatchObject({ phase: 'playing', pendingDirection: 'up' })
    await vi.advanceTimersByTimeAsync(345)
    expect(gateway.turn.mock.calls.map(call => call[1])).toEqual(['up', 'left'])
    expect(gateway.tick).toHaveBeenCalledTimes(3)
  })

  it('lets a same-heading intent start a ready run without inventing a turn', async () => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle(); controller.turn('right')
    expect(controller.getSnapshot()).toMatchObject({ phase: 'playing', pendingDirection: null })
    await vi.advanceTimersByTimeAsync(115)
    expect(gateway.turn).not.toHaveBeenCalled()
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('does not overlap a delayed turn with a tick or lose the next queued direction', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.turn.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    controller.turn('left')
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.turn).toHaveBeenCalledTimes(1)
    expect(gateway.tick).not.toHaveBeenCalled()
    delayed.resolve(state({ direction: 'up' })); await settle()
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(114)
    expect(gateway.turn).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.turn.mock.calls.map(call => call[1])).toEqual(['up', 'left'])
  })

  it.each(['won', 'lost'] as const)('stops further movement and clears queued input when the gateway reports %s', async phase => {
    const { controller, gateway } = fixture()
    gateway.tick.mockResolvedValueOnce(state({ phase, lastEvent: phase }))
    controller.activate(); await settle(); controller.turn('up'); controller.turn('left')
    await vi.advanceTimersByTimeAsync(115)
    expect(controller.getSnapshot()).toMatchObject({ phase, pendingDirection: null })
    controller.turn('down'); controller.play(); controller.resume()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    expect(gateway.turn).toHaveBeenCalledTimes(1)
  })
})

describe('Snake pause and cadence', () => {
  it('clears queued input and resumes with a complete interval without hidden catch-up', async () => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(80)
    controller.turn('up'); controller.pause(); controller.turn('left')
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', pendingDirection: null })
    await vi.advanceTimersByTimeAsync(10_000)
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    await vi.advanceTimersByTimeAsync(114)
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    expect(gateway.turn).not.toHaveBeenCalled()
  })

  it('pauses at the next movement deadline when the cabinet is no longer eligible to run', async () => {
    let visible = true
    const { controller, gateway } = fixture(() => visible)
    controller.activate(); await settle(); controller.play()
    visible = false
    await vi.advanceTimersByTimeAsync(115)
    expect(controller.getSnapshot().phase).toBe('paused')
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    expect(controller.getSnapshot().phase).toBe('paused')
    visible = true
    await vi.advanceTimersByTimeAsync(10_000)
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    await vi.advanceTimersByTimeAsync(115)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('does not issue a tick when a delayed turn resolves after pause', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.turn.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    controller.pause()
    delayed.resolve(state({ direction: 'up' })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', game: { direction: 'up', moves: 0 } })
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    await vi.advanceTimersByTimeAsync(115)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('does not revive an old movement step if resume happens before its delayed turn finishes', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.turn.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    controller.pause()
    await vi.advanceTimersByTimeAsync(30)
    controller.resume()
    delayed.resolve(state({ direction: 'up' })); await settle()
    expect(controller.getSnapshot().phase).toBe('playing')
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(114)
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('rechecks visibility before issuing a tick after an awaited turn', async () => {
    let visible = true
    const { controller, gateway } = fixture(() => visible)
    const delayed = deferred<SnakeGameState>()
    gateway.turn.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    visible = false
    delayed.resolve(state({ direction: 'up' })); await settle()
    expect(gateway.tick).not.toHaveBeenCalled()
    expect(controller.getSnapshot().phase).toBe('paused')
    visible = true
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    await vi.advanceTimersByTimeAsync(115)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('accepts an already issued tick result while keeping the cabinet paused', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.tick.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(115)
    controller.pause()
    delayed.resolve(state({ moves: 1 })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', game: { moves: 1 } })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it.each([['relaxed', 170], ['classic', 115], ['turbo', 78]] as const)('preserves %s cadence of %ims', async (speed, interval) => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle(); controller.configure(speed, 20); controller.play()
    await vi.advanceTimersByTimeAsync(interval - 1)
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(interval)
    expect(gateway.tick).toHaveBeenCalledTimes(2)
  })

  it('changing speed pauses the run and applies a full new interval only after resume', async () => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(60)
    controller.configure('turbo', 20)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', speed: 'turbo' })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
    controller.resume()
    await vi.advanceTimersByTimeAsync(77)
    expect(gateway.tick).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('starts a requested new run paused when eligibility is lost before delayed setup finishes', async () => {
    let visible = true
    const { controller, gateway } = fixture(() => visible)
    controller.activate(); await settle()
    const delayed = deferred<SnakeGameState>()
    gateway.reset.mockImplementationOnce(() => delayed.promise)
    controller.newGame(true)
    await settle(); visible = false
    delayed.resolve(state({ gameId: 'snake-2' })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', busy: false })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
  })

  it('opens a delayed fresh start paused when run eligibility changes during recovery', async () => {
    let visible = true
    const { controller, gateway } = fixture(() => visible)
    gateway.startGame.mockRejectedValueOnce(new Error('start failed'))
    controller.activate(); await settle()
    const delayed = deferred<SnakeGameState>()
    gateway.startGame.mockImplementationOnce(() => delayed.promise)
    controller.newGame(true); await settle()
    visible = false
    delayed.resolve(state({ gameId: 'fresh-recovery' })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'paused', game: { gameId: 'fresh-recovery' }, busy: false })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).not.toHaveBeenCalled()
  })
})

describe('Snake serial setup, error recovery and lifecycle leases', () => {
  it('gates repeated new-run intent synchronously before the async reset completes', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    controller.activate(); await settle()
    gateway.reset.mockImplementationOnce(() => delayed.promise)
    controller.newGame(); controller.newGame(); controller.retry()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'loading', busy: true })
    await settle()
    expect(gateway.reset).toHaveBeenCalledTimes(1)
    delayed.resolve(state({ gameId: 'snake-2' })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'playing', busy: false, game: { gameId: 'snake-2' } })
  })

  it('waits for an issued tick before reset and cannot resurrect its old state afterward', async () => {
    const { controller, gateway } = fixture()
    const delayedTick = deferred<SnakeGameState>()
    const delayedReset = deferred<SnakeGameState>()
    gateway.tick.mockImplementationOnce(() => delayedTick.promise)
    gateway.reset.mockImplementationOnce(() => delayedReset.promise)
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(115)
    controller.newGame(true, 'relaxed', 16)
    await settle()
    expect(gateway.reset).not.toHaveBeenCalled()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'loading', busy: true })
    delayedTick.resolve(state({ moves: 7 })); await settle()
    expect(gateway.reset).toHaveBeenCalledTimes(1)
    expect(gateway.reset).toHaveBeenCalledWith('snake-1', undefined, expect.any(AbortSignal), { width: 16, height: 16 })
    expect(controller.getSnapshot().phase).toBe('loading')
    delayedReset.resolve(state({ gameId: 'snake-2', width: 16, height: 16 })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'playing', game: { gameId: 'snake-2', moves: 0 }, speed: 'relaxed', boardSize: 16 })
    await vi.advanceTimersByTimeAsync(169)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('does not issue a tick after a delayed turn when reset was requested', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.turn.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    controller.newGame()
    await settle()
    expect(gateway.reset).not.toHaveBeenCalled()
    delayed.resolve(state({ direction: 'up' })); await settle()
    expect(gateway.tick).not.toHaveBeenCalled()
    expect(gateway.reset).toHaveBeenCalledTimes(1)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'playing', game: { gameId: 'snake-2', direction: 'right' } })
  })

  it.each([16, 20, 24])('passes selected board size %i to reset without altering the gateway outcome', async boardSize => {
    const { controller, gateway } = fixture()
    controller.activate(); await settle(); controller.newGame(false, 'classic', boardSize); await settle()
    expect(gateway.reset).toHaveBeenCalledWith('snake-1', undefined, expect.any(AbortSignal), { width: boardSize, height: boardSize })
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', boardSize, game: { width: boardSize, height: boardSize } })
  })

  it.each(['turn', 'tick'] as const)('stops the loop after a %s failure and retries only on explicit intent', async operation => {
    const { controller, gateway } = fixture()
    gateway[operation].mockRejectedValueOnce(new SnakeGatewayError('Connection interrupted.', 'snake-unavailable', 503))
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'error', busy: false, error: 'Connection interrupted.', pendingDirection: null })
    const calls = gateway[operation].mock.calls.length
    await vi.advanceTimersByTimeAsync(5_000)
    expect(gateway[operation]).toHaveBeenCalledTimes(calls)
    expect(gateway.reset).not.toHaveBeenCalled()
    controller.retry(); await settle()
    expect(gateway.reset).toHaveBeenCalledTimes(1)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', error: null })
  })

  it('recovers unavailable initial status with an explicit fresh status attempt', async () => {
    const { controller, gateway } = fixture()
    gateway.getStatus.mockResolvedValueOnce({ ...status, available: false })
    controller.activate(); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'error', busy: false, game: null, error: 'Snake is unavailable right now.' })
    expect(gateway.startGame).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.getStatus).toHaveBeenCalledTimes(1)
    controller.retry(); await settle()
    expect(gateway.getStatus).toHaveBeenCalledTimes(2)
    expect(gateway.startGame).toHaveBeenCalledTimes(1)
    expect(controller.getSnapshot().phase).toBe('ready')
  })

  it('shows a bounded opening error and remains recoverable when the initial start rejects', async () => {
    const { controller, gateway } = fixture()
    gateway.startGame.mockRejectedValueOnce(new Error('private transport detail'))
    controller.activate(); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'error', error: 'Snake could not be prepared. Try again.', busy: false })
    controller.retry(); await settle()
    expect(gateway.startGame).toHaveBeenCalledTimes(2)
    expect(controller.getSnapshot().phase).toBe('ready')
  })

  it('aborts pending status and ignores stale success after disposal', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeStatus>()
    gateway.getStatus.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle()
    const signal = gateway.getStatus.mock.calls[0]?.[0]
    expect(signal?.aborted).toBe(false)
    controller.dispose()
    expect(signal?.aborted).toBe(true)
    delayed.resolve(status); await settle()
    expect(gateway.startGame).not.toHaveBeenCalled()
    expect(controller.getSnapshot().game).toBeNull()
  })

  it('supports activate-dispose-activate replay while ignoring an aborted old start response', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.startGame.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle()
    const signal = gateway.startGame.mock.calls[0]?.[1]
    controller.dispose(); controller.activate(); await settle()
    expect(signal?.aborted).toBe(true)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', game: { gameId: 'snake-1' } })
    delayed.resolve(state({ gameId: 'old-lease', score: 999 })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', game: { gameId: 'snake-1', score: 0 } })
    expect(gateway.startGame).toHaveBeenCalledTimes(2)
  })

  it.each(['turn', 'tick'] as const)('aborts pending %s and ignores its stale result after a new lease opens', async operation => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway[operation].mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.turn('up')
    await vi.advanceTimersByTimeAsync(115)
    const call = gateway[operation].mock.calls[0]
    const signal = call?.[operation === 'turn' ? 2 : 1] as AbortSignal | undefined
    controller.dispose(); controller.activate(); await settle()
    expect(signal?.aborted).toBe(true)
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', game: { gameId: 'snake-2' } })
    delayed.resolve(state({ gameId: 'old-lease', moves: 50 })); await settle()
    expect(controller.getSnapshot()).toMatchObject({ phase: 'ready', game: { gameId: 'snake-2', moves: 0 } })
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).toHaveBeenCalledTimes(operation === 'tick' ? 1 : 0)
  })

  it('ignores a stale request failure after disposal and never schedules a replacement tick', async () => {
    const { controller, gateway } = fixture()
    const delayed = deferred<SnakeGameState>()
    gateway.tick.mockImplementationOnce(() => delayed.promise)
    controller.activate(); await settle(); controller.play()
    await vi.advanceTimersByTimeAsync(115)
    controller.dispose()
    delayed.reject(new Error('stale failure')); await settle()
    expect(controller.getSnapshot().error).toBeNull()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(gateway.tick).toHaveBeenCalledTimes(1)
  })

  it('publishes gateway outcomes unchanged and supports unsubscribing a listener', async () => {
    const { controller, gateway } = fixture()
    const notified = vi.fn()
    const unsubscribe = controller.subscribe(notified)
    controller.activate(); await settle()
    expect(notified).toHaveBeenCalled()
    unsubscribe(); const prior = notified.mock.calls.length
    const authoritative = state({ score: 40, bestScore: 70, moves: 12, length: 7, message: 'Gateway result.' })
    gateway.tick.mockResolvedValueOnce(authoritative)
    controller.play(); await vi.advanceTimersByTimeAsync(115)
    expect(controller.getSnapshot().game).toBe(authoritative)
    expect(notified).toHaveBeenCalledTimes(prior)
  })
})
