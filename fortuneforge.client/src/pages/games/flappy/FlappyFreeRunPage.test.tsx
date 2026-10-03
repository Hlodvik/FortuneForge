// @vitest-environment jsdom
import { act, StrictMode } from 'react'
import { FlappyReplaySession } from '@fortuneforge/games-flappy'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { ArcadeCompetitionRequestError, type FlappyFreeRun } from '../../../games/arcade/arcadeCompetitionApi'
import { FlappyFreeRunPage } from './FlappyFreeRunPage'
import { recoveryKey } from './flappyRecovery'

const runId = 'flappy_free_' + 'ab'.repeat(32)
const run: FlappyFreeRun = { runId, seed: 17, startedAtUtc: '2026-10-01T00:00:00Z', wasReplay: false }
const account: AccountSummary = { userId: 'pilot-one', playerName: 'Pilot', email: 'pilot@example.test', role: 'Player', createdAtUtc: run.startedAtUtc,
  balances: { slotsCredits: 100, freeGames: 0 }, slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 } }
const gateway = { startFreeFlappyRun: vi.fn(), completeFreeFlappyReplay: vi.fn() }
let root: Root | null, host: HTMLDivElement
function deferred<T>() { let resolve!: (value: T) => void, reject!: (reason: unknown) => void; const promise = new Promise<T>((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
async function flush() { await act(async () => { for (let i = 0; i < 8; i++) await Promise.resolve() }) }
async function mount(owner = account) { root = createRoot(host); await act(async () => root!.render(<StrictMode><FlappyFreeRunPage account={owner} gateway={gateway} /></StrictMode>)); await flush() }
async function time(ms: number) { await act(async () => vi.advanceTimersByTimeAsync(ms)); await flush() }
function button(name: string) { const b = [...host.querySelectorAll<HTMLButtonElement>('button')].find(b => (b.getAttribute('aria-label') ?? b.textContent?.trim()) === name); if (!b) throw new Error(`Missing ${name}: ${host.textContent}`); return b }
async function click(name: string, double = false) { await act(async () => { button(name).focus(); button(name).click(); if (double) button(name).click() }); await flush() }
const phase = () => host.querySelector('main')?.getAttribute('data-phase')
const course = () => host.querySelector<HTMLDivElement>('.flappy-course')!
const tick = () => Number(host.querySelector('.flappy-scene-bird')?.getAttribute('data-tick'))
const saved = () => JSON.parse(sessionStorage.getItem(recoveryKey(account.userId)) ?? 'null')
async function key(code: string, repeat = false, target: HTMLElement = course()) { await act(async () => target.dispatchEvent(new KeyboardEvent('keydown', { code, key: code === 'Space' ? ' ' : code, repeat, bubbles: true, cancelable: true }))); await flush() }
async function pointer(buttonNumber = 0, primary = true, target: HTMLElement = course()) {
  const event = new MouseEvent('pointerdown', { button: buttonNumber, bubbles: true, cancelable: true })
  Object.defineProperty(event, 'isPrimary', { value: primary })
  await act(async () => target.dispatchEvent(event)); await flush()
}
beforeEach(() => {
  vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }))
  HTMLDialogElement.prototype.showModal = function () { this.open = true }
  HTMLDialogElement.prototype.close = function () { this.open = false }
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
  // jsdom ties hasFocus to a focused DOM node; real browser window focus survives replacing the start button.
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  sessionStorage.clear(); localStorage.clear(); host = document.createElement('div'); document.body.append(host); root = null
  gateway.startFreeFlappyRun.mockReset().mockResolvedValue(run)
  gateway.completeFreeFlappyReplay.mockReset().mockResolvedValue({ runId, score: 0, terminal: 'ground-collision', wasReplay: false })
})
afterEach(async () => { if (root) await act(async () => root!.unmount()); host.remove(); vi.restoreAllMocks(); vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('recorded Flappy flight intent and recovery', () => {
  it('opens a compact arcade lobby without a write or historical best claim', async () => {
    await mount(); expect(phase()).toBe('lobby'); expect(host.textContent).toContain('Flappy')
    expect(host.textContent).not.toMatch(/Every flight is securely|Session best/); expect(gateway.startFreeFlappyRun).not.toHaveBeenCalled()
  })
  it('one start intent prepares and begins the flight, prevents duplicate writes and focuses the course', async () => {
    await mount(); await click('Start flight', true); await time(20)
    expect(gateway.startFreeFlappyRun).toHaveBeenCalledTimes(1); expect(phase()).toBe('playing'); expect(document.activeElement).toBe(course())
    expect(saved().cursor).toEqual({ totalTicks: 1, flapTicks: [0] }); expect(tick()).toBe(1)
  })
  it('saves the exact start key before its transport and passes an abort signal', async () => {
    gateway.startFreeFlappyRun.mockImplementation((key: string, signal: AbortSignal) => {
      expect(saved()).toEqual({ kind: 'start', idempotencyKey: key }); expect(signal.aborted).toBe(false); return Promise.resolve(run)
    }); await mount(); await click('Start flight'); expect(gateway.startFreeFlappyRun).toHaveBeenCalledTimes(1)
  })
  it('blocks new starts before transport when storage cannot retain a key', async () => {
    await mount(); vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    await click('Start flight'); expect(gateway.startFreeFlappyRun).not.toHaveBeenCalled(); expect(phase()).toBe('lobby'); expect(host.textContent).toContain('could not save')
  })
  it('restores an uncertain start without automatic POST, then retries the original key', async () => {
    sessionStorage.setItem(recoveryKey(account.userId), JSON.stringify({ kind: 'start', idempotencyKey: 'flappy-original-start-key' }))
    await mount(); expect(phase()).toBe('start-failed'); expect(gateway.startFreeFlappyRun).not.toHaveBeenCalled()
    await click('Retry flight start'); expect(gateway.startFreeFlappyRun.mock.calls[0][0]).toBe('flappy-original-start-key')
  })
  it('retains start uncertainty after network failure and uses the same key on explicit retry', async () => {
    gateway.startFreeFlappyRun.mockRejectedValueOnce(new Error('lost response')); await mount(); await click('Start flight')
    const oldKey = gateway.startFreeFlappyRun.mock.calls[0][0]; expect(phase()).toBe('start-failed'); await click('Retry flight start')
    expect(gateway.startFreeFlappyRun.mock.calls[1][0]).toBe(oldKey)
  })
  it('pauses synchronously and resumes the same frame without an extra flap', async () => {
    await mount(); await click('Start flight'); await time(100); const cursor = saved().cursor
    await click('Pause'); await time(1000); expect(tick()).toBe(5); expect(saved().cursor).toEqual(cursor)
    await click('Resume flight'); await time(20); expect(saved().cursor.flapTicks).toEqual([0]); expect(tick()).toBe(6)
  })
  it.each(['blur', 'visibilitychange'])('suspends on %s without catching up hidden frames', async event => {
    await mount(); await click('Start flight'); await time(100)
    await act(async () => { if (event === 'blur') window.dispatchEvent(new Event(event)); else { Object.defineProperty(document, 'hidden', { configurable: true, value: true }); document.dispatchEvent(new Event(event)) } })
    await time(5000); expect(phase()).toBe('paused'); expect(tick()).toBe(5); expect(gateway.completeFreeFlappyReplay).not.toHaveBeenCalled()
  })
  it.each(['hidden', 'unfocused'])('a start receipt arriving while %s stays paused before the first physics tick', async state => {
    const response = deferred<FlappyFreeRun>(); gateway.startFreeFlappyRun.mockReturnValue(response.promise)
    await mount(); await click('Start flight')
    if (state === 'hidden') Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    else vi.spyOn(document, 'hasFocus').mockReturnValue(false)
    await act(async () => response.resolve(run)); await flush(); await time(1000)
    expect(phase()).toBe('paused'); expect(tick()).toBe(0); expect(saved().cursor).toEqual({ totalTicks: 0, flapTicks: [] })
    expect(gateway.completeFreeFlappyReplay).not.toHaveBeenCalled()
  })
  it('restores the exact running cursor paused after reload without a start or completion write', async () => {
    await mount(); await click('Start flight'); await time(100); await key('Space'); await time(20); const cursor = saved().cursor
    await act(async () => root!.unmount()); root = null; gateway.startFreeFlappyRun.mockClear(); await mount()
    expect(phase()).toBe('paused'); expect(tick()).toBe(cursor.totalTicks); expect(saved().cursor).toEqual(cursor)
    expect(gateway.startFreeFlappyRun).not.toHaveBeenCalled(); expect(gateway.completeFreeFlappyReplay).not.toHaveBeenCalled()
    await click('Resume flight'); await time(20); expect(saved().cursor.flapTicks).toEqual(cursor.flapTicks)
  })
  it('a primary pointer press flaps before release and the later click does not repeat it', async () => {
    await mount(); await click('Start flight'); await time(20); await pointer(); await time(20)
    await act(async () => course().dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 1 }))); await time(20)
    expect(saved().cursor.flapTicks).toEqual([0, 1])
  })
  it('ignores right/secondary pointer presses, unrelated keys and repeated Space', async () => {
    await mount(); await click('Start flight'); await time(20); await pointer(2); await pointer(0, false); await key('Enter'); await key('Space', true); await time(20)
    expect(saved().cursor.flapTicks).toEqual([0])
  })
  it('multiple inputs in the same simulation tick produce one canonical flap', async () => {
    await mount(); await click('Start flight'); await time(20); await key('Space'); await pointer(); await key('Space'); await time(20)
    expect(saved().cursor.flapTicks).toEqual([0, 1])
  })
  it('Escape pauses; a nonrepeating Space deliberately resumes and flaps', async () => {
    await mount(); await click('Start flight'); await time(20); await key('Escape'); expect(phase()).toBe('paused')
    await key('Space'); await time(20); expect(phase()).toBe('playing'); expect(saved().cursor.flapTicks).toEqual([0, 1])
  })
  it('storage loss during play pauses and keeps the latest frame in memory until storage recovers', async () => {
    await mount(); await click('Start flight'); await time(20)
    const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    await time(20); expect(phase()).toBe('paused'); expect(tick()).toBe(2); await click('Resume flight'); expect(phase()).toBe('paused')
    spy.mockRestore(); await click('Resume flight'); await time(20); expect(saved().cursor.totalTicks).toBe(3)
  })
  it('keeps the actual terminal scene while recording, then displays the service score', async () => {
    const response = deferred<{ runId: string; score: number; terminal: string; wasReplay: boolean }>(); gateway.completeFreeFlappyReplay.mockReturnValue(response.promise)
    await mount(); await click('Start flight'); await time(2000)
    expect(phase()).toBe('submitting'); const finalTick = tick(); expect(finalTick).toBeGreaterThan(1)
    expect(host.querySelector('.flappy-scene-bird.is-collided')).not.toBeNull(); expect(host.querySelector('.flappy-result-score')?.textContent).toBe('0')
    await act(async () => response.resolve({ runId, score: 7, terminal: 'ground-collision', wasReplay: false })); await flush()
    expect(phase()).toBe('result'); expect(host.querySelector('.flappy-result-score')?.getAttribute('aria-label')).toBe('Official score 7'); expect(tick()).toBe(finalTick)
    expect(sessionStorage.getItem(recoveryKey(account.userId))).toBeNull()
  })
  it('stores the replay before completion transport and never sends local score metadata', async () => {
    gateway.completeFreeFlappyReplay.mockImplementation((id: string, replay: unknown, signal: AbortSignal) => {
      expect(saved().kind).toBe('submission'); expect(saved().submission.replay).toEqual(replay); expect(id).toBe(runId)
      expect(Object.keys(replay as object)).toEqual(['totalTicks', 'flapTicks']); expect(signal.aborted).toBe(false)
      return Promise.resolve({ runId, score: 0, terminal: 'ground-collision', wasReplay: false })
    }); await mount(); await click('Start flight'); await time(2000); expect(gateway.completeFreeFlappyReplay).toHaveBeenCalledTimes(1)
  })
  it('uncertain recording survives reload without auto-submit and explicit retry pins the same replay', async () => {
    gateway.completeFreeFlappyReplay.mockRejectedValueOnce(new Error('lost response')); await mount(); await click('Start flight'); await time(2000)
    const original = gateway.completeFreeFlappyReplay.mock.calls[0]; const originalStored = saved()
    await act(async () => root!.unmount()); root = null; await mount(); expect(phase()).toBe('submit-failed'); expect(gateway.completeFreeFlappyReplay).toHaveBeenCalledTimes(1)
    expect(saved()).toEqual(originalStored); await click('Retry recording', true)
    expect(gateway.completeFreeFlappyReplay).toHaveBeenCalledTimes(2); expect(gateway.completeFreeFlappyReplay.mock.calls[1].slice(0, 2)).toEqual(original.slice(0, 2))
  })
  it.each([401, 404, 409])('status %s keeps the saved replay available until an explicit choice', async status => {
    gateway.completeFreeFlappyReplay.mockRejectedValue(new ArcadeCompetitionRequestError(status, status === 409 ? 'arcade-flappy-free-run-conflict' : undefined))
    await mount(); await click('Start flight'); await time(2000); expect(phase()).toBe('submit-failed'); expect(saved().kind).toBe('submission'); expect(button('Retry recording').disabled).toBe(false)
  })
  it('shows the confirmed score as the session best and starts the next flight with a new key', async () => {
    gateway.completeFreeFlappyReplay.mockResolvedValue({ runId, score: 6, terminal: 'ground-collision', wasReplay: false })
    await mount(); await click('Start flight'); await time(2000); expect(host.textContent).toContain('Best6')
    await click('Fly again'); expect(phase()).toBe('playing'); await click('Pause')
    expect(gateway.startFreeFlappyRun.mock.calls[1][0]).not.toBe(gateway.startFreeFlappyRun.mock.calls[0][0])
  })
  it('aborts a pending start on unmount and ignores its late response/storage clearing', async () => {
    const response = deferred<FlappyFreeRun>(); gateway.startFreeFlappyRun.mockReturnValue(response.promise)
    await mount(); await click('Start flight'); const signal = gateway.startFreeFlappyRun.mock.calls[0][1] as AbortSignal, original = saved()
    await act(async () => root!.unmount()); root = null; expect(signal.aborted).toBe(true)
    await act(async () => response.resolve(run)); await flush(); expect(saved()).toEqual(original)
  })
  it('account change aborts old completion and cannot put its result in the new account', async () => {
    const response = deferred<{ runId: string; score: number; terminal: string; wasReplay: boolean }>(); gateway.completeFreeFlappyReplay.mockReturnValue(response.promise)
    await mount(); await click('Start flight'); await time(2000); const original = saved(), signal = gateway.completeFreeFlappyReplay.mock.calls[0][2] as AbortSignal
    await act(async () => root!.render(<StrictMode><FlappyFreeRunPage account={{ ...account, userId: 'pilot-two' }} gateway={gateway} /></StrictMode>)); await flush()
    expect(signal.aborted).toBe(true); await act(async () => response.resolve({ runId, score: 44, terminal: 'ground-collision', wasReplay: false })); await flush()
    expect(phase()).toBe('lobby'); expect(host.textContent).not.toContain('44'); expect(saved()).toEqual(original)
  })
  it('rules pause the flight, keep focus bounded and restore the opener after cancellation', async () => {
    await mount(); await click('Start flight'); await time(20); await click('Rules'); const dialog = host.querySelector('dialog')!
    expect(dialog.open).toBe(true); expect(phase()).toBe('paused'); expect(document.activeElement).toBe(button('Close flight details'))
    await key('Space', false, button('Close flight details')); await time(400); expect(tick()).toBe(1)
    await act(async () => dialog.dispatchEvent(new Event('cancel', { cancelable: true }))); await flush()
    expect(host.querySelector('dialog')).toBeNull(); expect(document.activeElement).toBe(button('Rules'))
  })
  it('clearing an unfinished flight requires confirmation and issues no server mutation', async () => {
    await mount(); await click('Start flight'); await time(20); await click('Pause'); await click('New flight')
    expect(saved().kind).toBe('flight'); await click('Close flight details'); expect(phase()).toBe('paused')
    await click('New flight'); await click('Clear flight'); expect(phase()).toBe('lobby'); expect(saved()).toBeNull(); expect(gateway.startFreeFlappyRun).toHaveBeenCalledTimes(1); expect(gateway.completeFreeFlappyReplay).not.toHaveBeenCalled()
  })
  it('malformed saved data locks new play until explicit clear, without automatic network calls', async () => {
    sessionStorage.setItem(recoveryKey(account.userId), '{broken'); await mount(); expect(phase()).toBe('unavailable'); expect(gateway.startFreeFlappyRun).not.toHaveBeenCalled()
    await click('Clear saved flight'); await click('Clear flight'); expect(phase()).toBe('lobby'); expect(saved()).toBeNull()
  })
  it('a replay-session failure gives an explicit unrecorded exit without submitting a fabricated collision', async () => {
    await mount(); await click('Start flight')
    vi.spyOn(FlappyReplaySession.prototype, 'advanceFrame').mockImplementation(function (this: FlappyReplaySession) {
      return { ...this.view, status: 'failed', error: 'Replay limit reached.' }
    })
    await time(20); expect(phase()).toBe('failed'); expect(host.textContent).toContain('This flight was not recorded.')
    expect(gateway.completeFreeFlappyReplay).not.toHaveBeenCalled(); await click('New flight'); expect(phase()).toBe('lobby'); expect(saved()).toBeNull()
  })
})
