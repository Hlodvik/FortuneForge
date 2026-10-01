import { describe, expect, it, vi } from 'vitest'
import { RouletteGatewayError, type RouletteRound, type RouletteStatus } from './contracts'
import { HttpRouletteGateway } from './httpRouletteGateway'
import { makeBet } from './roulettePresentation'

const roundId = '00000000-0000-4000-8000-000000000001'
const status: RouletteStatus = { available: true, minimumStake: 1, maximumStake: 100, stakeIncrement: 1, startingBalance: 1_000, mode: 'free-play-single-zero' }
const open: RouletteRound = { roundId, balance: 990, phase: 'open', bets: [{ ...makeBet('straight', [17], 10), betIndex: 0, playerId: 'player' }], winningPocket: null, settlements: [] }
const settled: RouletteRound = { ...open, balance: 1_350, phase: 'settled', winningPocket: 17, settlements: [{ playerId: 'player', kind: 'straight', stake: 10, won: true, totalReturn: 360 }] }
const response = (value: unknown, code = 200) => new Response(JSON.stringify(value), { status: code, headers: { 'content-type': 'application/json' } })

describe('HttpRouletteGateway recovery contract', () => {
  it('reads an existing round once with an encoded identifier and the caller cancellation signal', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(open))
    const signal = new AbortController().signal
    await new HttpRouletteGateway('/table/', fetcher).getRound(roundId, signal)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/table/rounds/${roundId}`, { signal })
  })

  it('posts the exact bet shape without adding an unsupported idempotency contract', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(open))
    const bet = makeBet('red', [], 5)
    await new HttpRouletteGateway(undefined, fetcher).placeBet(roundId, bet)
    expect(fetcher).toHaveBeenCalledExactlyOnceWith(`/api/games/roulette/rounds/${roundId}/bets`, {
      method: 'POST', signal: undefined, headers: { 'content-type': 'application/json' }, body: JSON.stringify(bet),
    })
  })

  it('does not replay a mutation when its response is lost', async () => {
    const fetcher = vi.fn().mockRejectedValue(new TypeError('Connection lost'))
    await expect(new HttpRouletteGateway(undefined, fetcher).removeBet(roundId, 0)).rejects.toThrow('Connection lost')
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('preserves service errors and cancellation so the controller can distinguish recovery paths', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ code: 'roulette-round-not-found', message: 'That table expired.' }, 404))
    await expect(new HttpRouletteGateway(undefined, fetcher).getRound(roundId)).rejects.toMatchObject({ name: 'RouletteGatewayError', code: 'roulette-round-not-found', status: 404, message: 'That table expired.' })
    const aborted = new DOMException('Cancelled', 'AbortError')
    const cancelled = vi.fn().mockRejectedValue(aborted)
    await expect(new HttpRouletteGateway(undefined, cancelled).spin(roundId)).rejects.toBe(aborted)
  })

  it.each([
    { ...status, minimumStake: 0 }, { ...status, maximumStake: .5 },
    { ...status, stakeIncrement: 0 }, { ...status, startingBalance: -1 },
    { ...status, minimumStake: NaN },
  ])('rejects invalid table limits before allowing chip controls', async value => {
    const fetcher = vi.fn().mockResolvedValue(response(value))
    await expect(new HttpRouletteGateway(undefined, fetcher).getStatus()).rejects.toBeInstanceOf(RouletteGatewayError)
  })

  it('accepts a well formed settled round without recomputing its server payout', async () => {
    const fetcher = vi.fn().mockResolvedValue(response(settled))
    await expect(new HttpRouletteGateway(undefined, fetcher).getRound(roundId)).resolves.toEqual(settled)
  })

  it.each([
    { ...open, winningPocket: 17 },
    { ...open, settlements: settled.settlements },
    { ...settled, winningPocket: null },
    { ...settled, settlements: [] },
    { ...open, bets: [{ ...open.bets[0], betIndex: -1 }] },
  ])('rejects contradictory recovery states rather than reopening a settled or malformed table', async value => {
    const fetcher = vi.fn().mockResolvedValue(response(value))
    await expect(new HttpRouletteGateway(undefined, fetcher).getRound(roundId)).rejects.toBeInstanceOf(RouletteGatewayError)
  })

  it('rejects recovery of a different round than requested', async () => {
    const fetcher = vi.fn().mockResolvedValue(response({ ...open, roundId: '00000000-0000-4000-8000-000000000002' }))
    await expect(new HttpRouletteGateway(undefined, fetcher).getRound(roundId)).rejects.toBeInstanceOf(RouletteGatewayError)
  })
})
