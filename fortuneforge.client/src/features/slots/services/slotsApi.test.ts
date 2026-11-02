import { afterEach, describe, expect, it, vi } from 'vitest'
import { requestDemoAvailability, requestDemoSpin } from './slotsApi'

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
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      spinId: 'c80e3ebc-e113-4bb8-9134-690b4db42083',
      gameId: symbolId === '8' ? 'dragon-hoard-v1' : 'wukong-journey-v1',
      reelSetId: 'classic-reels-v4',
      symbolSetId: symbolId === '8' ? 'midnight-vault-symbols-v1' : 'wukong-treasures-v3',
      paytableId: 'classic-paytable-v4',
      wagerPoints: 50,
      pointValueInCents: 100,
      reelStops: [0, 0, 0, 0, 0],
      reels: [[symbolId, '3', '4', '5'], [symbolId, '4', '5', '6'], [symbolId, '5', '6', '7'], ['3', '4', '5', '6'], ['4', '5', '6', '7']],
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
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await requestDemoSpin({
      gameId: symbolId === '8' ? 'dragon-hoard-v1' : 'wukong-journey-v1',
      wagerPoints: 50,
      useFreeSpin: false,
      freeSpinsRemaining: 0,
      freeSpinWagerPoints: null,
      energyBalance: 0,
      sealCollections: [],
      freeSpinFeatureMode: null,
    })

    expect(result.slotsCreditsBalance).toBeNull()
    expect(result.reels[0][0]).toBe(symbolId)
    expect(result.payout.paylines[0].matches[0].match.symbolId).toBe(symbolId)
    expect(fetchMock).toHaveBeenCalledWith('/api/slots/demo/spins', expect.objectContaining({
      method: 'POST',
      credentials: 'omit',
    }))
  })
})
