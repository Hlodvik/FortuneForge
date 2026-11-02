import { describe, expect, it, vi } from 'vitest'
import type { MinesCommand, MinesSession } from './contracts'
import { HttpMinesGateway, isMinesSession } from './httpMinesGateway'

const idle = (): MinesSession => ({
  balanceCents: 100_000, practice: true, round: null,
  minimumWagerCents: 100, maximumWagerCents: 500, houseEdgeBasisPoints: 1000, payoutPolicyVersion: 2,
})
const active = (): MinesSession => ({
  ...idle(), balanceCents: 99_000,
  round: {
    roundId: 'mines-round-0001', wagerCents: 1000, mineCount: 3, status: 'active',
    safeReveals: 1, gemsRemaining: 21, currentMultiplier: 45 / 44, nextMultiplier: 90 / 77,
    cashOutCents: 1022, canCashOut: true, payoutCents: 0, houseEdgeBasisPoints: 1000,
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
  const payout = status === 'lost' ? 0 : cleared ? 2_070_000 : 1022
  return {
    ...session, balanceCents: 99_000 + payout,
    round: {
      ...round, status, safeReveals, gemsRemaining: 22 - safeReveals,
      currentMultiplier: status === 'lost' ? 0 : cleared ? 2070 : 45 / 44,
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
const lateV2 = (): MinesSession => ({ ...idle(), balanceCents: 9900, round: {
  ...active().round!, wagerCents: 100, mineCount: 21, safeReveals: 2, gemsRemaining: 2,
  currentMultiplier: 45, nextMultiplier: 47.5, cashOutCents: 4500,
  payoutPolicyVersion: 2, nextMinePayoutCents: 4476,
  tiles: Array.from({ length: 25 }, (_, index) => ({ index, isRevealed: index === 21 || index === 22,
    kind: index === 21 || index === 22 ? 'gem' : 'hidden' })),
} })
const consolationV2 = (): MinesSession => {
  const session = lateV2()
  return { ...session, balanceCents: 14376, round: { ...session.round!, status: 'consolation',
    currentMultiplier: 940 / 21, nextMultiplier: null, cashOutCents: 0, canCashOut: false,
    payoutCents: 4476, nextMinePayoutCents: 0,
    tiles: session.round!.tiles.map(tile => ({ ...tile, kind: tile.index < 21 ? 'mine' : 'gem',
      isRevealed: tile.isRevealed || tile.index === 0 })),
  } }
}

describe('HttpMinesGateway', () => {
  it('accepts full-board v2 late play and its exact server-provided next-mine disclosure', async () => {
    await expect(setup(lateV2()).gateway.getSession()).resolves.toEqual(lateV2())
  })

  it('accepts one terminal v2 consolation payment with exactly one revealed mine', async () => {
    await expect(setup(consolationV2()).gateway.getSession()).resolves.toEqual(consolationV2())
  })

  it('accepts the complete v2 clear at 50× without an early terminal shortcut', async () => {
    const session = lateV2()
    const value = { ...session, round: { ...session.round!, status: 'cleared', safeReveals: 4,
      gemsRemaining: 0, currentMultiplier: 50, nextMultiplier: null, cashOutCents: 0, canCashOut: false,
      payoutCents: 5000, nextMinePayoutCents: 0, tiles: session.round!.tiles.map(tile => ({ ...tile,
        kind: tile.index < 21 ? 'mine' : 'gem', isRevealed: tile.index >= 21 })),
    } }
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
  })

  it.each([
    ['unknown session policy', { ...lateV2(), payoutPolicyVersion: 3 }],
    ['unsupported v2 session edge', { ...lateV2(), houseEdgeBasisPoints: 100 }],
    ['incorrect v2 session minimum', { ...lateV2(), minimumWagerCents: 99 }],
    ['incorrect v2 session maximum', { ...lateV2(), maximumWagerCents: 501 }],
    ['unsupported v2 round edge', { ...lateV2(), round: { ...lateV2().round, houseEdgeBasisPoints: 100 } }],
    ['missing v2 round edge', { ...lateV2(), round: { ...lateV2().round, houseEdgeBasisPoints: undefined } }],
    ['below-current v2 wager', { ...lateV2(), round: { ...lateV2().round, wagerCents: 99 } }],
    ['above-current v2 wager', { ...lateV2(), round: { ...lateV2().round, wagerCents: 501 } }],
    ['null session policy', { ...lateV2(), payoutPolicyVersion: null }],
    ['unknown round policy', { ...lateV2(), round: { ...lateV2().round, payoutPolicyVersion: 3 } }],
    ['null round policy', { ...lateV2(), round: { ...lateV2().round, payoutPolicyVersion: null } }],
    ['missing v2 next-mine disclosure', { ...lateV2(), round: { ...lateV2().round, nextMinePayoutCents: undefined } }],
    ['negative next-mine money', { ...lateV2(), round: { ...lateV2().round, nextMinePayoutCents: -1 } }],
    ['fractional next-mine money', { ...lateV2(), round: { ...lateV2().round, nextMinePayoutCents: 1.5 } }],
    ['over-ceiling next quote', { ...lateV2(), round: { ...lateV2().round, nextMultiplier: 50.01 } }],
    ['over-ceiling current quote', { ...lateV2(), round: { ...lateV2().round, currentMultiplier: 50.01 } }],
    ['over-ceiling mine return', { ...lateV2(), round: { ...lateV2().round, nextMinePayoutCents: 5001 } }],
    ['over-ceiling payout', { ...consolationV2(), round: { ...consolationV2().round, payoutCents: 5001 } }],
    ['zero consolation payout', { ...consolationV2(), round: { ...consolationV2().round, payoutCents: 0 } }],
    ['terminal next-mine payment', { ...consolationV2(), round: { ...consolationV2().round, nextMinePayoutCents: 10 } }],
    ['legacy consolation status', { ...consolationV2(), round: { ...consolationV2().round, payoutPolicyVersion: 1 } }],
    ['unselected consolation mine', { ...consolationV2(), round: { ...consolationV2().round,
      tiles: consolationV2().round!.tiles.map(tile => tile.kind === 'mine' ? { ...tile, isRevealed: false } : tile) } }],
    ['private v2 board', { ...lateV2(), round: { ...lateV2().round, mineIndices: [0, 1] } }],
  ])('rejects malformed or private %s data', async (_name, value) => {
    await expect(setup(value).gateway.getSession()).rejects.toMatchObject({ code: 'mines-invalid-response' })
  })

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

  it.each([undefined, 100])('preserves a legacy 99% receipt (%s metadata) while the table advertises 90% for new wagers', async edge => {
    const session = active()
    const { houseEdgeBasisPoints: _edge, ...oldRound } = session.round!
    const value = { ...session, round: { ...oldRound, currentMultiplier: 1.125,
      nextMultiplier: 9 / 7, cashOutCents: 1125, ...(edge === undefined ? {} : { houseEdgeBasisPoints: edge }) } }
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
    expect(value.houseEdgeBasisPoints).toBe(1000)
  })

  it('accepts a complete legacy session from an older replay receipt unchanged', async () => {
    const session = active()
    const { houseEdgeBasisPoints: _edge, ...oldRound } = session.round!
    const value = { ...session, houseEdgeBasisPoints: 100, payoutPolicyVersion: 1, round: { ...oldRound,
      currentMultiplier: 1.125, nextMultiplier: 9 / 7, cashOutCents: 1125 } }
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
  })

  it.each([undefined, 1])('preserves missing/policy-%s legacy session limits as an accepted snapshot', async policy => {
    const value = { ...idle(), minimumWagerCents: 10, maximumWagerCents: 10_000,
      ...(policy === undefined ? {} : { payoutPolicyVersion: policy }) }
    if (policy === undefined) delete value.payoutPolicyVersion
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
  })

  it.each([undefined, 100, 1000])('resumes an accepted R100 round (%s edge) under current R1–R5 new-wager limits', async edge => {
    const current = active()
    const { houseEdgeBasisPoints: _edge, ...accepted } = current.round!
    const value = { ...current, round: { ...accepted, wagerCents: 10_000,
      cashOutCents: edge === 1000 ? 10227 : 11250,
      currentMultiplier: edge === 1000 ? 45 / 44 : 1.125,
      nextMultiplier: edge === 1000 ? 90 / 77 : 9 / 7,
      ...(edge === undefined ? {} : { houseEdgeBasisPoints: edge }) } }
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
    expect(value.minimumWagerCents).toBe(100)
    expect(value.maximumWagerCents).toBe(500)
  })

  it.each([10, 99, 501, 10_000])('accepts historical %s-cent rounds outside new-wager bounds without repricing their response', async wagerCents => {
    const value = changedRound({ wagerCents })
    await expect(setup(value).gateway.getSession()).resolves.toEqual(value)
  })

  it('does not impose the new 50× ceiling on a missing-policy legacy active response', async () => {
    const value = changedRound({ mineCount: 12, safeReveals: 12, gemsRemaining: 1,
      currentMultiplier: 360020.76923076925, nextMultiplier: 4680270,
      cashOutCents: 360020769,
      tiles: active().round!.tiles.map(tile => ({ ...tile, kind: tile.index < 12 ? 'gem' : 'hidden', isRevealed: tile.index < 12 })),
    })
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
    ['unsupported table edge', { ...idle(), houseEdgeBasisPoints: 900 }],
    ['string table edge', { ...idle(), houseEdgeBasisPoints: '1000' }],
    ['unsupported frozen round edge', changedRound({ houseEdgeBasisPoints: 900 })],
    ['null frozen round edge', changedRound({ houseEdgeBasisPoints: null })],
    ['string frozen round edge', changedRound({ houseEdgeBasisPoints: '1000' })],
    ['missing round', { ...idle(), round: undefined }],
    ['unknown round status', changedRound({ status: 'won' })],
    ['zero wager', changedRound({ wagerCents: 0 })],
    ['below historical accepted wager', changedRound({ wagerCents: 9 })],
    ['above historical accepted wager', changedRound({ wagerCents: 10_001 })],
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
    ['paid active round', changedRound({ payoutCents: 1022 })],
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
      currentMultiplier: 1, nextMultiplier: 45 / 44, cashOutCents: 0, canCashOut: false,
      tiles: session.round!.tiles.map(tile => ({ ...tile, kind: 'hidden', isRevealed: false })),
    } }
    expect(isMinesSession(unopened)).toBe(true)
  })
})
