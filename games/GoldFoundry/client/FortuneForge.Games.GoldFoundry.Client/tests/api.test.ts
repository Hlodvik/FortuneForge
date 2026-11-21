import { expect, it } from 'vitest'
import { createGoldFoundryApi } from '../src/api.js'
import { GOLD_FOUNDRY_GAME_ID, GOLD_FOUNDRY_PAYTABLE_ID, GOLD_FOUNDRY_SYMBOL_SET_ID } from '../src/types.js'

it('replays the same funded wager after a lost response and then reads the current wallet balance', async () => {
  const player = 'gold-foundry-recovery-player'
  let rounds = 0
  let failedRefresh = false
  const posts: RequestInit[] = []
  const state = { freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: 0, energyBalance: 0,
    sealCollections: [], freeSpinFeatureMode: null, crucibleWageredCents: 0, cruciblePourValueCents: 0 }
  const result = {
    ...state, gameId: GOLD_FOUNDRY_GAME_ID, spinId: 'saved-gold-spin', wagerPoints: 2, pointValueInCents: 25,
    reelSetId: 'gold-foundry-reels-v1', symbolSetId: GOLD_FOUNDRY_SYMBOL_SET_ID, paytableId: GOLD_FOUNDRY_PAYTABLE_ID,
    reelStops: [0, 0, 0, 0, 0], reels: Array.from({ length: 5 }, () => ['2', '3', '4', '5']),
    payout: { totalPoints: 0, paylines: [] }, consecutiveFiveMisses: 1, fiveMatchPityTriggered: false,
    isFreeSpin: false, freeSpinsAwarded: 0, specialPointsAwarded: 0, energyAwarded: 1,
    energyMultiplierApplied: false, cruciblePayoutCents: 32, cruciblePoured: true, payoutMultiplier: 1,
    monkeyPawCount: 0, moneyGrabPoints: 0, bananaBonusPoints: 0, sealsAwarded: {}, specialBoostApplied: false,
    slotsCreditsBalance: 99.82,
  }
  const transport = async (_path: string, init?: RequestInit) => {
    if (init?.method === 'GET') {
      if (rounds === 2 && !failedRefresh) {
        failedRefresh = true
        return Response.json({ code: 'retry-required' }, { status: 503 })
      }
      return Response.json({ ...state, slotsCreditsBalance: 103 }, {
        headers: { 'X-FortuneForge-Prepared-Spin': '1' },
      })
    }
    posts.push(init!)
    rounds++
    return rounds === 1 ? Response.json({ code: 'retry-required' }, { status: 503 }) : Response.json(result)
  }
  const api = createGoldFoundryApi(transport, player)
  await expect(api.spin({ wagerPoints: 2, state: await api.state() }, false)).rejects.toMatchObject({ status: 503 })
  expect(new Headers(posts[0].headers).get('Idempotency-Key')).toMatch(/^[A-Za-z0-9_-]{16,128}$/)
  expect(new Headers(posts[0].headers).get('X-FortuneForge-Expected-Account')).toBe(player)
  const reloaded = createGoldFoundryApi(transport, player)
  await reloaded.state()
  await expect(reloaded.resumeSpin()).rejects.toMatchObject({ status: 503 })
  expect(reloaded.pendingSpin()).toBeDefined()
  await expect(reloaded.resumeSpin()).resolves.toMatchObject({
    result: { cruciblePayoutCents: 32, wagerPoints: 2, payout: { totalPoints: 0 } },
    state: { slotsCreditsBalance: 103 },
  })
  expect(posts[1]).toEqual(posts[0])
  expect(posts[2]).toEqual(posts[0])
  expect(reloaded.pendingSpin()).toBeUndefined()
})
