import { describe, expect, it, vi } from 'vitest'
import { CrapsGatewayError, type CrapsRound, type CrapsStatus } from './contracts'
import { HttpCrapsGateway } from './httpCrapsGateway'

const roundId = '00000000-0000-4000-8000-000000000001'
const status: CrapsStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, mode: 'free-play-pass-line' }
const open: CrapsRound = { roundId, stake: 10, phase: 'come-out', point: null, rolls: [], lastOutcome: null, extraBets: [] }
const point: CrapsRound = {
  ...open, phase: 'point', point: 6,
  rolls: [{ rollNumber: 1, first: 2, second: 4, total: 6, result: 'point-established' }],
  lastOutcome: { first: 2, second: 4, total: 6, result: 'point-established', isTerminal: false, totalReturn: null },
  extraBets: [{ kind: 'field', stake: 5, resolved: true, won: false, totalReturn: 0 }, { kind: 'odds', stake: 1, resolved: false, won: false, totalReturn: null }],
}
const continuing: CrapsRound = {
  ...point,
  rolls: [{ ...point.rolls[0], result: null }, { rollNumber: 2, first: 4, second: 5, total: 9, result: 'no-decision' }],
  lastOutcome: { first: 4, second: 5, total: 9, result: 'no-decision', isTerminal: false, totalReturn: null },
}
const settled: CrapsRound = {
  ...point, phase: 'resolved',
  rolls: [{ ...point.rolls[0], result: null }, { rollNumber: 2, first: 1, second: 5, total: 6, result: 'point-hit' }],
  lastOutcome: { first: 1, second: 5, total: 6, result: 'point-hit', isTerminal: true, totalReturn: 20 },
  extraBets: [{ kind: 'field', stake: 5, resolved: true, won: false, totalReturn: 0 }, { kind: 'odds', stake: 1, resolved: true, won: true, totalReturn: 2.2 }],
}
const response = (value: unknown, code = 200) => new Response(JSON.stringify(value), { status: code, headers: { 'content-type': 'application/json' } })
const gatewayFor = (value: unknown) => new HttpCrapsGateway(undefined, vi.fn().mockResolvedValue(response(value)))

