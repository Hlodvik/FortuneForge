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
      'Demo service availability check failed with status 503.',
    )
  })

  it('rejects an HTML fallback even when it returns 200', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<!doctype html>', {
      status: 200,
      headers: { 'Content-Type': 'text/html' },
    })))

    await expect(requestDemoAvailability('classic-demo-v1')).rejects.toThrow(
      'Demo service availability check failed with status 200.',
    )
  })

  it('omits account credentials and accepts a non-persistent balance result', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      spinId: 'c80e3ebc-e113-4bb8-9134-690b4db42083',
      gameId: 'classic-demo-v1',
      reelSetId: 'classic-reels-v4',
      symbolSetId: 'wukong-treasures-v3',
      paytableId: 'classic-paytable-v4',
      wagerPoints: 50,
      pointValueInCents: 100,
      reelStops: [0],
      reels: [['2']],
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
      payout: { totalPoints: 0, paylines: [] },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await requestDemoSpin({
      gameId: 'classic-demo-v1',
      wagerPoints: 50,
      useFreeSpin: false,
      freeSpinsRemaining: 0,
      freeSpinWagerPoints: null,
      energyBalance: 0,
      sealCollections: [],
      freeSpinFeatureMode: null,
    })

    expect(result.slotsCreditsBalance).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/slots/demo/spins', expect.objectContaining({
      method: 'POST',
      credentials: 'omit',
    }))
  })
})
