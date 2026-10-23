import { describe, expect, it, vi } from 'vitest'
import type { MinesCommand, MinesSession } from './contracts'
import { HttpMinesGateway, isMinesSession } from './httpMinesGateway'

const idle = (): MinesSession => ({
  balanceCents: 100_000, practice: true, round: null,
  minimumWagerCents: 10, maximumWagerCents: 10_000, houseEdgeBasisPoints: 100,
})
const active = (): MinesSession => ({
  ...idle(), balanceCents: 99_000,
  round: {
    roundId: 'mines-round-0001', wagerCents: 1000, mineCount: 3, status: 'active',
    safeReveals: 1, gemsRemaining: 21, currentMultiplier: 1.125, nextMultiplier: 9 / 7,
    cashOutCents: 1125, canCashOut: true, payoutCents: 0,
    tiles: Array.from({ length: 25 }, (_, index) => ({
      index, isRevealed: index === 6, kind: index === 6 ? 'gem' : 'hidden',
    })),
  },
})
const terminal = (status: 'lost' | 'cashed-out' | 'cleared'): MinesSession => {
  const session = active()
  const round = session.round!
  const cleared = status === 'cleared'
  const safeReveals = cleared ? 22 : 1
  const payout = status === 'lost' ? 0 : cleared ? 2_277_000 : 1125
  return {
    ...session, balanceCents: 99_000 + payout,
    round: {
      ...round, status, safeReveals, gemsRemaining: 22 - safeReveals,
      currentMultiplier: status === 'lost' ? 0 : cleared ? 2277 : 1.125,
      nextMultiplier: null, canCashOut: false, payoutCents: payout,
      cashOutCents: 0,
      tiles: Array.from({ length: 25 }, (_, index) => ({
        index, kind: index < 3 ? 'mine' : 'gem',
        isRevealed: cleared ? index >= 3 : index === 6 || status === 'lost' && index === 0,
      })),
    },
  }
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json' },
})
const setup = (value: unknown) => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json(value))
  return { gateway: new HttpMinesGateway('/custom/mines/', fetcher), fetcher }
}
const changedRound = (change: Record<string, unknown>) => ({ ...active(), round: { ...active().round, ...change } })

