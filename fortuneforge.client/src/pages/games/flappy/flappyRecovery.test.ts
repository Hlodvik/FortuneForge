// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { FlappyReplaySession } from '@fortuneforge/games-flappy'
import { clearFlappyRecovery, readFlappyRecovery, recoveryKey, restoreFlight, writeFlappyRecovery, type FlappyRecovery } from './flappyRecovery'
const userId = 'pilot-one', runId = 'flappy_free_' + 'ab'.repeat(32)
const run = { runId, seed: 17, startedAtUtc: '2026-10-01T00:00:00Z', wasReplay: false }
const store = (value: unknown) => sessionStorage.setItem(recoveryKey(userId), JSON.stringify(value))
const flight: FlappyRecovery = { kind: 'flight', run, cursor: { totalTicks: 10, flapTicks: [0] } }
function terminal() {
  const session = new FlappyReplaySession(runId, 17); session.advanceFrame(true)
  while (session.view.status === 'running') session.advanceFrame()
  return session.takeCompletion()!
}
beforeEach(() => { vi.restoreAllMocks(); sessionStorage.clear() })
describe('account-scoped Flappy recovery', () => {
  it('restores exact deterministic prefix state without advancing an extra tick', () => {
    const expected = new FlappyReplaySession(runId, 17); for (let tick = 0; tick < 10; tick++) expected.advanceFrame(tick === 0)
    expect(restoreFlight(run, flight.cursor).view).toEqual(expected.view)
    store(flight); expect(readFlappyRecovery(userId)).toEqual({ recovery: flight, error: null })
  })
  it('empty saved course is valid while a completion requires a terminal frame', () => {
    store({ ...flight, cursor: { totalTicks: 0, flapTicks: [] } }); expect(readFlappyRecovery(userId).error).toBeNull()
    store({ kind: 'submission', submission: { run, runId, replay: { totalTicks: 10, flapTicks: [0] }, display: { score: 0, phase: 'ground-collision' } } })
    expect(readFlappyRecovery(userId).error).toContain('could not be opened')
  })
  it('the terminal snapshot becomes an exact pending submission rather than a new flight', () => {
    const ended = terminal(); store({ kind: 'flight', run, cursor: ended.replay })
    expect(readFlappyRecovery(userId).recovery).toEqual({ kind: 'submission', submission: { run, runId, ...ended } })
  })
  it('rejects a saved claimed score or collision that differs from its actual replay', () => {
    const ended = terminal(); store({ kind: 'submission', submission: { run, runId, ...ended, display: { ...ended.display, score: 9 } } })
    expect(readFlappyRecovery(userId).recovery).toBeNull()
    store({ kind: 'submission', submission: { run, runId, ...ended, display: { ...ended.display, phase: 'ceiling-collision' } } })
    expect(readFlappyRecovery(userId).recovery).toBeNull()
  })
  it('does not read another account’s course or write to another account key', () => {
    expect(writeFlappyRecovery(userId, flight)).toBeNull(); expect(readFlappyRecovery('pilot-two').recovery).toBeNull()
    expect(sessionStorage.getItem(recoveryKey('pilot-two'))).toBeNull()
  })
  it('reads the prior start receipt without creating new intent', () => {
    sessionStorage.setItem(`fortuneforge:flappy:start:${userId}`, JSON.stringify({ idempotencyKey: 'flappy-legacy-start-key' }))
    expect(readFlappyRecovery(userId).recovery).toEqual({ kind: 'start', idempotencyKey: 'flappy-legacy-start-key' })
  })
  it('reads prior exact pending replay without inventing an issued seed/course', () => {
    const ended = terminal(); sessionStorage.setItem(`fortuneforge:flappy:pending:${userId}`, JSON.stringify({ runId, ...ended }))
    expect(readFlappyRecovery(userId).recovery).toEqual({ kind: 'submission', submission: { run: null, runId, ...ended } })
  })
  it('prefers current snapshot to stale legacy values and clear removes all three own keys only', () => {
    store(flight); sessionStorage.setItem(`fortuneforge:flappy:start:${userId}`, 'bad'); sessionStorage.setItem(`fortuneforge:flappy:pending:${userId}`, 'bad')
    sessionStorage.setItem(recoveryKey('pilot-two'), 'other'); expect(readFlappyRecovery(userId).recovery).toEqual(flight)
    expect(clearFlappyRecovery(userId)).toBeNull(); expect(readFlappyRecovery(userId).recovery).toBeNull(); expect(sessionStorage.getItem(recoveryKey('pilot-two'))).toBe('other')
  })
  it.each([null, [], {}, { kind: 'start', idempotencyKey: 'short' }, { kind: 'unknown' }, { ...flight, run: { ...run, seed: 0 } },
    { ...flight, run: { ...run, runId: 'other' } }, { ...flight, run: { ...run, startedAtUtc: 'invalid' } }])('locks malformed saved value %j without deleting it', value => {
    store(value); const raw = sessionStorage.getItem(recoveryKey(userId)); expect(readFlappyRecovery(userId).error).not.toBeNull()
    expect(sessionStorage.getItem(recoveryKey(userId))).toBe(raw)
  })
  it.each([{ totalTicks: -1, flapTicks: [] }, { totalTicks: 9001, flapTicks: [] }, { totalTicks: 1.5, flapTicks: [] },
    { totalTicks: 0, flapTicks: [0] }, { totalTicks: 10, flapTicks: [0, 0] }, { totalTicks: 10, flapTicks: [2, 1] },
    { totalTicks: 10, flapTicks: [10] }, { totalTicks: 10, flapTicks: [-1] }, { totalTicks: 10, flapTicks: [1.2] },
    { totalTicks: 3000, flapTicks: Array.from({ length: 1501 }, (_, i) => i) }, { totalTicks: 38, flapTicks: [] }])('rejects noncanonical or post-collision prefix %j', cursor => {
    store({ ...flight, cursor }); expect(readFlappyRecovery(userId).recovery).toBeNull()
  })
  it('reports storage failures without representing the flight as durably saved', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    expect(writeFlappyRecovery(userId, flight)).toContain('could not save'); expect(sessionStorage.getItem(recoveryKey(userId))).toBeNull()
  })
  it('reports unavailable reads/clears without clearing unknown recovery state', () => {
    store(flight); vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('disabled') })
    expect(readFlappyRecovery(userId).error).not.toBeNull(); vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('disabled') })
    expect(clearFlappyRecovery(userId)).toContain('could not save')
  })
})
