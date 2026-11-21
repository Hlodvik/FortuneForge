import { describe, expect, it, vi } from 'vitest'
import { createGameCommandRecovery, type GameCommandRecovery } from '@fortuneforge/client-presentation/game-command-recovery'
import { HttpHeartsGateway } from './httpHeartsGateway'

const match = { matchId: 'saved-hand', targetScore: 100, roundNumber: 1, phase: 'playing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: true, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: [], hand: [], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '' }
const idle = { kind: 'idle', version: 8, balanceCredits: 100, entryOptions: [1, 2, 5], queue: null, table: null }
const response = (value: unknown, status = 200) => status === 204 ? new Response(null, { status }) : new Response(JSON.stringify(value), { status, headers: { 'content-type': 'application/json' } })
function journal(accountId = 'alice', storage = memoryStorage(), key = 'original-command') {
  return createGameCommandRecovery({ accountId, gameId: 'hearts', routePrefix: '/hearts', storage, createIdempotencyKey: () => key })
}
function memoryStorage() {
  const values = new Map<string, string>()
  return { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value) }, removeItem: (key: string) => { values.delete(key) } }
}
function headers(init?: RequestInit) { return new Headers(init?.headers) }

describe('Hearts durable command recovery', () => {
  const freeCommands: readonly [string, (api: HttpHeartsGateway) => Promise<unknown>, unknown, string][] = [
    ['start', api => api.startMatch({ targetScore: 100, difficulty: 'sharp', seed: 42 }), match, '/matches'],
    ['trial', api => api.startTrial(), { ...match, isTrial: true }, '/trial'],
    ['pass', api => api.pass('saved-hand', ['2|clubs', '3|clubs', '4|clubs']), match, '/matches/saved-hand/pass'],
    ['card', api => api.playCard('saved-hand', '2|clubs'), match, '/matches/saved-hand/card'],
    ['advance', api => api.advance('saved-hand'), match, '/matches/saved-hand/advance'],
    ['next round', api => api.nextRound('saved-hand', 42), match, '/matches/saved-hand/next-round'],
    ['leave', api => api.leaveMatch('saved-hand'), undefined, '/matches/saved-hand/leave'],
    ['cancel trial', api => api.cancelTrial('saved-hand'), { shouldAskPlayedBefore: true }, '/trial/saved-hand/cancel'],
    ['confirm experience', api => api.onboarding.confirmExperience(), { shouldAskPlayedBefore: false }, '/onboarding'],
  ]
  it.each(freeCommands)('replays the exact %s path, body, and key after a gateway restart', async (_name, command, result, path) => {
    const storage = memoryStorage()
    const saved = journal('alice', storage)
    const fetcher = vi.fn<typeof fetch>().mockRejectedValueOnce(new Error('Response lost.'))
      .mockResolvedValueOnce(response(result, result === undefined ? 204 : 200)).mockResolvedValue(response(result))
    await expect(command(new HttpHeartsGateway('/hearts', fetcher, saved, 'alice'))).rejects.toThrow('Response lost.')
    const pending = saved.read().pending!
    expect(pending.path).toBe('/hearts' + path)
    expect(pending.idempotencyKey).toBe('original-command')
    await new HttpHeartsGateway('/hearts', fetcher, journal('alice', storage, 'must-not-replace-key'), 'alice').recoverSession()
    const [target, init] = fetcher.mock.calls[1]
    expect(target).toBe(pending.path)
    expect(init?.body).toBe(pending.body)
    expect(init?.method).toBe('POST')
    expect(headers(init).get('Idempotency-Key')).toBe(pending.idempotencyKey)
    expect(headers(init).get('X-FortuneForge-Expected-Account')).toBe('alice')
    expect(saved.read().pending).toBeNull()
    expect(saved.read().activeBoardId).toBe(path === '/onboarding' || result === undefined || path.endsWith('/cancel') ? null : 'saved-hand')
  })

  const paidOptions = { idempotencyKey: 'transient-ui-key', expectedVersion: 7 }
  const paidCommands: readonly [string, (api: HttpHeartsGateway) => Promise<unknown>][] = [
    ['join', api => api.paid.join(5, paidOptions)],
    ['house consent', api => api.paid.playHouse!('paid-hand', paidOptions)],
    ['pass', api => api.paid.pass('paid-hand', ['2|clubs', '3|clubs', '4|clubs'], paidOptions)],
    ['card', api => api.paid.play('paid-hand', '2|clubs', paidOptions)],
    ['advance', api => api.paid.advance('paid-hand', paidOptions)],
    ['next round', api => api.paid.nextRound('paid-hand', paidOptions)],
    ['leave or cancel queue', api => api.paid.leave('paid-hand', paidOptions)],
  ]
  it.each(paidCommands)('recovers paid %s before reading a fresh session', async (_name, command) => {
    const saved = journal()
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => response(idle)).mockRejectedValueOnce(new Error('Response lost.'))
    await expect(command(new HttpHeartsGateway('/hearts', fetcher, saved, 'alice'))).rejects.toThrow()
    const pending = saved.read().pending!
    expect(JSON.parse(pending.body).expectedVersion).toBe(7)
    expect(await new HttpHeartsGateway('/hearts', fetcher, saved, 'alice').paid.getSession()).toEqual(idle)
    expect(fetcher.mock.calls[1][0]).toBe(pending.path)
    expect(fetcher.mock.calls[1][1]?.body).toBe(pending.body)
    expect(headers(fetcher.mock.calls[1][1]).get('Idempotency-Key')).toBe(pending.idempotencyKey)
    expect(fetcher.mock.calls[2][0]).toBe('/hearts/paid/session')
    expect(saved.read().pending).toBeNull()
  })

  it('keeps the explicit paid caller key when a completed command is submitted again', async () => {
    const saved = journal('alice', memoryStorage(), 'unused-generated-key')
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => response(idle))
    const api = new HttpHeartsGateway('/hearts', fetcher, saved, 'alice')
    const options = { idempotencyKey: 'paid-caller-key', expectedVersion: 0 }
    await api.paid.join(5, options)
    expect(saved.read().pending).toBeNull()
    await api.paid.join(5, options)
    expect(fetcher.mock.calls).toHaveLength(2)
    expect(fetcher.mock.calls.map(call => headers(call[1]).get('Idempotency-Key'))).toEqual(['paid-caller-key', 'paid-caller-key'])
    expect(fetcher.mock.calls[1][1]?.body).toBe(fetcher.mock.calls[0][1]?.body)
  })

  it('uses the original pending paid caller key when the UI supplies a different retry key', async () => {
    const storage = memoryStorage()
    const saved = journal('alice', storage, 'unused-generated-key')
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => response(idle)).mockRejectedValueOnce(new Error('Reply lost.'))
    await expect(new HttpHeartsGateway('/hearts', fetcher, saved, 'alice').paid.join(5,
      { idempotencyKey: 'paid-original-key', expectedVersion: 0 })).rejects.toThrow('Reply lost.')
    const original = saved.read().pending!
    expect(original.idempotencyKey).toBe('paid-original-key')
    await new HttpHeartsGateway('/hearts', fetcher, journal('alice', storage, 'another-generated-key'), 'alice').paid.join(5,
      { idempotencyKey: 'new-ui-retry-key', expectedVersion: 0 })
    expect(headers(fetcher.mock.calls[1][1]).get('Idempotency-Key')).toBe('paid-original-key')
    expect(fetcher.mock.calls[1][0]).toBe(original.path)
    expect(fetcher.mock.calls[1][1]?.body).toBe(original.body)
    expect(saved.read().pending).toBeNull()
  })

  it('recovers a pending start before account onboarding and retains the accepted hand across restarts', async () => {
    const storage = memoryStorage()
    const saved = journal('alice', storage)
    saved.prepare('/hearts/matches', '{"targetScore":100}')
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(response(match)).mockResolvedValueOnce(response({ shouldAskPlayedBefore: false })).mockResolvedValue(response(match))
    const api = new HttpHeartsGateway('/hearts', fetcher, saved, 'alice')
    await api.onboarding.getState()
    expect(fetcher.mock.calls.map(call => call[0])).toEqual(['/hearts/matches', '/hearts/onboarding'])
    const recovered = await new HttpHeartsGateway('/hearts', fetcher, journal('alice', storage), 'alice').recoverSession()
    expect(recovered).toEqual({ kind: 'practice', match })
    expect(fetcher.mock.calls[2][0]).toBe('/hearts/matches/saved-hand')
    expect(fetcher.mock.calls[2][1]?.method).toBeUndefined()
  })

  it.each([401, 403, 408, 425, 429, 500, 503])('retains uncertain or authentication-expired commands after HTTP %s', async status => {
    const saved = journal()
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response({ code: 'unavailable', message: 'Retry later.' }, status))
    await expect(new HttpHeartsGateway('/hearts', fetcher, saved, 'alice').startTrial()).rejects.toMatchObject({ status })
    expect(saved.read().pending?.path).toBe('/hearts/trial')
  })

  it('isolates a lost authenticated command from the next account and restores its original identity after sign-in', async () => {
    const storage = memoryStorage()
    const alice = journal('alice', storage)
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => response(match)).mockResolvedValueOnce(response({ code: 'session-expired', message: 'Sign in again.' }, 401))
    await expect(new HttpHeartsGateway('/hearts', fetcher, alice, 'alice').playCard('saved-hand', '2|clubs')).rejects.toMatchObject({ status: 401 })
    const original = alice.read().pending!
    const bob = new HttpHeartsGateway('/hearts', fetcher, journal('bob', storage), 'bob')
    expect(await bob.recoverSession()).toBeNull()
    expect(fetcher).toHaveBeenCalledOnce()
    await new HttpHeartsGateway('/hearts', fetcher, journal('alice', storage, 'new-key'), 'alice').recoverSession()
    expect(headers(fetcher.mock.calls[1][1]).get('Idempotency-Key')).toBe(original.idempotencyKey)
    expect(headers(fetcher.mock.calls[1][1]).get('X-FortuneForge-Expected-Account')).toBe('alice')
  })

  it('blocks a different command while recovery is pending and keeps malformed successful replies retryable', async () => {
    const saved = journal()
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response({ ...match, matchId: 'wrong-hand' }))
    const api = new HttpHeartsGateway('/hearts', fetcher, saved)
    await expect(api.playCard('saved-hand', '2|clubs')).rejects.toMatchObject({ code: 'hearts-invalid-response' })
    await expect(api.nextRound('saved-hand')).rejects.toThrow('Finish recovering')
    expect(fetcher).toHaveBeenCalledOnce()
    expect(saved.read().pending?.body).toBe('{"card":"2|clubs"}')
  })

  it('clears a definitive rejected command and an authoritatively missing saved hand', async () => {
    const saved = journal()
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response({ code: 'hearts-conflict', message: 'The hand changed.' }, 409))
    await expect(new HttpHeartsGateway('/hearts', fetcher, saved).startTrial()).rejects.toMatchObject({ status: 409 })
    expect(saved.read().pending).toBeNull()
    saved.setActiveBoardId('removed-hand')
    fetcher.mockResolvedValue(response({ code: 'not-found', message: 'Closed.' }, 404))
    expect(await new HttpHeartsGateway('/hearts', fetcher, saved).recoverSession()).toEqual({ kind: 'closed' })
    expect(saved.read().activeBoardId).toBeNull()
  })

  it('does not send unsupported saved command paths', async () => {
    const saved: GameCommandRecovery = journal()
    saved.prepare('/hearts/unrecognized', '{}')
    const fetcher = vi.fn<typeof fetch>()
    await expect(new HttpHeartsGateway('/hearts', fetcher, saved).recoverSession()).rejects.toMatchObject({ code: 'hearts-invalid-command' })
    expect(fetcher).not.toHaveBeenCalled()
    expect(saved.read().pending).not.toBeNull()
  })
})
