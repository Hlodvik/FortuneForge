// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { HorseFlightGateway, HorseFlightStatus } from './contracts'
import { HorseFlightGame } from './HorseFlightGame'
import { horseFlightRecoveryKey, newHorseFlightRecovery, writeHorseFlightRecovery, type HorseFlightRecovery } from './horseFlightRecovery'
import { startHorse, stepHorse } from './horseFlightSimulation'

const freeScope = JSON.stringify(['free']), runId = 'horse_flight_free_' + 'a'.repeat(64)
const status: HorseFlightStatus = { available: true, tickMilliseconds: 20, balance: 100, mode: 'free', recoveryMode: 'free', recoveryScope: freeScope }
function gateway() {
  return {
    getStatus: vi.fn<HorseFlightGateway['getStatus']>().mockResolvedValue(status),
    start: vi.fn<HorseFlightGateway['start']>().mockResolvedValue({ runId, seed: 42, tickMilliseconds: 20, recoveryScope: freeScope }),
    complete: vi.fn<HorseFlightGateway['complete']>().mockImplementation(async (id, ticks, jumps, _options, rights) => {
      let game = startHorse(42)
      for (let tick = 0; tick < ticks; tick++) game = stepHorse(game, jumps.includes(tick), rights?.includes(tick)).state
      if (game.phase === 'running') throw new Error('Incomplete fixture')
      return { runId: id, totalTicks: ticks, jumpTicks: jumps, rightClickTicks: rights, balance: 100, phase: game.phase, score: game.score }
    }),
  }
}
async function flush() { await act(async () => { for (let index = 0; index < 20; index++) await Promise.resolve() }) }
async function time(milliseconds: number) { await act(async () => vi.advanceTimersByTimeAsync(milliseconds)); await flush() }
const saved = (owner = 'alex') => JSON.parse(localStorage.getItem(horseFlightRecoveryKey(owner, 'free'))!) as HorseFlightRecovery

beforeEach(() => {
  vi.useFakeTimers(); localStorage.clear()
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
})
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.clearAllTimers(); vi.useRealTimers() })

