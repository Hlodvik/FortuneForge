import { afterEach, describe, expect, it, vi } from 'vitest'
import { BaccaratGatewayError } from './contracts'
import { HttpBaccaratGateway } from './httpBaccaratGateway'

const settledRound = {
  roundId: 'round-11',
  balance: 998.05,
  betSide: 'banker',
  stake: 10,
  phase: 'settled',
  playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'eight', suit: 'spades' }],
  bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }],
  playerTotal: 6,
  bankerTotal: 9,
  outcome: 'banker',
  endedOnNatural: true,
  disposition: 'win',
  profit: 9.5,
  totalReturn: 19.5,
}

afterEach(() => vi.unstubAllGlobals())

describe('HttpBaccaratGateway', () => {
  it('requests status from the expected route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, balance: 1_000, mode: 'local-free-play' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpBaccaratGateway().getStatus()).resolves.toMatchObject({ available: true, balance: 1_000 })
    expect(fetchMock).toHaveBeenCalledWith('/api/games/baccarat/status', { signal: undefined })
  })

  it('posts bet side and stake and accepts multi-deck duplicate cards', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(settledRound))
    vi.stubGlobal('fetch', fetchMock)

    const round = await new HttpBaccaratGateway().createRound('banker', 10)

    expect(round).toEqual(settledRound)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/baccarat/rounds', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'content-type': 'application/json', 'Idempotency-Key': expect.stringMatching(/^baccarat-[A-Za-z0-9]+$/) }),
      body: JSON.stringify({ betSide: 'banker', stake: 10 }),
      signal: undefined,
    }))
  })

  it('uses a caller-supplied key so a logical deal can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(settledRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpBaccaratGateway().createRound('banker', 10, { idempotencyKey: 'baccarat-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/baccarat/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'baccarat-retry-0001' }),
    }))
  })

  it('preserves API errors as typed gateway errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'insufficient-balance', message: 'Not enough credits.' }, 400)))

    await expect(new HttpBaccaratGateway().createRound('player', 10)).rejects.toMatchObject({
      name: 'BaccaratGatewayError', code: 'insufficient-balance', status: 400, message: 'Not enough credits.',
    })
  })

  it('rejects malformed and inconsistent successful responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...settledRound, profit: 9.49 })))

    await expect(new HttpBaccaratGateway().createRound('banker', 10)).rejects.toBeInstanceOf(BaccaratGatewayError)
  })
})

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
