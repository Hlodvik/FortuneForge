import { afterEach, describe, expect, it, vi } from 'vitest'
import { CasinoWarGatewayError } from './contracts'
import { HttpCasinoWarGateway } from './httpCasinoWarGateway'

const playerOpeningCard = { rank: 'ace', suit: 'clubs' } as const
const dealerOpeningCard = { rank: 'king', suit: 'diamonds' } as const

const immediateRound = {
  roundId: 'round-11',
  balance: 1_008,
  primaryStake: 10,
  tieStake: 2,
  phase: 'completed',
  playerOpeningCard,
  dealerOpeningCard,
  decision: null,
  playerWarCard: null,
  dealerWarCard: null,
  primarySettlement: {
    disposition: 'win', outcome: 'player-opening-win', totalWagered: 10, totalReturn: 20, profit: 10,
  },
  tieSettlement: {
    won: false, disposition: 'loss', outcome: 'player-win', stake: 2, totalReturn: 0, profit: -2,
  },
}

const awaitingTieRound = {
  roundId: 'round-12',
  balance: 988,
  primaryStake: 10,
  tieStake: 2,
  phase: 'awaiting-tie-decision',
  playerOpeningCard: { rank: 'queen', suit: 'clubs' },
  dealerOpeningCard: { rank: 'queen', suit: 'spades' },
  decision: null,
  playerWarCard: null,
  dealerWarCard: null,
  primarySettlement: null,
  tieSettlement: {
    won: true, disposition: 'win', outcome: 'tie-decision-required', stake: 2, totalReturn: 22, profit: 20,
  },
}

const completedWarRound = {
  ...awaitingTieRound,
  phase: 'completed',
  decision: 'go-to-war',
  playerWarCard: { rank: 'ace', suit: 'spades' },
  dealerWarCard: { rank: 'king', suit: 'clubs' },
  primarySettlement: {
    disposition: 'win', outcome: 'player-war-win', totalWagered: 20, totalReturn: 30, profit: 10,
  },
}

afterEach(() => vi.unstubAllGlobals())

describe('HttpCasinoWarGateway', () => {
  it('requests status from the expected route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({
      available: true, minimumPrimaryStake: 1, maximumPrimaryStake: 100, stakeIncrement: 1,
      maximumTieStake: 25, balance: 1_000, mode: 'local-free-play',
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpCasinoWarGateway().getStatus()).resolves.toMatchObject({ available: true, maximumTieStake: 25 })
    expect(fetchMock).toHaveBeenCalledWith('/api/games/casino-war/status', { signal: undefined })
  })

  it('gets a recorded round with its identifier encoded in the route', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(awaitingTieRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpCasinoWarGateway().getRound('round / twelve')

    expect(fetchMock).toHaveBeenCalledWith('/api/games/casino-war/rounds/round%20%2F%20twelve', { signal: undefined })
  })

  it('posts primary and optional Tie stakes and accepts an immediate result', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(immediateRound))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpCasinoWarGateway().createRound(10, 2)).resolves.toEqual(immediateRound)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/casino-war/rounds', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'content-type': 'application/json', 'Idempotency-Key': expect.stringMatching(/^casino-war-opening-/) }),
      body: JSON.stringify({ primaryStake: 10, tieStake: 2 }),
      signal: undefined,
    }))
  })

  it('uses a caller-supplied key so an opening wager can be retried safely', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(immediateRound))
    vi.stubGlobal('fetch', fetchMock)

    await new HttpCasinoWarGateway().createRound(10, 2, { idempotencyKey: 'casino-war-opening-retry-0001' })

    expect(fetchMock).toHaveBeenCalledWith('/api/games/casino-war/rounds', expect.objectContaining({
      headers: expect.objectContaining({ 'Idempotency-Key': 'casino-war-opening-retry-0001' }),
    }))
  })

  it('accepts an opening tie awaiting the player decision', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(awaitingTieRound)))

    const round = await new HttpCasinoWarGateway().createRound(10, 2)

    expect(round.phase).toBe('awaiting-tie-decision')
    expect(round.primarySettlement).toBeNull()
    expect(round.tieSettlement?.totalReturn).toBe(22)
  })

  it('posts a decision to its round-specific route and accepts a completed war', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(completedWarRound))
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HttpCasinoWarGateway().decide('round/12', 'go-to-war')).resolves.toEqual(completedWarRound)
    expect(fetchMock).toHaveBeenCalledWith('/api/games/casino-war/rounds/round%2F12/decision', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'content-type': 'application/json', 'Idempotency-Key': expect.stringMatching(/^casino-war-decision-/) }),
      body: JSON.stringify({ decision: 'go-to-war' }),
      signal: undefined,
    }))
  })

  it('preserves API and transport failures as typed gateway errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'insufficient-balance', message: 'Not enough credits.' }, 400)))

    await expect(new HttpCasinoWarGateway().createRound(10, 0)).rejects.toMatchObject({
      name: 'CasinoWarGatewayError', code: 'insufficient-balance', status: 400, message: 'Not enough credits.',
    })

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('Network unavailable')))
    await expect(new HttpCasinoWarGateway().getStatus()).rejects.toMatchObject({
      name: 'CasinoWarGatewayError', code: 'casino-war-request-failed', status: 0, message: 'Network unavailable',
    })
  })

  it('rejects malformed phase consistency and arithmetic', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(jsonResponse({
      ...immediateRound,
      phase: 'awaiting-tie-decision',
      primarySettlement: null,
    })).mockResolvedValueOnce(jsonResponse({
      ...completedWarRound,
      primarySettlement: { ...completedWarRound.primarySettlement, profit: 9.99 },
    })))

    const gateway = new HttpCasinoWarGateway()
    await expect(gateway.createRound(10, 2)).rejects.toBeInstanceOf(CasinoWarGatewayError)
    await expect(gateway.decide('round-12', 'go-to-war')).rejects.toBeInstanceOf(CasinoWarGatewayError)
  })

  it('rejects malformed card unions', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({
      ...immediateRound,
      playerOpeningCard: { rank: 'joker', suit: 'clubs' },
    })))

    await expect(new HttpCasinoWarGateway().createRound(10, 2)).rejects.toBeInstanceOf(CasinoWarGatewayError)
  })
})

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
