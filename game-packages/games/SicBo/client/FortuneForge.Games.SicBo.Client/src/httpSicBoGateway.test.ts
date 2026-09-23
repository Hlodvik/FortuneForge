import { afterEach, describe, expect, it, vi } from 'vitest'
import { SicBoGatewayError, type SicBoBetRequest } from './contracts'
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

const settledRound = {
  roundId: 'round-11', balance: 1_016.5, phase: 'settled', dice: [2, 2, 5], total: 9, isTriple: false,
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

  it('uses a caller-supplied key so a bet slip can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(settledRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpSicBoGateway().createRound(bets, { idempotencyKey: 'sic-bo-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/sic-bo/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'sic-bo-retry-0001' }),
    }))
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
})

function settlement(betIndex: number, bet: SicBoBetRequest, won: boolean, profitOdds: number, profit: number, totalReturn: number) {
  return { betIndex, ...bet, won, profitOdds, profit, totalReturn }
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
