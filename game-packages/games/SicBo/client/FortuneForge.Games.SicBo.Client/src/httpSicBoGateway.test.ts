import { afterEach, describe, expect, it, vi } from 'vitest'
import { SicBoGatewayError, type SicBoBetRequest, type SicBoRound } from './contracts'
import { HttpSicBoGateway } from './httpSicBoGateway'

const bets: SicBoBetRequest[] = [
  { kind: 'small', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
  { kind: 'big', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
  { kind: 'odd', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
  { kind: 'even', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
  { kind: 'single-number', stake: 1, face: 2, total: null, firstFace: null, secondFace: null },
  { kind: 'total', stake: 1, face: null, total: 8, firstFace: null, secondFace: null },
  { kind: 'two-number-combination', stake: 1, face: null, total: null, firstFace: 2, secondFace: 5 },
  { kind: 'specific-double', stake: 1, face: 2, total: null, firstFace: null, secondFace: null },
  { kind: 'any-triple', stake: 1, face: null, total: null, firstFace: null, secondFace: null },
  { kind: 'specific-triple', stake: 1, face: 2, total: null, firstFace: null, secondFace: null },
]

const roundId = '9a'.repeat(32)
const settledRound: SicBoRound = {
  roundId, balance: 1_016.5, phase: 'settled', dice: [2, 2, 5], total: 9, isTriple: false,
  totalStaked: 10, totalReturn: 26.5, profit: 16.5,
  settlements: [
    settlement(0, bets[0]!, true, 1, 1, 2), settlement(1, bets[1]!, false, 0, -1, 0),
    settlement(2, bets[2]!, true, 1, 1, 2), settlement(3, bets[3]!, false, 0, -1, 0),
    settlement(4, bets[4]!, true, 2, 2, 3), settlement(5, bets[5]!, false, 0, -1, 0),
    settlement(6, bets[6]!, true, 6, 6, 7), settlement(7, bets[7]!, true, 11.5, 11.5, 12.5),
    settlement(8, bets[8]!, false, 0, -1, 0), settlement(9, bets[9]!, false, 0, -1, 0),
  ],
}

afterEach(() => vi.unstubAllGlobals())

describe('HttpSicBoGateway', () => {
  it('requests status from the expected route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1,
      maximumBetsPerRound: 10, balance: 1_000, mode: 'local-free-play',
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpSicBoGateway().getStatus()).resolves.toMatchObject({ maximumBetsPerRound: 10 })
    expect(fetchMock).toHaveBeenCalledWith('/api/games/sic-bo/status', { signal: undefined })
  })

  it('posts every selection shape and accepts an ordered multi-bet response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(settledRound))
    vi.stubGlobal('fetch', fetchMock)

    const round = await new HttpSicBoGateway().createRound(bets)

    expect(round).toEqual(settledRound)
    expect(round.settlements.map(settlement => settlement.kind)).toEqual(bets.map(bet => bet.kind))
    expect(fetchMock).toHaveBeenCalledWith('/api/games/sic-bo/rounds', expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ 'content-type': 'application/json', 'Idempotency-Key': expect.stringMatching(/^sic-bo-/) }), body: JSON.stringify({ bets }), signal: undefined,
    }))
  })

  it('calls native fetch with its global receiver', async () => {
    const receivers: unknown[] = []
    vi.stubGlobal('fetch', function (this: unknown) {
      receivers.push(this)
      return Promise.resolve(jsonResponse(settledRound))
    })

    await new HttpSicBoGateway().getRound(roundId)

    expect(receivers).toEqual([globalThis])
  })

  it('reads a known round through GET without posting a wager', async () => {
    const signal = new AbortController().signal
    const request = vi.fn().mockResolvedValue(jsonResponse(settledRound))

    await expect(new HttpSicBoGateway('/custom/sic-bo/', request).getRound(roundId, signal)).resolves.toEqual(settledRound)

    expect(request).toHaveBeenCalledExactlyOnceWith(`/custom/sic-bo/rounds/${roundId}`, { signal })
  })

  it.each(['', 'round-11', 'a'.repeat(63), 'a'.repeat(65), 'g'.repeat(64), `${'a'.repeat(63)}/`, ` ${'a'.repeat(64)}`])(
    'rejects invalid known-round identifier %j before transport', id => {
      const request = vi.fn()

      expect(() => new HttpSicBoGateway(undefined, request).getRound(id)).toThrow(SicBoGatewayError)

      expect(request).not.toHaveBeenCalled()
    },
  )

  it('rejects a successful GET for a different round', async () => {
    const request = vi.fn().mockResolvedValue(jsonResponse({ ...settledRound, roundId: 'b'.repeat(64) }))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('retains the local sample POST identifier while requiring 64 hex for known-round reads', async () => {
    const localId = '1234567890abcdef'.repeat(2)
    const request = vi.fn().mockResolvedValue(jsonResponse({ ...settledRound, roundId: localId }))
    const gateway = new HttpSicBoGateway(undefined, request)

    await expect(gateway.createRound(bets)).resolves.toMatchObject({ roundId: localId })
    expect(() => gateway.getRound(localId)).toThrow(SicBoGatewayError)
    expect(request).toHaveBeenCalledTimes(1)
  })

  it('captures the submitted ordered slip before the caller can mutate it', async () => {
    let resolve!: (response: Response) => void
    const request = vi.fn().mockImplementation(() => new Promise<Response>(complete => { resolve = complete }))
    const submitted = bets.map(bet => ({ ...bet }))
    const result = new HttpSicBoGateway(undefined, request).createRound(submitted)
    submitted[0]!.stake = 2
    submitted.reverse()
    resolve(jsonResponse(settledRound))

    await expect(result).resolves.toEqual(settledRound)
    expect(JSON.parse(request.mock.calls[0]![1].body).bets).toEqual(bets)
  })

  it('uses a caller-supplied key so a bet slip can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(settledRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpSicBoGateway().createRound(bets, { idempotencyKey: 'sic-bo-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/sic-bo/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'sic-bo-retry-0001' }),
    }))
  })

  it.each(['', 'short-key', 'a'.repeat(129), 'sic-bo-retry key-0001', 'sic-bo-retry/0001', 'sic-bo-retry-0001\n']) (
    'rejects invalid idempotency key %j before transport', key => {
      const request = vi.fn()

      expect(() => new HttpSicBoGateway(undefined, request).createRound(bets, { idempotencyKey: key })).toThrow(SicBoGatewayError)

      expect(request).not.toHaveBeenCalled()
    },
  )

  it('passes the caller signal and key to POST unchanged', async () => {
    const signal = new AbortController().signal
    const request = vi.fn().mockResolvedValue(jsonResponse(settledRound))

    await new HttpSicBoGateway(undefined, request).createRound(bets, { signal, idempotencyKey: 'sic-bo-retry-0001' })

    expect(request).toHaveBeenCalledExactlyOnceWith('/api/games/sic-bo/rounds', expect.objectContaining({ signal, headers: expect.objectContaining({ 'Idempotency-Key': 'sic-bo-retry-0001' }) }))
  })

  it('rejects malformed dice, aggregate arithmetic, indexes, and settlement arithmetic', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(jsonResponse({ ...settledRound, dice: [2, 2, 7] }))
      .mockResolvedValueOnce(jsonResponse({ ...settledRound, totalReturn: 26.4 }))
      .mockResolvedValueOnce(jsonResponse({ ...settledRound, settlements: [{ ...settledRound.settlements[0], betIndex: 1 }, ...settledRound.settlements.slice(1)] }))
      .mockResolvedValueOnce(jsonResponse({ ...settledRound, settlements: [{ ...settledRound.settlements[0], profit: 0.9 }, ...settledRound.settlements.slice(1)] })))

    const gateway = new HttpSicBoGateway()
    await expect(gateway.createRound(bets)).rejects.toBeInstanceOf(SicBoGatewayError)
    await expect(gateway.createRound(bets)).rejects.toBeInstanceOf(SicBoGatewayError)
    await expect(gateway.createRound(bets)).rejects.toBeInstanceOf(SicBoGatewayError)
    await expect(gateway.createRound(bets)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('rejects malformed selection requests before issuing a call', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    expect(() => new HttpSicBoGateway().createRound([{ ...bets[0]!, face: 1 }])).toThrow(SicBoGatewayError)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it.each([
    ['negative stake', { ...bets[0]!, stake: -1 }],
    ['nonfinite stake', { ...bets[0]!, stake: Infinity }],
    ['missing required null selector', { kind: 'small', stake: 1, total: null, firstFace: null, secondFace: null }],
    ['reversed combination', { ...bets[6]!, firstFace: 5, secondFace: 2 }],
    ['duplicate combination faces', { ...bets[6]!, firstFace: 2, secondFace: 2 }],
    ['out of range face', { ...bets[4]!, face: 7 }],
    ['fractional face', { ...bets[4]!, face: 1.5 }],
    ['unwagerable total', { ...bets[5]!, total: 18 }],
    ['extra property', { ...bets[0]!, serverSeed: 'secret' }],
  ])('rejects %s in the submitted slip before transport', (_name, bet) => {
    const request = vi.fn()

    expect(() => new HttpSicBoGateway(undefined, request).createRound([bet as SicBoBetRequest])).toThrow(SicBoGatewayError)

    expect(request).not.toHaveBeenCalled()
  })

  it.each([
    ['wrong phase', { phase: 'rolling' }],
    ['short dice', { dice: [2, 2] }],
    ['extra die', { dice: [2, 2, 5, 5] }],
    ['fractional die', { dice: [2, 2, 5.5] }],
    ['incorrect dice total', { total: 10 }],
    ['incorrect triple flag', { isTriple: true }],
    ['negative balance', { balance: -1 }],
    ['nonfinite balance', { balance: Infinity }],
    ['nonfinite aggregate return', { totalReturn: Infinity }],
    ['nonfinite aggregate profit', { profit: NaN }],
    ['empty settlements', { settlements: [] }],
    ['seed outside the contract', { serverSeed: 'unexposed-seed' }],
  ])('rejects %s in known-round reads', async (_name, change) => {
    const request = vi.fn().mockResolvedValue(rawResponse({ ...settledRound, ...change }))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it.each([
    ['reordered index', { betIndex: 1 }],
    ['missing selection', { face: undefined }],
    ['extra selector', { face: 2 }],
    ['lost winning stake', { won: false }],
    ['zero winning odds', { profitOdds: 0 }],
    ['nonfinite odds', { profitOdds: Infinity }],
    ['nonfinite stake', { stake: Infinity }],
    ['inconsistent profit', { profit: 1.5 }],
    ['inconsistent total return', { totalReturn: 1.5 }],
    ['seed inside settlement', { serverSeed: 'secret' }],
  ])('rejects %s in a known-round settlement', async (_name, change) => {
    const request = vi.fn().mockResolvedValue(rawResponse({ ...settledRound, settlements: [{ ...settledRound.settlements[0], ...change }, ...settledRound.settlements.slice(1)] }))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('rejects reordered submitted bets even when returned arithmetic remains consistent', async () => {
    const reordered = [...bets].reverse()
    const request = vi.fn().mockResolvedValue(jsonResponse(settledRound))

    await expect(new HttpSicBoGateway(undefined, request).createRound(reordered)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('rejects a subtly changed submitted stake', async () => {
    const submitted = [{ ...bets[0]!, stake: 1 + Number.EPSILON }, ...bets.slice(1)]
    const request = vi.fn().mockResolvedValue(jsonResponse(settledRound))

    await expect(new HttpSicBoGateway(undefined, request).createRound(submitted)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('rejects overflowing aggregate arithmetic instead of treating infinity as equal', async () => {
    const stake = Number.MAX_VALUE
    const bet = { ...bets[0]!, stake }
    const returned = {
      ...settledRound, totalStaked: stake, totalReturn: 0, profit: -stake,
      settlements: [settlement(0, bet, false, 0, -stake, 0), settlement(1, bet, false, 0, -stake, 0)],
    }
    const request = vi.fn().mockResolvedValue(jsonResponse(returned))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('rejects overflowing winning settlement arithmetic', async () => {
    const bet = { ...bets[0]!, stake: Number.MAX_VALUE }
    const returned = { ...settledRound, settlements: [settlement(0, bet, true, 2, Number.MAX_VALUE, Number.MAX_VALUE)] }
    const request = vi.fn().mockResolvedValue(jsonResponse(returned))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it.each([
    { available: 'yes' }, { minimumStake: 0 }, { maximumStakePerBet: 0.5 }, { stakeIncrement: 0 },
    { maximumBetsPerRound: 1.5 }, { maximumBetsPerRound: Number.MAX_SAFE_INTEGER + 1 }, { balance: -1 },
    { balance: Infinity }, { mode: ' ' }, { serverSeed: 'secret' },
  ])('rejects malformed status %j', async change => {
    const request = vi.fn().mockResolvedValue(rawResponse({
      available: true, minimumStake: 1, maximumStakePerBet: 100, stakeIncrement: 1,
      maximumBetsPerRound: 20, balance: 1_000, mode: 'three-dice-sic-bo', ...change,
    }))

    await expect(new HttpSicBoGateway(undefined, request).getStatus()).rejects.toBeInstanceOf(SicBoGatewayError)
  })

  it('preserves API errors as typed gateway errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'sic-bo-insufficient-balance', message: 'Not enough credits.' }, 400)))

    await expect(new HttpSicBoGateway().createRound(bets)).rejects.toMatchObject({
      name: 'SicBoGatewayError', code: 'sic-bo-insufficient-balance', status: 400, message: 'Not enough credits.',
    })
  })

  it('preserves an aborted status request for callers that intentionally cancel it', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new DOMException('The operation was aborted.', 'AbortError')))

    await expect(new HttpSicBoGateway().getStatus(new AbortController().signal)).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('preserves an abort while decoding a known-round response', async () => {
    const aborted = new DOMException('The operation was aborted.', 'AbortError')
    const request = vi.fn().mockResolvedValue({ ok: true, status: 200, json: () => Promise.reject(aborted) })

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toBe(aborted)
  })

  it('reports malformed JSON as an invalid response', async () => {
    const request = vi.fn().mockResolvedValue(new Response('not json', { status: 200 }))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toMatchObject({ code: 'sic-bo-request-failed' })
  })

  it('preserves the missing-round error so recovery can remain read only', async () => {
    const request = vi.fn().mockResolvedValue(jsonResponse({ code: 'sic-bo-round-not-found', message: 'Sic Bo round not found.' }, 404))

    await expect(new HttpSicBoGateway(undefined, request).getRound(roundId)).rejects.toMatchObject({ code: 'sic-bo-round-not-found', status: 404 })
    expect(request).toHaveBeenCalledExactlyOnceWith(`/api/games/sic-bo/rounds/${roundId}`, { signal: undefined })
  })

  it('does not make another mutation after a failed POST', async () => {
    const request = vi.fn().mockRejectedValue(new Error('Connection lost'))

    await expect(new HttpSicBoGateway(undefined, request).createRound(bets, { idempotencyKey: 'sic-bo-retry-0001' })).rejects.toMatchObject({ message: 'Connection lost' })
    expect(request).toHaveBeenCalledTimes(1)
  })
})

function settlement(betIndex: number, bet: SicBoBetRequest, won: boolean, profitOdds: number, profit: number, totalReturn: number) {
  return { betIndex, ...bet, won, profitOdds, profit, totalReturn }
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}

function rawResponse(body: unknown): Pick<Response, 'ok' | 'status' | 'json'> {
  return { ok: true, status: 200, json: async () => body }
}
