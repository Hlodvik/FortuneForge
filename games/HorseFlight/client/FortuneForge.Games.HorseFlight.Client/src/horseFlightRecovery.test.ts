// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { startHorse, stepHorse } from './horseFlightSimulation'
import { horseFlightReceiptMatches, horseFlightRecoveryKey, newHorseFlightRecovery, readHorseFlightRecovery,
  reconstructHorseFlight, validRecoveryScope, writeHorseFlightRecovery, type HorseFlightRecovery } from './horseFlightRecovery'

const freeScope = JSON.stringify(['free'])
const weeklyScope = JSON.stringify(['paid:weekly', '2026-11-02T00:00:00.000Z', '2026-11-09T00:00:00.000Z'])
function record(): HorseFlightRecovery {
  const value = newHorseFlightRecovery('alex', 'free', freeScope, 'horse-flight-start-0123456789', 'horse-flight-complete-0123456789')
  value.run = { runId: 'horse_flight_free_' + 'a'.repeat(64), seed: 42, tickMilliseconds: 20, recoveryScope: freeScope }
  value.state = 'active'
  return value
}
function terminal(value: HorseFlightRecovery) {
  let state = startHorse(value.run!.seed, value.rulesVersion)
  while (state.phase === 'running' && state.tick < 30_000) state = stepHorse(state, false, false).state
  if (state.phase === 'running') throw new Error('Fixture did not end')
  value.ticks = state.tick
  return state
}
beforeEach(() => localStorage.clear())

describe('owner and competition-scoped Horse Flight recovery', () => {
  it('reconstructs accepted inputs through the current simulation rather than saved positions or score', () => {
    const value = record(); value.ticks = 10
    expect(writeHorseFlightRecovery(value)).toBe(true)
    const restored = readHorseFlightRecovery('alex', 'free', freeScope)
    let expected = startHorse(42)
    for (let tick = 0; tick < 10; tick++) expected = stepHorse(expected, false, false).state
    expect(restored.error).toBeNull(); expect(restored.game).toEqual(expected)
    expect(readHorseFlightRecovery('bob', 'free', freeScope).record).toBeNull()
  })
  it('keeps the same admission and finish keys before any accepted run exists', () => {
    const value = newHorseFlightRecovery('alex', 'free', freeScope, 'horse-flight-start-0123456789', 'horse-flight-complete-0123456789')
    writeHorseFlightRecovery(value)
    expect(readHorseFlightRecovery('alex', 'free', freeScope)).toEqual({ record: value, game: null, error: null })
  })
  it('separates daily and weekly records and holds an unresolved old window', () => {
    const value = newHorseFlightRecovery('alex', 'paid:weekly', weeklyScope, 'horse-flight-start-0123456789', 'horse-flight-complete-0123456789')
    writeHorseFlightRecovery(value)
    const next = JSON.stringify(['paid:weekly', '2026-11-09T00:00:00.000Z', '2026-11-16T00:00:00.000Z'])
    expect(readHorseFlightRecovery('alex', 'paid:weekly', next).error).toContain('previous competition window')
    expect(readHorseFlightRecovery('alex', 'paid:daily', JSON.stringify(['paid:daily', '2026-11-02T00:00:00.000Z', '2026-11-03T00:00:00.000Z'])).record).toBeNull()
  })
  it.each([
    { ticks: 30_001 }, { ticks: -1 }, { ticks: 4, jumps: [1, 1] }, { ticks: 4, jumps: [2, 1] },
    { ticks: 4, jumps: [4] }, { ticks: 4, jumps: [1], rights: [1] }, { rulesVersion: 999 },
    { owner: 'other' }, { state: 'saved', result: null }, { run: null, ticks: 1 },
  ])('holds malformed or noncanonical recovery %j without trusting it', patch => {
    const value = { ...record(), ...patch }
    localStorage.setItem(horseFlightRecoveryKey('alex', 'free'), JSON.stringify(value))
    const restored = readHorseFlightRecovery('alex', 'free', freeScope)
    expect(restored.error).toContain('could not be verified'); expect(restored.game).toBeNull()
  })
  it('rejects an accepted input that the current simulation did not consume', () => {
    const value = record(); value.ticks = 3; value.jumps = [0, 1, 2]
    expect(() => reconstructHorseFlight(value)).toThrow()
  })
  it('requires the exact terminal receipt before marking a run saved', () => {
    const value = record(), game = terminal(value)
    const result = { runId: value.run!.runId, totalTicks: value.ticks, jumpTicks: [], rightClickTicks: [], score: game.score,
      phase: game.phase as 'fell' | 'obstacle-collision', balance: 100 }
    expect(horseFlightReceiptMatches(value, game, result)).toBe(true)
    expect(horseFlightReceiptMatches(value, game, { ...result, runId: 'other' })).toBe(false)
    expect(horseFlightReceiptMatches(value, game, { ...result, score: game.score + 1 })).toBe(false)
    expect(horseFlightReceiptMatches(value, game, { ...result, balance: NaN })).toBe(false)
    expect(horseFlightReceiptMatches(value, game, { ...result, jumpTicks: [0] })).toBe(false)
    value.state = 'saved'; value.result = result; writeHorseFlightRecovery(value)
    expect(readHorseFlightRecovery('alex', 'free', freeScope).record?.result).toEqual(result)
  })
  it('keeps debug runs out of replay reconstruction', () => {
    const value = record(); value.state = 'debug'; value.ticks = 50
    writeHorseFlightRecovery(value)
    expect(readHorseFlightRecovery('alex', 'free', freeScope)).toMatchObject({ error: null, game: null, record: { state: 'debug' } })
  })
  it('uses explicit, canonical mode/window metadata', () => {
    expect(validRecoveryScope(freeScope, 'free')).toBe(true)
    expect(validRecoveryScope(weeklyScope, 'paid:weekly')).toBe(true)
    expect(validRecoveryScope(weeklyScope, 'paid:daily')).toBe(false)
    expect(validRecoveryScope(JSON.stringify(['paid:weekly', 'invalid', 'date']), 'paid:weekly')).toBe(false)
  })
})
