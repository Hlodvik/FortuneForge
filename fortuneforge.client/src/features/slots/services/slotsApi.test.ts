// Edited: 2026-11-18T19:58:40-06:00 | Computer: Vinnabok
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GODS_OF_OLYMPUS_SLOT_GAME } from '@fortuneforge/games-gods-of-olympus/manifest'
import type { SpinResult } from '../types/slots'
import { requestDemoAvailability, requestDemoSpin } from './slotsApi'

const demoRun = {
  wagerPoints: 50,
  useFreeSpin: false,
  freeSpinsRemaining: 0,
  freeSpinWagerPoints: null,
  energyBalance: 0,
  sealCollections: [],
  freeSpinFeatureMode: null,
}

function demoResult(result: Pick<SpinResult, 'gameId' | 'reelSetId' | 'symbolSetId' | 'paytableId' | 'reels' | 'payout' | 'olympusTumble'>): Response {
  return Response.json({
    ...result,
    spinId: 'c80e3ebc-e113-4bb8-9134-690b4db42083',
    wagerPoints: demoRun.wagerPoints,
    pointValueInCents: 100,
    reelStops: result.reels.map(() => 0),
    consecutiveFiveMisses: 1,
    fiveMatchPityTriggered: false,
    isFreeSpin: false,
    freeSpinsAwarded: 0,
    freeSpinsRemaining: 0,
    freeSpinWagerPoints: null,
    specialPointsAwarded: 0,
    specialPointsBalance: 0,
    energyAwarded: 0,
    energyBalance: 0,
    energyMultiplierApplied: false,
    payoutMultiplier: 1,
    monkeyPawCount: 0,
    moneyGrabPoints: 0,
    bananaBonusPoints: 0,
    sealsAwarded: {},
    sealCollections: [],
    freeSpinFeatureMode: null,
    specialBoostApplied: false,
    slotsCreditsBalance: null,
  })
}

describe('demo slot API', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('checks availability without credentials or cached responses', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await requestDemoAvailability('rainbow-realm-fruits-v1')

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/slots/demo/status?gameId=rainbow-realm-fruits-v1',
      {
        method: 'GET',
        credentials: 'omit',
        cache: 'no-store',
        signal: undefined,
      },
    )
  })

  it('rejects an unavailable demo service', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))

    await expect(requestDemoAvailability('missing-game')).rejects.toThrow(
      'Game service availability check failed with status 503.',
    )
  })

  it('rejects an HTML fallback even when it returns 200', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<!doctype html>', {
      status: 200,
      headers: { 'Content-Type': 'text/html' },
    })))

    await expect(requestDemoAvailability('wukong-journey-v1')).rejects.toThrow(
      'Game service availability check failed with status 200.',
    )
  })

  it.each(['2', '8'] as const)('omits account credentials and accepts reel and paid-match symbol %s', async (symbolId) => {
    const gameId = symbolId === '8' ? 'dragon-hoard-v1' : 'wukong-journey-v1'
    const fetchMock = vi.fn().mockResolvedValue(demoResult({
      gameId,
      reelSetId: 'classic-reels-v4',
      symbolSetId: symbolId === '8' ? 'midnight-vault-symbols-v1' : 'classic-symbols-v3',
      paytableId: 'classic-paytable-v4',
      reels: [[symbolId, '3', '4', '5'], [symbolId, '4', '5', '6'], [symbolId, '5', '6', '7'], ['3', '4', '5', '6'], ['4', '5', '6', '7']],
      payout: {
        totalPoints: 200,
        paylines: [{
          paylineId: 1, amountPoints: 200,
          matches: [{
            match: { paylineId: 1, symbolId, matchLength: 3,
              positions: [{ reel: 0, row: 0 }, { reel: 1, row: 0 }, { reel: 2, row: 0 }], wildPositions: [] },
            multiplier: 4, amountPoints: 200,
          }],
        }],
      },
    }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await requestDemoSpin({ ...demoRun, gameId })

    expect(result.slotsCreditsBalance).toBeNull()
    expect(result.reels[0][0]).toBe(symbolId)
    expect(result.payout.paylines[0].matches[0].match.symbolId).toBe(symbolId)
    expect(fetchMock).toHaveBeenCalledWith('/api/slots/demo/spins', expect.objectContaining({
      method: 'POST',
      credentials: 'omit',
    }))
  })

  it.each([
    { starting: ['X3'], refill: 'X5', multiplier: 8, accepted: true },
    { starting: ['X10', 'X10'], refill: 'X10', multiplier: 25, accepted: true },
    { starting: ['X3'], refill: 'X5', multiplier: 7, accepted: false },
  ])('validates Olympus multiplier $multiplier with the selected tumble rules', async ({ starting, refill, multiplier, accepted }) => {
    const { gameId, tumble } = GODS_OF_OLYMPUS_SLOT_GAME.experience.rules
    if (!tumble) throw new Error('Olympus must supply its tumble rules.')

    const initialReels = tumble.initialReels.map(reel => [...reel])
    const positions = Array.from({ length: 8 }, (_, index) => ({
      reel: Math.floor(index / tumble.rows),
      row: index % tumble.rows,
    }))
    for (const { reel, row } of positions) initialReels[reel][row] = '2'
    starting.forEach((symbol, row) => { initialReels[2][row] = symbol })
    const reels = initialReels.map(reel => [...reel])
    for (const { reel, row } of positions) reels[reel][row] = 'J'
    reels[0][0] = refill

    const baseTotalPoints = 12
    const totalPoints = baseTotalPoints * multiplier
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(demoResult({
      gameId,
      reelSetId: 'olympus-tumble-reels',
      symbolSetId: 'olympus-tumble-symbols',
      paytableId: 'olympus-tumble-paytable',
      reels: initialReels,
      payout: { totalPoints, paylines: [] },
      olympusTumble: {
        initialReels,
        steps: [{
          reels,
          amountPoints: baseTotalPoints,
          awards: [{ symbol: '2', amountPoints: baseTotalPoints, positions }],
          cleared: positions,
          dropRows: reels.map(reel => reel.map(() => 0)),
        }],
        totalPoints,
        baseTotalPoints,
        multiplier,
      },
    })))

    const result = requestDemoSpin({ ...demoRun, gameId, olympusTumbleShowcase: false }, tumble)
    if (accepted) await expect(result).resolves.toMatchObject({ payout: { totalPoints } })
    else await expect(result).rejects.toThrow('The spin server returned an invalid Olympus tumble result.')
  })
})