describe('Horse Flight owner-scoped reload recovery in the current scene', () => {
  it('restores accepted input history paused without admitting a second flight', async () => {
    const api = gateway(), first = render(<StrictMode><HorseFlightGame gateway={api} playerId="alex" autoStart /></StrictMode>)
    await flush(); await time(1080); await time(60)
    const before = saved(); expect(before.ticks).toBeGreaterThan(0); expect(api.start).toHaveBeenCalledTimes(1)
    first.unmount()
    render(<StrictMode><HorseFlightGame gateway={api} playerId="alex" autoStart /></StrictMode>); await flush()
    expect(screen.getByText('Run paused')).toBeTruthy(); expect(api.start).toHaveBeenCalledTimes(1)
    await time(1000); expect(saved().ticks).toBe(before.ticks)
    fireEvent.click(screen.getByRole('button', { name: 'Resume run' })); await time(100)
    expect(saved().ticks).toBeGreaterThan(before.ticks)
  })
  it('requires explicit retry of an uncertain start and preserves its exact key across reload', async () => {
    const api = gateway(); api.start.mockRejectedValueOnce(new Error('lost response'))
    const first = render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    const key = api.start.mock.calls[0]![0]!.idempotencyKey
    expect(saved().state).toBe('starting'); first.unmount()
    render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    expect(api.start).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Retry flight start' })); await flush()
    expect(api.start).toHaveBeenCalledTimes(2)
    expect(api.start.mock.calls[1]![0]).toMatchObject({ idempotencyKey: key, recoveryScope: freeScope })
    expect(saved().run?.runId).toBe(runId)
  })
  it('does not restore another owner or overwrite that owner\'s pending flight', async () => {
    const api = gateway(), first = render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    const before = localStorage.getItem(horseFlightRecoveryKey('alex', 'free')); first.unmount()
    render(<HorseFlightGame gateway={api} playerId="bob" autoStart />); await flush()
    expect(api.start).toHaveBeenCalledTimes(2)
    expect(localStorage.getItem(horseFlightRecoveryKey('alex', 'free'))).toBe(before)
    expect(saved('bob').owner).toBe('bob')
  })
  it('retries an unconfirmed terminal save with the accepted run and original finish key', async () => {
    const value = newHorseFlightRecovery('alex', 'free', freeScope, 'horse-flight-start-0123456789', 'horse-flight-complete-0123456789')
    value.run = { runId, seed: 42, tickMilliseconds: 20, recoveryScope: freeScope }; value.state = 'saving'
    let game = startHorse(42)
    while (game.phase === 'running') game = stepHorse(game, false, false).state
    value.ticks = game.tick; writeHorseFlightRecovery(value)
    const api = gateway(); render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    expect(api.start).not.toHaveBeenCalled(); expect(api.complete).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Try saving again' })); await flush()
    expect(api.complete).toHaveBeenCalledWith(runId, value.ticks, [], expect.objectContaining({ idempotencyKey: value.finishKey, recoveryScope: freeScope }), [])
    expect(saved().state).toBe('saved'); expect(screen.getByRole('button', { name: 'Start fresh run' })).toBeTruthy()
  })
  it('does not auto-charge a new competition window while the previous start is unresolved', async () => {
    const oldScope = JSON.stringify(['paid:weekly', '2026-11-02T00:00:00.000Z', '2026-11-09T00:00:00.000Z'])
    const nextScope = JSON.stringify(['paid:weekly', '2026-11-09T00:00:00.000Z', '2026-11-16T00:00:00.000Z'])
    writeHorseFlightRecovery(newHorseFlightRecovery('alex', 'paid:weekly', oldScope, 'horse-flight-start-0123456789', 'horse-flight-complete-0123456789'))
    const api = gateway(); api.getStatus.mockResolvedValue({ ...status, mode: 'paid', recoveryMode: 'paid:weekly', recoveryScope: nextScope })
    render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    expect(api.start).not.toHaveBeenCalled(); expect(screen.getAllByText(/previous competition window/).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Retry flight start' })).toBeNull()
  })
  it('refuses admission when recovery cannot be persisted before the paid request', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    const api = gateway(); render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
    expect(api.start).not.toHaveBeenCalled(); expect(screen.getAllByText(/could not save flight recovery/).length).toBeGreaterThan(0)
  })
  it('keeps a held gamepad pause from resuming during renders or changed button inputs', async () => {
    const descriptor = Object.getOwnPropertyDescriptor(navigator, 'getGamepads')
    const buttons = Array.from({ length: 16 }, () => ({ pressed: false }))
    Object.defineProperty(navigator, 'getGamepads', { configurable: true, value: () => [{ buttons, axes: [0, 0] }] })
    vi.spyOn(document, 'hasFocus').mockReturnValue(true)
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => window.setTimeout(() => callback(performance.now()), 16))
    vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(id => window.clearTimeout(id))
    try {
      const api = gateway(); render(<HorseFlightGame gateway={api} playerId="alex" autoStart />); await flush()
      await time(1080); await time(60)
      buttons[9]!.pressed = true; await time(16); expect(screen.getByText('Run paused')).toBeTruthy()
      const before = saved().ticks
      buttons[0]!.pressed = true; await time(64)
      expect(screen.getByText('Run paused')).toBeTruthy(); expect(saved().ticks).toBe(before)
      buttons[9]!.pressed = false; await time(16)
      buttons[9]!.pressed = true; await time(16); expect(screen.queryByText('Run paused')).toBeNull()
    } finally {
      if (descriptor) Object.defineProperty(navigator, 'getGamepads', descriptor)
      else delete (navigator as unknown as Record<string, unknown>).getGamepads
    }
  })
  it('does not recover or submit a debug flight as a ranked replay', async () => {
    const api = gateway(), first = render(<HorseFlightGame gateway={api} playerId="alex" debugFreePlay autoStart />); await flush()
    await time(1080); await time(20)
    fireEvent.keyDown(window, { code: 'KeyG', ctrlKey: true, altKey: true }); await flush()
    expect(saved().state).toBe('debug'); first.unmount()
    render(<HorseFlightGame gateway={api} playerId="alex" debugFreePlay />); await flush()
    expect(api.complete).not.toHaveBeenCalled(); expect(screen.getByRole('button', { name: 'Start run' })).toBeTruthy()
    expect(screen.queryByText('Run paused')).toBeNull()
  })
})
