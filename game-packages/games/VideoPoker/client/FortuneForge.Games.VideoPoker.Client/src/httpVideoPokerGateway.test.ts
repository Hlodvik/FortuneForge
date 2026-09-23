import { afterEach, describe, expect, it, vi } from 'vitest'
import { VideoPokerGatewayError } from './contracts'
import { HttpVideoPokerGateway } from './httpVideoPokerGateway'

const awaitingRound = {
  roundId: 'round-7',
  balance: 100,
  coinsWagered: 3,
  handCount: 1,
  wager: 3,
  phase: 'awaiting-draw',
  initialCards: [
    { rank: 'ace', suit: 'clubs' },
    { rank: 'two', suit: 'diamonds' },
    { rank: 'three', suit: 'hearts' },
    { rank: 'four', suit: 'spades' },
    { rank: 'five', suit: 'clubs' },
  ],
  heldPositions: [0, 2],
  finalCards: null,
  handRank: null,
  payout: null,
  finalHands: null,
  handRanks: null,
  handPayouts: null,
}

afterEach(() => vi.unstubAllGlobals())

describe('HttpVideoPokerGateway', () => {
  it('requests status from the expected route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ available: true, minimumCoinsWagered: 1, maximumCoinsWagered: 5, coinValue: 1, balance: 100 }))
    vi.stubGlobal('fetch', fetchMock)

    const status = await new HttpVideoPokerGateway().getStatus()

    expect(status).toEqual({ available: true, minimumCoinsWagered: 1, maximumCoinsWagered: 5, coinValue: 1, balance: 100 })
    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/status', { signal: undefined })
  })

  it('gets a recorded round with its identifier encoded in the route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(awaitingRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpVideoPokerGateway().getRound('round / seven')

    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/rounds/round%20%2F%20seven', { signal: undefined })
  })

  it('posts the wager when creating a round and parses the response', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(awaitingRound))
    vi.stubGlobal('fetch', fetchMock)

    const round = await new HttpVideoPokerGateway().createRound(3)

    expect(round).toEqual(awaitingRound)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/rounds', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        'content-type': 'application/json',
        'Idempotency-Key': expect.stringMatching(/^video-poker-deal-[A-Za-z0-9]+$/),
      }),
      body: JSON.stringify({ coinsWagered: 3, handCount: 1 }),
      signal: undefined,
    }))
  })

  it('posts an explicitly selected multi-hand count', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ...awaitingRound, handCount: 5, wager: 15 }))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpVideoPokerGateway().createRound(3, { handCount: 5 })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/rounds', expect.objectContaining({
      body: JSON.stringify({ coinsWagered: 3, handCount: 5 }),
    }))
  })

  it('uses a caller-supplied key so a logical deal can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(awaitingRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpVideoPokerGateway().createRound(3, { idempotencyKey: 'video-poker-deal-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'video-poker-deal-retry-0001' }),
    }))
  })

  it('posts held positions to the round draw route', async () => {
    const completedRound = {
      ...awaitingRound,
      phase: 'completed',
      heldPositions: [0, 2],
      finalCards: awaitingRound.initialCards,
      handRank: 'straight',
      payout: 12,
      finalHands: [awaitingRound.initialCards],
      handRanks: ['straight'],
      handPayouts: [12],
    }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(completedRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpVideoPokerGateway().draw('round / seven', [0, 2])

    expect(fetchMock).toHaveBeenCalledWith('/api/games/video-poker/rounds/round%20%2F%20seven/draw', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        'content-type': 'application/json',
        'Idempotency-Key': expect.stringMatching(/^video-poker-draw-[A-Za-z0-9]+$/),
      }),
      body: JSON.stringify({ heldPositions: [0, 2] }),
      signal: undefined,
    }))
  })

  it('preserves API errors as typed gateway errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'insufficient-credits', message: 'Not enough credits.' }, 409)))

    const request = new HttpVideoPokerGateway().createRound(1)

    await expect(request).rejects.toMatchObject({
      name: 'VideoPokerGatewayError',
      code: 'insufficient-credits',
      status: 409,
      message: 'Not enough credits.',
    })
  })

  it('rejects malformed successful responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ...awaitingRound, initialCards: [] })))

    await expect(new HttpVideoPokerGateway().createRound(1)).rejects.toBeInstanceOf(VideoPokerGatewayError)
  })
})

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
