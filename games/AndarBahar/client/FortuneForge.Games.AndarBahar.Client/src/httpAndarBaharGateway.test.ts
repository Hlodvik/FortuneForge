import { describe, expect, it, vi } from 'vitest'
import type { AndarBaharCard, AndarBaharRound } from './contracts'
import { HttpAndarBaharGateway, isRound } from './httpAndarBaharGateway'

const key = 'andar-bahar-original-key'
const settled: AndarBaharRound = {
  roundId: 'round-1', balance: 100.45, phase: 'settled',
  joker: { rank: 'ace', suit: 'spades' },
  deals: [{ number: 1, side: 'andar', card: { rank: 'ace', suit: 'hearts' } }],
  winner: 'andar', side: 'andar', stake: .5, disposition: 'win', profit: .45, totalReturn: .95,
}
const status = { available: true, minimumStake: .5, maximumStake: 100, stakeIncrement: .5, balance: 10000, mode: 'development-practice-andar-first' }
const json = (value: unknown, code = 200) => new Response(JSON.stringify(value), { status: code, headers: { 'Content-Type': 'application/json' } })
const makeGateway = (value: unknown) => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => json(value))
  return { gateway: new HttpAndarBaharGateway('/custom/table/', fetcher), fetcher }
}

describe('HttpAndarBaharGateway', () => {
  it('posts only the wager with its original key, cookies, no cache and abort support', async () => {
    const { gateway, fetcher } = makeGateway(settled)
    const signal = new AbortController().signal
    expect(await gateway.createRound('andar', .5, { idempotencyKey: key, signal })).toEqual(settled)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith('/custom/table/rounds', {
      method: 'POST', signal, credentials: 'include', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key },
      body: JSON.stringify({ side: 'andar', stake: .5 }),
    })
    // Native browser fetch rejects invocation with the gateway as its receiver.
    expect(fetcher.mock.contexts[0]).toBeUndefined()
  })

  it('validates and fetches the practice table status', async () => {
    const { gateway, fetcher } = makeGateway(status)
    await expect(gateway.getStatus()).resolves.toEqual(status)
    expect(fetcher).toHaveBeenCalledWith('/custom/table/status', expect.objectContaining({ credentials: 'include', cache: 'no-store' }))
  })

  it('accepts a Bahar win paying 1:1 after the second dealt card', async () => {
    const bahar: AndarBaharRound = {
      ...settled, side: 'bahar', winner: 'bahar', profit: .5, totalReturn: 1,
      deals: [
        { number: 1, side: 'andar', card: { rank: 'king', suit: 'clubs' } },
        { number: 2, side: 'bahar', card: { rank: 'ace', suit: 'diamonds' } },
      ],
    }
    const { gateway } = makeGateway(bahar)
    await expect(gateway.createRound('bahar', .5, { idempotencyKey: key })).resolves.toEqual(bahar)
  })

  it('accepts a lost wager with zero return and negative net profit', async () => {
    const loss: AndarBaharRound = { ...settled, side: 'bahar', disposition: 'loss', profit: -.5, totalReturn: 0 }
    const { gateway } = makeGateway(loss)
    await expect(gateway.createRound('bahar', .5, { idempotencyKey: key })).resolves.toEqual(loss)
  })

  it('accepts the longest possible played prefix of 49 cards', () => {
    const ranks: AndarBaharCard['rank'][] = ['two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'jack', 'queen', 'king']
    const suits: AndarBaharCard['suit'][] = ['clubs', 'diamonds', 'hearts', 'spades']
    const cards = ranks.flatMap(rank => suits.map(suit => ({ rank, suit })))
    const deals = [...cards, { rank: 'ace', suit: 'hearts' } as const].map((card, index) => ({ number: index + 1, side: index % 2 === 0 ? 'andar' as const : 'bahar' as const, card }))
    expect(isRound({ ...settled, deals })).toBe(true)
  })

  it.each([
    ['wrong winner', { ...settled, winner: 'bahar' }],
    ['tie', { ...settled, winner: 'tie' }],
    ['wrong disposition', { ...settled, disposition: 'loss' }],
    ['invented even-money Andar payout', { ...settled, profit: .5, totalReturn: 1 }],
    ['wrong net profit', { ...settled, profit: .95 }],
    ['fractional-cent money', { ...settled, balance: 100.001 }],
    ['unsafe money', { ...settled, balance: Number.MAX_SAFE_INTEGER }],
    ['negative balance', { ...settled, balance: -.01 }],
    ['nonfinite return', { ...settled, totalReturn: Number.POSITIVE_INFINITY }],
    ['unsupported stake', { ...settled, stake: .25 }],
    ['empty deal list', { ...settled, deals: [] }],
    ['noncontiguous numbering', { ...settled, deals: [{ ...settled.deals[0], number: 2 }] }],
    ['Bahar-first ordering', { ...settled, deals: [{ ...settled.deals[0], side: 'bahar' }], winner: 'bahar' }],
    ['unknown rank', { ...settled, joker: { rank: 'joker', suit: 'spades' } }],
    ['unknown suit', { ...settled, joker: { rank: 'ace', suit: 'red' } }],
    ['repeated joker card', { ...settled, deals: [{ number: 1, side: 'andar', card: settled.joker }] }],
    ['nonmatching final rank', { ...settled, deals: [{ number: 1, side: 'andar', card: { rank: 'king', suit: 'hearts' } }] }],
    ['cards after first match', { ...settled, deals: [...settled.deals, { number: 2, side: 'bahar', card: { rank: 'ace', suit: 'clubs' } }] }],
    ['duplicate nonmatching card', { ...settled, deals: [
      { number: 1, side: 'andar', card: { rank: 'king', suit: 'hearts' } },
      { number: 2, side: 'bahar', card: { rank: 'king', suit: 'hearts' } },
      { number: 3, side: 'andar', card: { rank: 'ace', suit: 'hearts' } },
    ] }],
    ['wrong alternating side', { ...settled, deals: [
      { number: 1, side: 'andar', card: { rank: 'king', suit: 'hearts' } },
      { number: 2, side: 'andar', card: { rank: 'ace', suit: 'hearts' } },
    ] }],
    ['more than 49 cards', { ...settled, deals: Array.from({ length: 50 }, (_, index) => ({ ...settled.deals[0], number: index + 1 })) }],
    ['undealt deck on root', { ...settled, deck: [{ rank: 'king', suit: 'clubs' }] }],
    ['undealt card on deal', { ...settled, deals: [{ ...settled.deals[0], nextCard: { rank: 'king', suit: 'clubs' } }] }],
    ['undealt data on card', { ...settled, joker: { ...settled.joker, shoe: ['hidden'] } }],
    ['wrong phase', { ...settled, phase: 'dealing' }],
    ['empty round id', { ...settled, roundId: '' }],
    ['null', null],
    ['array', []],
  ])('rejects a successful HTTP response with %s', async (_reason, payload) => {
    const { gateway } = makeGateway(payload)
    await expect(gateway.createRound('andar', .5, { idempotencyKey: key })).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
  })

  it('rejects valid settlement for a different requested side or stake', async () => {
    const { gateway } = makeGateway(settled)
    await expect(gateway.createRound('bahar', .5, { idempotencyKey: key })).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
    await expect(gateway.createRound('andar', 1, { idempotencyKey: key })).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
  })

  it.each([
    { ...status, maximumStake: .25 },
    { ...status, minimumStake: 101 },
    { ...status, stakeIncrement: .01 },
    { ...status, mode: 'random-start' },
    { ...status, balance: -1 },
    { ...status, nextCard: settled.joker },
  ])('rejects unusable or unknown table status %#', async payload => {
    const { gateway } = makeGateway(payload)
    await expect(gateway.getStatus()).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
  })

  it.each([-.5, 0, .25, 100.5, NaN, Infinity])('rejects invalid wager %s before issuing a request', async stake => {
    const { gateway, fetcher } = makeGateway(settled)
    await expect(gateway.createRound('andar', stake, { idempotencyKey: key })).rejects.toMatchObject({ code: 'andar-bahar-invalid-request' })
    expect(fetcher).not.toHaveBeenCalled()
  })

  it.each(['', 'short-key', 'a key with spaces and length', 'x'.repeat(129)])('requires a supported idempotency key', async idempotencyKey => {
    const { gateway, fetcher } = makeGateway(settled)
    await expect(gateway.createRound('andar', .5, { idempotencyKey })).rejects.toMatchObject({ code: 'andar-bahar-invalid-request' })
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('preserves authoritative rejection details for recovery decisions', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'insufficient-slot-credits', message: 'Insufficient balance.' }, 409))
    const gateway = new HttpAndarBaharGateway(undefined, fetcher)
    await expect(gateway.createRound('andar', .5, { idempotencyKey: key })).rejects.toMatchObject({ code: 'insufficient-slot-credits', status: 409, message: 'Insufficient balance.' })
  })

  it('supports retrying an uncertain request with the same original wager key', async () => {
    const fetcher = vi.fn<typeof fetch>().mockRejectedValueOnce(new TypeError('Connection interrupted')).mockResolvedValueOnce(json(settled))
    const gateway = new HttpAndarBaharGateway(undefined, fetcher)
    await expect(gateway.createRound('andar', .5, { idempotencyKey: key })).rejects.toThrow('Connection interrupted')
    await expect(gateway.createRound('andar', .5, { idempotencyKey: key })).resolves.toEqual(settled)
    expect(fetcher.mock.calls[0][1]).toEqual(fetcher.mock.calls[1][1])
  })

  it('preserves native abort cancellation', async () => {
    const abort = new DOMException('Cancelled', 'AbortError')
    const fetcher = vi.fn<typeof fetch>().mockRejectedValue(abort)
    const gateway = new HttpAndarBaharGateway(undefined, fetcher)
    await expect(gateway.getStatus()).rejects.toBe(abort)
  })

  it('treats HTTP 204 as no previous round, and rejects HTTP 200 null', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(null, { status: 204 })).mockResolvedValueOnce(json(null))
    const gateway = new HttpAndarBaharGateway(undefined, fetcher)
    await expect(gateway.getLatestRound()).resolves.toBeNull()
    await expect(gateway.getLatestRound()).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
  })

  it('restores a settled prefix by latest or exact round id', async () => {
    const { gateway, fetcher } = makeGateway(settled)
    await expect(gateway.getLatestRound()).resolves.toEqual(settled)
    await expect(gateway.getRound('round-1')).resolves.toEqual(settled)
    expect(fetcher.mock.calls.map(call => call[0])).toEqual(['/custom/table/rounds/latest', '/custom/table/rounds/round-1'])
    await expect(gateway.getRound('another-round')).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
  })

  it('rejects an invalid restore reference before making a request', async () => {
    const { gateway, fetcher } = makeGateway(settled)
    await expect(gateway.getRound('../status')).rejects.toMatchObject({ code: 'andar-bahar-invalid-request' })
    expect(fetcher).not.toHaveBeenCalled()
  })

  it('rejects malformed JSON and provides a safe fallback for non-JSON server errors', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response('not json')).mockResolvedValueOnce(new Response('Bad gateway', { status: 502 }))
    const gateway = new HttpAndarBaharGateway(undefined, fetcher)
    await expect(gateway.getLatestRound()).rejects.toMatchObject({ code: 'andar-bahar-invalid-response' })
    await expect(gateway.getStatus()).rejects.toMatchObject({ code: 'andar-bahar-request-failed', status: 502 })
  })
})
