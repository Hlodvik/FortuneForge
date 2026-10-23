import { describe, expect, it, vi } from 'vitest'
import { HttpHeartsGateway, isMatch } from './httpHeartsGateway'

const idle = { kind: 'idle', version: 0, balanceCredits: 100, entryOptions: [1, 2, 5], queue: null, table: null }
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'content-type': 'application/json' } })
const match = { matchId: 'trial-one', targetScore: 100, roundNumber: 1, phase: 'passing', passDirection: 'left', turn: 'north', humanSeat: 'north', yourTurn: false, opponentsThinking: false, heartsBroken: false, submittedPasses: [], players: [], hand: [], legalCards: [], currentTrick: { leader: 'north', plays: [] }, recentTrick: null, completedTricks: [], score: { north: 0, east: 0, south: 0, west: 0 }, roundScore: { north: 0, east: 0, south: 0, west: 0 }, difficulty: 'standard', winner: null, message: '' }

describe('Hearts paid gateway', () => {
  it('reads the existing session through the supplied authenticated fetch without starting a game', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response(idle))
    const api = new HttpHeartsGateway('/api/games/hearts', request)
    expect(await api.paid.getSession()).toEqual(idle)
    expect(request).toHaveBeenCalledOnce()
    expect(request).toHaveBeenCalledWith('/api/games/hearts/paid/session', expect.objectContaining({ signal: undefined }))
  })
  it('submits a selected entry without exposing a seed, score limit or difficulty setting', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response(idle))
    await new HttpHeartsGateway('/api/games/hearts/', request).paid.join(5, { idempotencyKey: 'hearts-test', expectedVersion: 0 })
    const [url, init] = request.mock.calls[0]
    expect(url).toBe('/api/games/hearts/paid/join')
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({ 'content-type': 'application/json', 'Idempotency-Key': 'hearts-test' })
    expect(JSON.parse(String(init?.body))).toEqual({ entryCredits: 5, expectedVersion: 0 })
  })
  it('binds a legal play to the displayed table version and exact request key', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response(idle))
    await new HttpHeartsGateway('/hearts', request).paid.play('match/a', '2|clubs', { idempotencyKey: 'hearts-card-1', expectedVersion: 12 })
    expect(request.mock.calls[0][0]).toBe('/hearts/paid/matches/match%2Fa/card')
    expect(JSON.parse(String(request.mock.calls[0][1]?.body))).toEqual({ card: '2|clubs', expectedVersion: 12 })
  })
  it('sends house consent explicitly with the queue version and no new entry amount', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response(idle))
    await new HttpHeartsGateway('/hearts', request).paid.playHouse!('queue/one', { expectedVersion: 4, idempotencyKey: 'hearts-house-choice' })
    expect(request.mock.calls[0][0]).toBe('/hearts/paid/matches/queue%2Fone/play-house')
    expect(JSON.parse(String(request.mock.calls[0][1]?.body))).toEqual({ expectedVersion: 4 })
    expect(request.mock.calls[0][1]?.headers).toEqual({ 'content-type': 'application/json', 'Idempotency-Key': 'hearts-house-choice' })
  })
  it('rejects a malformed house availability flag rather than treating a truthy value as consent availability', async () => {
    const queue = { matchId: 'one', entryCredits: 1, potCredits: 1, joinedAtUtc: '2026-10-22T12:00:00Z', message: '', players: [{ seat: 'north', displayName: 'You', isCurrentPlayer: true }], canPlayHouse: 'yes' }
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ ...idle, kind: 'queue', queue }))
    await expect(new HttpHeartsGateway('/hearts', request).paid.getSession()).rejects.toMatchObject({ code: 'hearts-invalid-response' })
  })
  it('rejects a malformed queue before it can present an invented pot or playable hand', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ ...idle, kind: 'queue', queue: { matchId: 'queue-one', players: [] } }))
    await expect(new HttpHeartsGateway('/hearts', request).paid.getSession()).rejects.toMatchObject({ code: 'hearts-invalid-response' })
  })
  it('closes a free table through authenticated fetch and accepts an empty success response', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    await expect(new HttpHeartsGateway('/hearts', request).leaveMatch('free/one')).resolves.toBeUndefined()
    expect(request).toHaveBeenCalledWith('/hearts/matches/free%2Fone/leave', { method: 'POST', signal: undefined })
  })
  it('retains a failed free-table close as an error instead of silently leaving locally', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ code: 'hearts-match-not-found', message: 'This is not your table.' }, 404))
    await expect(new HttpHeartsGateway('/hearts', request).leaveMatch('other-table')).rejects.toMatchObject({ status: 404, code: 'hearts-match-not-found' })
  })
  it('preserves insufficient-funds and stale-version error codes from the server', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValueOnce(response({ code: 'hearts-insufficient-funds', message: 'Not enough balance.' }, 402)).mockResolvedValueOnce(response({ code: 'hearts-conflict', message: 'The table changed.' }, 409))
    const api = new HttpHeartsGateway('/hearts', request)
    await expect(api.paid.join(5, { idempotencyKey: 'join' })).rejects.toMatchObject({ status: 402, code: 'hearts-insufficient-funds' })
    await expect(api.paid.play('match', '2|clubs', { idempotencyKey: 'play', expectedVersion: 1 })).rejects.toMatchObject({ status: 409, code: 'hearts-conflict' })
  })
})

