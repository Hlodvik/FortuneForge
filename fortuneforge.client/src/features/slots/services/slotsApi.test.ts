import { afterEach, describe, expect, it, vi } from 'vitest'
import { requestDemoSpin } from './slotsApi'

describe('demo slot API', () => {
  afterEach(() => vi.unstubAllGlobals())

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
    })

    expect(result.slotsCreditsBalance).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/slots/demo/spins', expect.objectContaining({
      method: 'POST',
      credentials: 'omit',
    }))
  })
})
