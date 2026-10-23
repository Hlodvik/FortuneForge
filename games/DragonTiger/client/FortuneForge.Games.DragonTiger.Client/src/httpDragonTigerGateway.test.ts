import { describe, expect, it, vi } from 'vitest'
import type { DragonTigerRound, DragonTigerStakes } from './contracts'
import { HttpDragonTigerGateway } from './httpDragonTigerGateway'

const stakes: DragonTigerStakes = { dragonStake: 2, tigerStake: 0, tieStake: 0 }
const settled: DragonTigerRound = {
  roundId: 'round-1', phase: 'settled', balance: 102,
  dragonCard: { rank: 'king', suit: 'hearts' }, tigerCard: { rank: 'ace', suit: 'spades' },
  dragonValue: 13, tigerValue: 1, outcome: 'dragon',
  bets: [
    { side: 'dragon', stake: 2, disposition: 'win', profit: 2, totalReturn: 4 },
    { side: 'tiger', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
    { side: 'tie', stake: 0, disposition: 'not-placed', profit: 0, totalReturn: 0 },
  ],
  totalStake: 2, profit: 2, totalReturn: 4, shoeCardsUsed: 2, shoeCardsRemaining: 414,
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const makeGateway = (value: unknown) => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json(value))
  return { gateway: new HttpDragonTigerGateway('/custom/table/', fetcher), fetcher }
}

describe('HttpDragonTigerGateway', () => {
  it('posts the selected wagers and original idempotency key, with abort support', async () => {
    const { gateway, fetcher } = makeGateway(settled)
    const signal = new AbortController().signal
    expect(await gateway.createRound(stakes, { idempotencyKey: 'dragon-tiger-original-key', signal })).toEqual(settled)
    expect(fetcher).toHaveBeenCalledOnce()
    expect(fetcher).toHaveBeenCalledWith('/custom/table/rounds', {
      method: 'POST', signal,
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'dragon-tiger-original-key' },
      body: JSON.stringify(stakes),
    })
    // Native browser fetch rejects invocation with the gateway as its receiver.
    expect(fetcher.mock.contexts[0]).toBeUndefined()
  })

  it('accepts the exact quarter-credit return of a half-credit main bet on a tie', async () => {
    const tied: DragonTigerRound = {
      ...settled, balance: 99.75, dragonCard: { rank: 'ace', suit: 'hearts' },
      dragonValue: 1, outcome: 'tie', totalStake: .5, profit: -.25, totalReturn: .25,
      bets: [
        { side: 'dragon', stake: .5, disposition: 'half-loss', profit: -.25, totalReturn: .25 },
        settled.bets[1], settled.bets[2],
      ],
    }
    const { gateway } = makeGateway(tied)
    await expect(gateway.createRound({ ...stakes, dragonStake: .5 }, { idempotencyKey: 'original-key' })).resolves.toEqual(tied)
  })

  it.each([
    ['outcome contradicts the cards', { ...settled, outcome: 'tiger' }],
    ['card values contradict the ranks', { ...settled, dragonValue: 1 }],
    ['invalid card rank', { ...settled, dragonCard: { rank: 'joker', suit: 'hearts' } }],
    ['wrong winning payout', { ...settled, bets: [{ ...settled.bets[0], totalReturn: 6 }, ...settled.bets.slice(1)] }],
    ['wrong disposition', { ...settled, bets: [{ ...settled.bets[0], disposition: 'loss' }, ...settled.bets.slice(1)] }],
    ['duplicate spot', { ...settled, bets: [settled.bets[0], settled.bets[1], settled.bets[1]] }],
    ['missing spot', { ...settled, bets: settled.bets.slice(0, 2) }],
    ['incorrect total return', { ...settled, totalReturn: 5 }],
    ['incorrect net profit', { ...settled, profit: 4 }],
    ['incorrect total stake', { ...settled, totalStake: 3 }],
    ['negative balance', { ...settled, balance: -1 }],
    ['invalid shoe accounting', { ...settled, shoeCardsRemaining: 416 }],
  ])('rejects a successful HTTP response with %s', async (_reason, payload) => {
    const { gateway } = makeGateway(payload)
    await expect(gateway.createRound(stakes, { idempotencyKey: 'original-key' })).rejects.toMatchObject({ code: 'dragon-tiger-invalid-response' })
  })

  it('rejects a mathematically valid result for different wagers', async () => {
    const { gateway } = makeGateway(settled)
    await expect(gateway.createRound({ ...stakes, dragonStake: 1 }, { idempotencyKey: 'original-key' })).rejects.toMatchObject({ code: 'dragon-tiger-invalid-response' })
  })

  it('preserves authoritative rejection details for recovery decisions', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'insufficient-slot-credits', message: 'Insufficient balance.' }, 400))
    const gateway = new HttpDragonTigerGateway(undefined, fetcher)
    await expect(gateway.createRound(stakes, { idempotencyKey: 'original-key' })).rejects.toMatchObject({ code: 'insufficient-slot-credits', status: 400, message: 'Insufficient balance.' })
  })

  it('treats 204 as no previous round', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    const gateway = new HttpDragonTigerGateway(undefined, fetcher)
    await expect(gateway.getLatestRound()).resolves.toBeNull()
  })

  it('rejects unusable table limits before betting', async () => {
    const { gateway } = makeGateway({ available: true, minimumStake: .5, maximumStake: .25, maximumTotalStake: 500, stakeIncrement: .5, balance: 100, mode: 'practice' })
    await expect(gateway.getStatus()).rejects.toMatchObject({ code: 'dragon-tiger-invalid-response' })
  })
})