describe('Hearts onboarding and dedicated trial gateway', () => {
  it('reads account onboarding through authenticated fetch without a client account identifier', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ shouldAskPlayedBefore: true }))
    const state = await new HttpHeartsGateway('/hearts', request).onboarding.getState()
    expect(state).toEqual({ shouldAskPlayedBefore: true })
    expect(request).toHaveBeenCalledWith('/hearts/onboarding', { signal: undefined })
  })
  it('persists only a Yes answer and does not invent a false or completed-trial write', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ shouldAskPlayedBefore: false }))
    await new HttpHeartsGateway('/hearts', request).onboarding.confirmExperience()
    expect(request.mock.calls[0][0]).toBe('/hearts/onboarding')
    expect(request.mock.calls[0][1]?.method).toBe('POST')
    expect(JSON.parse(String(request.mock.calls[0][1]?.body))).toEqual({ hasPlayedBefore: true })
  })
  it('opens the separate trial endpoint without paid, seed, or free-match setup payloads', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ ...match, isTrial: true }))
    const trial = await new HttpHeartsGateway('/hearts', request).startTrial()
    expect(trial.isTrial).toBe(true)
    expect(request).toHaveBeenCalledWith('/hearts/trial', { method: 'POST', signal: undefined })
  })
  it('rejects an ordinary free match returned from the trial endpoint', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ ...match, isTrial: false }))
    await expect(new HttpHeartsGateway('/hearts', request).startTrial()).rejects.toMatchObject({ code: 'hearts-invalid-response' })
  })
  it('accepts only boolean trial flags when the optional flag is present', () => {
    expect(isMatch(match)).toBe(true)
    expect(isMatch({ ...match, isTrial: false })).toBe(true)
    expect(isMatch({ ...match, isTrial: true })).toBe(true)
    expect(isMatch({ ...match, isTrial: 'true' })).toBe(false)
    expect(isMatch({ ...match, isTrial: 1 })).toBe(false)
  })
  it('rejects malformed onboarding rather than guessing that the player may enter a match', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ shouldAskPlayedBefore: 'false' }))
    await expect(new HttpHeartsGateway('/hearts', request).onboarding.getState()).rejects.toMatchObject({ code: 'hearts-invalid-response' })
  })
  it('cancels a dedicated trial and reads authoritative pending setup after cleanup', async () => {
    const request = vi.fn<typeof fetch>().mockResolvedValue(response({ shouldAskPlayedBefore: true }))
    expect(await new HttpHeartsGateway('/hearts', request).cancelTrial('trial/one')).toEqual({ shouldAskPlayedBefore: true })
    expect(request).toHaveBeenCalledWith('/hearts/trial/trial%2Fone/cancel', { method: 'POST', signal: undefined })
  })
})