describe('HttpCrapsGateway public recovery contract', () => {
  it('calls the default native transport without binding the gateway as its receiver', async () => {
    const fetcher = vi.fn(function (this: unknown) {
      if (this !== undefined) throw new TypeError('Illegal invocation')
      return Promise.resolve(response(status))
    })
    vi.stubGlobal('fetch', fetcher)
    try {
      await expect(new HttpCrapsGateway().getStatus()).resolves.toEqual(status)
      expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/craps/status', { signal: undefined })
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('reads status with the caller cancellation signal and an injected transport', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(status))
    const signal = new AbortController().signal
    await expect(new HttpCrapsGateway('/table/', fetcher).getStatus(signal)).resolves.toEqual(status)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/table/status', { signal })
  })

  it('reads the exact private round without replaying a write', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(point))
    const signal = new AbortController().signal
    await expect(new HttpCrapsGateway('/table/', fetcher).getRound(roundId, signal)).resolves.toEqual(point)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/table/rounds/${roundId}`, { signal })
  })

  it('encodes the requested identifier instead of treating it as a URL path', async () => {
    const unusualId = 'private/round?x=1'
    const fetcher = vi.fn().mockResolvedValue(response({ ...open, roundId: unusualId }))
    await new HttpCrapsGateway(undefined, fetcher).getRound(unusualId)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/craps/rounds/private%2Fround%3Fx%3D1', { signal: undefined })
  })

  it('posts the exact Pass Line and one-roll drafts, including duplicate extras', async () => {
    const extras = [{ kind: 'field' as const, stake: 5 }, { kind: 'field' as const, stake: 2 }]
    const returned = { ...open, extraBets: extras.map(bet => ({ ...bet, resolved: false, won: false, totalReturn: null })) }
    const fetcher = vi.fn().mockResolvedValue(response(returned))
    const signal = new AbortController().signal
    await expect(new HttpCrapsGateway(undefined, fetcher).startRound(10, signal, extras)).resolves.toEqual(returned)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/api/games/craps/rounds', {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ stake: 10, extraBets: extras }), signal,
    })
  })

  it('sends an empty extras list when no optional wagers were selected', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(open))
    await new HttpCrapsGateway(undefined, fetcher).startRound(10)
    expect(fetcher.mock.calls[0][1].body).toBe(JSON.stringify({ stake: 10, extraBets: [] }))
  })

  it('posts a roll once without an invented body or idempotency header', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(point))
    const signal = new AbortController().signal
    await new HttpCrapsGateway(undefined, fetcher).roll(roundId, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/craps/rounds/${roundId}/roll`, { method: 'POST', signal })
  })

  it('posts the exact odds stake without imposing divisibility or Pass Line ratio caps', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ ...point, extraBets: [{ kind: 'odds', stake: 100, resolved: false, won: false, totalReturn: null }] }))
    const signal = new AbortController().signal
    await new HttpCrapsGateway(undefined, fetcher).placeOdds(roundId, 100, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/craps/rounds/${roundId}/odds`, {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ stake: 100 }), signal,
    })
  })

  it.each(['getRound', 'roll', 'placeOdds'] as const)('rejects a different round returned by %s', async method => {
    const gateway = gatewayFor({ ...point, roundId: 'another-round' })
    const result = method === 'placeOdds' ? gateway.placeOdds(roundId, 1) : gateway[method](roundId)
    await expect(result).rejects.toBeInstanceOf(CrapsGatewayError)
  })

  it('preserves the missing-round response so recovery can clear an expired round', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ code: 'craps-round-not-found', message: 'That Craps table is not available.' }, 404))
    await expect(new HttpCrapsGateway(undefined, fetcher).getRound(roundId)).rejects.toMatchObject({
      name: 'CrapsGatewayError', code: 'craps-round-not-found', status: 404, message: 'That Craps table is not available.',
    })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('preserves an action rejection and never retries the POST', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ code: 'craps-invalid-action', message: 'Odds are already placed for this point.' }, 400))
    await expect(new HttpCrapsGateway(undefined, fetcher).placeOdds(roundId, 1)).rejects.toMatchObject({ code: 'craps-invalid-action', status: 400 })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('reports a non-JSON service failure with its HTTP status', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('Unavailable', { status: 503 }))
    await expect(new HttpCrapsGateway(undefined, fetcher).getStatus()).rejects.toMatchObject({ code: 'craps-request-failed', status: 503 })
  })

  it('rejects malformed JSON from an otherwise successful response', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{', { status: 200 }))
    await expect(new HttpCrapsGateway(undefined, fetcher).roll(roundId)).rejects.toBeInstanceOf(CrapsGatewayError)
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('passes through a lost transport response without replaying a mutation', async () => {
    const failure = new TypeError('Connection lost')
    const fetcher = vi.fn().mockRejectedValue(failure)
    await expect(new HttpCrapsGateway(undefined, fetcher).roll(roundId)).rejects.toBe(failure)
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('preserves cancellation identity and forwards the caller signal', async () => {
    const cancelled = new DOMException('Cancelled', 'AbortError')
    const fetcher = vi.fn().mockRejectedValue(cancelled)
    const abort = new AbortController()
    abort.abort()
    await expect(new HttpCrapsGateway(undefined, fetcher).getRound(roundId, abort.signal)).rejects.toBe(cancelled)
    expect(fetcher.mock.calls[0][1].signal).toBe(abort.signal)
  })
})

describe('Craps response integrity', () => {
  it.each([open, point, continuing, settled])('accepts the service phase/history/settlement contract', async value => {
    await expect(gatewayFor(value).getRound(roundId)).resolves.toEqual(value)
  })

  it('keeps fractional server odds returns and does not recalculate payouts', async () => {
    const returned = await gatewayFor(settled).getRound(roundId)
    expect(returned.extraBets?.[1].totalReturn).toBe(2.2)
  })

  it.each([
    { ...status, available: false }, { ...status, minimumStake: 0.5, maximumStake: 0.5, stakeIncrement: 0.5 },
  ])('accepts authoritative availability and valid fractional limits', async value => {
    await expect(gatewayFor(value).getStatus()).resolves.toEqual(value)
  })

  it.each([
    { ...status, minimumStake: 0 }, { ...status, maximumStake: 0.5 }, { ...status, stakeIncrement: 0 },
    { ...status, minimumStake: NaN }, { ...status, maximumStake: Infinity }, { ...status, mode: '' }, { ...status, mode: '  ' },
  ])('rejects invalid limits or an absent table mode', async value => {
    await expect(gatewayFor(value).getStatus()).rejects.toBeInstanceOf(CrapsGatewayError)
  })

  it.each([
    { ...open, roundId: '' }, { ...open, stake: 0 }, { ...open, point: 7 }, { ...point, point: 11 },
    { ...open, phase: 'invalid' }, { ...point, phase: 'come-out' }, { ...point, point: null },
    { ...point, lastOutcome: null }, { ...open, lastOutcome: point.lastOutcome },
    { ...point, rolls: [] }, { ...point, rolls: [{ ...point.rolls[0], rollNumber: 0 }] },
    { ...continuing, rolls: [continuing.rolls[0], { ...continuing.rolls[1], rollNumber: 3 }] },
    { ...point, rolls: [{ ...point.rolls[0], first: 0 }] }, { ...point, rolls: [{ ...point.rolls[0], second: 7 }] },
    { ...point, rolls: [{ ...point.rolls[0], total: 5 }] },
    { ...point, rolls: [{ ...point.rolls[0], result: null }] },
    { ...point, lastOutcome: { ...point.lastOutcome, first: 3, second: 3 } },
    { ...point, lastOutcome: { ...point.lastOutcome, total: 5 } },
    { ...point, lastOutcome: { ...point.lastOutcome, isTerminal: true } },
    { ...point, lastOutcome: { ...point.lastOutcome, totalReturn: 20 } },
    { ...settled, phase: 'point' }, { ...point, phase: 'resolved' },
    { ...settled, lastOutcome: { ...settled.lastOutcome, totalReturn: null } },
    { ...settled, lastOutcome: { ...settled.lastOutcome, totalReturn: -1 } },
    { ...settled, lastOutcome: { ...settled.lastOutcome, totalReturn: Infinity } },
    { ...point, point: 4 },
    { ...point, extraBets: [{ kind: 'place', stake: 1, resolved: false, won: false, totalReturn: null }] },
    { ...open, extraBets: [{ kind: 'field', stake: 0, resolved: false, won: false, totalReturn: null }] },
    { ...open, extraBets: [{ kind: 'field', stake: 1, resolved: false, won: true, totalReturn: null }] },
    { ...open, extraBets: [{ kind: 'field', stake: 1, resolved: false, won: false, totalReturn: 0 }] },
    { ...point, extraBets: [{ kind: 'field', stake: 1, resolved: true, won: false, totalReturn: null }] },
    { ...point, extraBets: [{ kind: 'field', stake: 1, resolved: true, won: false, totalReturn: 2 }] },
    { ...point, extraBets: [{ kind: 'field', stake: 1, resolved: false, won: false, totalReturn: null }] },
    { ...open, extraBets: [{ kind: 'odds', stake: 1, resolved: false, won: false, totalReturn: null }] },
    { ...settled, extraBets: point.extraBets },
  ])('rejects contradictory or malformed round state', async value => {
    await expect(gatewayFor(value).getRound(roundId)).rejects.toBeInstanceOf(CrapsGatewayError)
  })

  it.each([
    { first: 1, second: 6, total: 7, result: 'natural-win' as const, point: null, totalReturn: 20 },
    { first: 1, second: 1, total: 2, result: 'craps-loss' as const, point: null, totalReturn: 0 },
    { first: 3, second: 4, total: 7, result: 'seven-out' as const, point: 6, totalReturn: 0 },
  ])('accepts a valid terminal result without deriving the monetary return', async value => {
    const { point: roundPoint, totalReturn, ...roll } = value
    const valueRound: CrapsRound = {
      ...open, phase: 'resolved', point: roundPoint,
      rolls: roundPoint === null ? [{ ...roll, rollNumber: 1 }] : [{ ...point.rolls[0], result: null }, { ...roll, rollNumber: 2 }],
      lastOutcome: { ...roll, isTerminal: true, totalReturn },
    }
    await expect(gatewayFor(valueRound).getRound(roundId)).resolves.toEqual(valueRound)
  })

  it('rejects a no-decision label attached to a point hit', async () => {
    const wrong = { ...point, rolls: [{ ...point.rolls[0], result: 'no-decision' }], lastOutcome: { ...point.lastOutcome, result: 'no-decision' } }
    await expect(gatewayFor(wrong).getRound(roundId)).rejects.toBeInstanceOf(CrapsGatewayError)
  })
})