describe('HttpMinesGateway', () => {
  it('gets the authoritative session using same-origin credentials and the caller signal', async () => {
    const { gateway, fetcher } = setup(idle())
    const signal = new AbortController().signal
    await expect(gateway.getSession(signal)).resolves.toEqual(idle())
    expect(fetcher).toHaveBeenCalledWith('/custom/mines/session', { credentials: 'same-origin', signal })
    expect(fetcher.mock.contexts[0]).toBeUndefined()
  })

  it.each<MinesCommand>([
    { action: 'start', requestId: 'mines-original-0001', wagerCents: 1000, mineCount: 3 },
    { action: 'reveal', requestId: 'mines-original-0002', roundId: 'round /?1', tile: 6 },
    { action: 'cashout', requestId: 'mines-original-0003', roundId: 'round /?1' },
  ])('preserves the exact $action payload, path, and requestId after an interrupted response', async command => {
    const { gateway, fetcher } = setup(active())
    fetcher.mockRejectedValueOnce(new TypeError('Network disconnected'))
    await expect(gateway.execute(command)).rejects.toMatchObject({ code: 'mines-unavailable', status: 0 })
    await expect(gateway.execute(command)).resolves.toEqual(active())
    const { action } = command
    const payload = action === 'start' ? { requestId: command.requestId, wagerCents: command.wagerCents, mineCount: command.mineCount }
      : action === 'reveal' ? { requestId: command.requestId, tile: command.tile } : { requestId: command.requestId }
    const path = action === 'start' ? '/rounds' : `/rounds/${encodeURIComponent(command.roundId)}/${action}`
    const expected = [`/custom/mines${path}`, {
      credentials: 'same-origin', method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    }]
    expect(fetcher.mock.calls).toEqual([expected, expected])
    expect(JSON.parse(fetcher.mock.calls[1][1]!.body as string)).not.toHaveProperty('action')
  })

  it.each(['lost', 'cashed-out', 'cleared'] as const)('accepts a private-free terminal %s board', async status => {
    const value = terminal(status)
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
  })

  it.each([
    ['missing session', undefined],
    ['string balance', { ...idle(), balanceCents: '100000' }],
    ['negative balance', { ...idle(), balanceCents: -1 }],
    ['unsafe balance', { ...idle(), balanceCents: Number.MAX_SAFE_INTEGER + 1 }],
    ['fractional cents', { ...idle(), balanceCents: 100.5 }],
    ['missing practice marker', { ...idle(), practice: undefined }],
    ['string practice marker', { ...idle(), practice: 'true' }],
    ['reversed limits', { ...idle(), minimumWagerCents: 200, maximumWagerCents: 100 }],
    ['zero minimum', { ...idle(), minimumWagerCents: 0 }],
    ['undisclosed edge', { ...idle(), houseEdgeBasisPoints: 0 }],
    ['missing round', { ...idle(), round: undefined }],
    ['unknown round status', changedRound({ status: 'won' })],
    ['zero wager', changedRound({ wagerCents: 0 })],
    ['fractional mine count', changedRound({ mineCount: 3.5 })],
    ['zero mines', changedRound({ mineCount: 0 })],
    ['too many mines', changedRound({ mineCount: 25 })],
    ['wrong remaining count', changedRound({ gemsRemaining: 20 })],
    ['invalid multiplier', changedRound({ currentMultiplier: -1 })],
    ['invalid next multiplier', changedRound({ nextMultiplier: 0 })],
    ['negative payout', changedRound({ payoutCents: -1 })],
    ['cash-out unavailable after a safe reveal', changedRound({ canCashOut: false })],
    ['wrong tile count', changedRound({ tiles: active().round!.tiles.slice(1) })],
    ['duplicate tile index', changedRound({ tiles: active().round!.tiles.map((t, i) => i === 0 ? { ...t, index: 1 } : t) })],
    ['wrong revealed gem count', changedRound({ tiles: active().round!.tiles.map(t => ({ ...t, kind: 'hidden', isRevealed: false })) })],
    ['revealed hidden tile', changedRound({ tiles: active().round!.tiles.map((t, i) => i === 0 ? { ...t, isRevealed: true } : t) })],
    ['unrevealed gem exposure', changedRound({ tiles: active().round!.tiles.map((t, i) => i === 0 ? { ...t, kind: 'gem' } : t) })],
    ['unrevealed mine exposure', changedRound({ tiles: active().round!.tiles.map((t, i) => i === 0 ? { ...t, kind: 'mine' } : t) })],
    ['paid active round', changedRound({ payoutCents: 1125 })],
    ['coerced terminal status', { ...terminal('cashed-out'), round: { ...terminal('cashed-out').round, status: ['cashed-out'] } }],
    ['unfinished terminal board', { ...terminal('cashed-out'), round: { ...terminal('cashed-out').round, tiles: active().round!.tiles } }],
  ])('rejects successful HTTP responses with %s', async (_reason, value) => {
    const { gateway } = setup(value)
    await expect(gateway.getSession()).rejects.toMatchObject({ code: 'mines-invalid-response' })
  })

  it.each([
    ['mineMask', 0b111], ['mineIndices', [0, 1, 2]], ['mines', [0, 1, 2]],
    ['board', Array.from({ length: 25 }, (_, i) => i < 3)], ['nonce', 'secret-server-nonce'],
  ])('rejects private active-round %s data instead of passing it to game UI', async (field, value) => {
    await expect(setup(changedRound({ [field]: value })).gateway.getSession()).rejects.toMatchObject({ code: 'mines-invalid-response' })
  })

  it.each([
    ['lost without the selected mine', { ...terminal('lost'), round: { ...terminal('lost').round,
      tiles: terminal('lost').round!.tiles.map(tile => tile.kind === 'mine' ? { ...tile, isRevealed: false } : tile),
    } }],
    ['won cash-out with no payout', { ...terminal('cashed-out'), round: { ...terminal('cashed-out').round, payoutCents: 0 } }],
    ['cleared board with a selected mine', { ...terminal('cleared'), round: { ...terminal('cleared').round,
      tiles: terminal('cleared').round!.tiles.map((tile, i) => i === 0 ? { ...tile, isRevealed: true } : tile),
    } }],
  ])('rejects an impossible %s state', async (_reason, value) => {
    await expect(setup(value).gateway.getSession()).rejects.toMatchObject({ code: 'mines-invalid-response' })
  })

  it.each([400, 401, 403, 409, 429, 500, 503])('preserves HTTP %s backend errors without inventing wallet or game state', async status => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({
      code: 'mines-authoritative-error', message: 'Original server rejection.',
    }, status))
    const gateway = new HttpMinesGateway(undefined, fetcher)
    await expect(gateway.execute({ action: 'start', requestId: 'mines-failed-0001', wagerCents: 1000, mineCount: 3 }))
      .rejects.toMatchObject({ code: 'mines-authoritative-error', message: 'Original server rejection.', status })
    expect(fetcher).toHaveBeenCalledOnce()
  })

  it('reports an unparseable error body as an error with its status', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response('<html>maintenance</html>', { status: 503 }))
    await expect(new HttpMinesGateway(undefined, fetcher).getSession()).rejects.toMatchObject({ status: 503 })
  })

  it('rejects an empty successful response rather than using practice money', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    await expect(new HttpMinesGateway(undefined, fetcher).getSession()).rejects.toMatchObject({ code: 'mines-invalid-response' })
  })
})

describe('isMinesSession', () => {
  it('accepts an unopened paid board while every tile remains hidden', () => {
    const session = active()
    const unopened = { ...session, round: { ...session.round!, safeReveals: 0, gemsRemaining: 22,
      currentMultiplier: 1, nextMultiplier: 1.125, cashOutCents: 0, canCashOut: false,
      tiles: session.round!.tiles.map(tile => ({ ...tile, kind: 'hidden', isRevealed: false })),
    } }
    expect(isMinesSession(unopened)).toBe(true)
  })
})
