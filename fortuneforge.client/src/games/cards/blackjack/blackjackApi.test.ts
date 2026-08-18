import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  requestBlackjackStatus,
  startBlackjackGame,
  toPlayingCard,
} from './blackjackApi'

describe('blackjack API', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('checks the demo endpoint before enabling a table', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      available: true,
      minimumWager: 0.5,
      maximumWager: 100,
      wagerIncrement: 0.5,
      dealerRule: 'Dealer stands on all 17s',
      blackjackPayout: '3:2',
      doubleAllowed: true,
      splitAllowed: false,
      insuranceAllowed: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    const status = await requestBlackjackStatus(true)

    expect(status.available).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/cards/blackjack/demo/status',
      expect.objectContaining({ method: 'GET', credentials: 'omit', cache: 'no-store' }),
    )
  })

  it('sends both idempotency and isolated demo-session headers on a deal', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(gameResponse), {
      status: 201,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await startBlackjackGame(5, 'deal_request_0000001', true)

    const [, request] = fetchMock.mock.calls[0] as [string, RequestInit]
    const headers = new Headers(request.headers)
    expect(headers.get('Idempotency-Key')).toBe('deal_request_0000001')
    expect(headers.get('X-Demo-Session-Id')?.length).toBeGreaterThanOrEqual(16)
    expect(request.body).toBe(JSON.stringify({ wager: 5 }))
  })

  it('maps server ranks to the frozen shared playing-card model', () => {
    expect(toPlayingCard({ rank: 'A', suit: 'spades', hidden: false }, 0)).toMatchObject({
      suit: 'spades',
      rank: 1,
    })
    expect(toPlayingCard({ rank: 'K', suit: 'hearts', hidden: false }, 1)).toMatchObject({
      suit: 'hearts',
      rank: 13,
    })
  })
})

const gameResponse = {
  gameId: 'a'.repeat(64),
  status: 'active',
  outcome: null,
  message: 'Choose hit, stand, or double.',
  wager: 5,
  totalWager: 5,
  payout: 0,
  balance: 9995,
  player: {
    cards: [
      { rank: 'A', suit: 'spades', hidden: false },
      { rank: 'K', suit: 'hearts', hidden: false },
    ],
    score: 21,
    soft: true,
    blackjack: true,
    bust: false,
  },
  dealer: {
    cards: [
      { rank: '9', suit: 'clubs', hidden: false },
      { rank: null, suit: null, hidden: true },
    ],
    score: null,
    soft: false,
    blackjack: false,
    bust: false,
  },
  canHit: true,
  canStand: true,
  canDouble: true,
  version: 1,
  createdAtUtc: '2026-08-14T00:00:00Z',
  updatedAtUtc: '2026-08-14T00:00:00Z',
}
