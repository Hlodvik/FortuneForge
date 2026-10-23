import { describe, expect, it, vi } from 'vitest'
import { HiLoGatewayError, type HiLoGateway } from './contracts'
import { HiLoPendingOperationStore, createHiLoOperationId, recoverHiLoPendingCommand, submitHiLoCommand, type HiLoPendingCommand } from './hiLoPendingCommand'
import { hiLoSessionFixture as session } from './testFixtures'

function storage() {
  const entries = new Map<string, string>()
  return { entries, getItem: vi.fn((key: string) => entries.get(key) ?? null), setItem: vi.fn((key: string, value: string) => { entries.set(key, value) }), removeItem: vi.fn((key: string) => { entries.delete(key) }) }
}
function gateway() {
  return { getSession: vi.fn<HiLoGateway['getSession']>().mockResolvedValue(session), startRound: vi.fn<HiLoGateway['startRound']>().mockResolvedValue(session), act: vi.fn<HiLoGateway['act']>().mockResolvedValue(session) }
}
const command: HiLoPendingCommand = { kind: 'start', operationId: 'original-start-operation', wagerCents: 100 }

describe('Hi-Lo pending-operation recovery', () => {
  it('persists the exact action before issuing a request and clears only after confirmation', async () => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    api.startRound.mockImplementation(async () => {
      expect(store.read()).toEqual(command)
      return session
    })
    await expect(submitHiLoCommand(api, store, command)).resolves.toEqual(session)
    expect(store.read()).toBeNull()
    expect(api.startRound).toHaveBeenCalledWith(100, { operationId: command.operationId, signal: undefined })
  })

  it.each([
    new TypeError('Network reset'),
    new DOMException('Aborted', 'AbortError'),
    new HiLoGatewayError('Retry', 'retry_required', 503),
    new HiLoGatewayError('Incomplete success', 'hi-lo-invalid-response', 200),
  ])('keeps an uncertain mutation through refresh and repeats the original key: %s', async error => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    api.startRound.mockRejectedValueOnce(error)
    await expect(submitHiLoCommand(api, store, command)).rejects.toBe(error)
    const refreshedStore = new HiLoPendingOperationStore('alice:practice', saved)
    expect(refreshedStore.read()).toEqual(command)
    await recoverHiLoPendingCommand(api, refreshedStore)
    expect(api.startRound).toHaveBeenNthCalledWith(2, 100, { operationId: command.operationId, signal: undefined })
    expect(api.getSession).toHaveBeenCalledOnce()
    expect(refreshedStore.read()).toBeNull()
  })

  it('replays exact round/revision/choice and then reads the newest snapshot', async () => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    const action: HiLoPendingCommand = { kind: 'action', operationId: 'original-cashout-operation', roundId: session.round!.roundId, expectedRevision: 8, choice: 'cashout' }
    store.write(action)
    const latest = { ...session, walletBalanceCents: 1000200 }
    api.getSession.mockResolvedValue(latest)
    await expect(recoverHiLoPendingCommand(api, store)).resolves.toEqual(latest)
    expect(api.act).toHaveBeenCalledWith(action.roundId, 'cashout', { operationId: action.operationId, expectedRevision: 8, signal: undefined })
    expect(api.getSession.mock.invocationCallOrder[0]).toBeGreaterThan(api.act.mock.invocationCallOrder[0])
  })

  it.each(['insufficient_funds', 'stale_revision', 'round_finished', 'operation_conflict'])('clears a definitive %s rejection and refreshes the table', async code => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    store.write(command)
    api.startRound.mockRejectedValue(new HiLoGatewayError('Authoritative rejection', code, 409))
    await expect(recoverHiLoPendingCommand(api, store)).resolves.toEqual(session)
    expect(store.read()).toBeNull()
    expect(api.getSession).toHaveBeenCalledOnce()
  })

  it.each(['start', 'higher'] as const)('clears a proven maintenance rejection for %s and restores the paused session', async choice => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    const pending: HiLoPendingCommand = choice === 'start' ? command : {
      kind: 'action', operationId: 'original-higher-operation', roundId: session.round!.roundId, expectedRevision: 0, choice,
    }
    const rejected = new HiLoGatewayError('Hi-Lo is paused. Existing rounds can still be cashed out.', 'hi_lo_disabled', 503)
    api.startRound.mockRejectedValue(rejected)
    api.act.mockRejectedValue(rejected)
    await expect(submitHiLoCommand(api, store, pending)).rejects.toBe(rejected)
    expect(store.read()).toBeNull()
    // An old saved request can also encounter maintenance during refresh.
    store.write(pending)
    const paused = { ...session, enabled: false }
    api.getSession.mockResolvedValue(paused)
    await expect(recoverHiLoPendingCommand(api, store)).resolves.toEqual(paused)
    expect(store.read()).toBeNull()
    expect(api.getSession).toHaveBeenCalledOnce()
  })

  it('isolates pending commands by player and practice/account mode', () => {
    const saved = storage(), practice = new HiLoPendingOperationStore('alice:practice', saved)
    practice.write(command)
    expect(new HiLoPendingOperationStore('alice:account', saved).read()).toBeNull()
    expect(new HiLoPendingOperationStore('bob:practice', saved).read()).toBeNull()
    expect([...saved.entries.values()][0]).not.toContain('walletBalance')
    expect([...saved.entries.values()][0]).not.toContain('currentCard')
  })

  it('refuses to replace an unresolved action and ignores a stale completion clearing another key', () => {
    const store = new HiLoPendingOperationStore('alice:practice', storage())
    store.write(command)
    expect(() => store.write({ ...command, operationId: 'second-operation' })).toThrow('Restore the pending')
    store.clear('another-operation')
    expect(store.read()).toEqual(command)
  })

  it('does not wager when browser storage cannot save recovery information', async () => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved), api = gateway()
    saved.setItem.mockImplementation(() => { throw new DOMException('Storage full') })
    await expect(submitHiLoCommand(api, store, command)).rejects.toMatchObject({ code: 'hi-lo-storage-unavailable' })
    expect(api.startRound).not.toHaveBeenCalled()
  })

  it('blocks malformed saved commands instead of discarding an unresolved request', () => {
    const saved = storage(), store = new HiLoPendingOperationStore('alice:practice', saved)
    store.write(command)
    saved.entries.set([...saved.entries.keys()][0], '{broken')
    expect(() => store.read()).toThrow('saved Hi-Lo action could not be read')
  })

  it('creates secure unique operation IDs with no fallback outcome randomness', () => {
    const first = createHiLoOperationId(), second = createHiLoOperationId()
    expect(first).toMatch(/^[a-f0-9-]{36}$/)
    expect(first).not.toBe(second)
  })
})
