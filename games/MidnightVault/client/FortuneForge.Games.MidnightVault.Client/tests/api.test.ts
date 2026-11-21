import { expect, it } from 'vitest'
import { createMidnightApi } from '../src/api.js'
import { midnightRows } from '../src/roundState.js'
import { MIDNIGHT_GAME_ID, MIDNIGHT_SYMBOL_SET_ID, type MidnightBonusRound, type MidnightState } from '../src/types.js'

it('resumes the original odd-wager legacy bonus spin and reloads the next queued round', async () => {
  const player = 'midnight-recovery-player'
  const old: MidnightBonusRound = { id: 'original-vault', remainingSpins: 1, totalSpins: 7, wagerPoints: 3,
    featureMode: 'sync-rows-paw', mathModelId: 'midnight-vault-paytable-v1' }
  const next: MidnightBonusRound = { id: 'next-vault', remainingSpins: 10, totalSpins: 10, wagerPoints: 40,
    featureMode: null, mathModelId: 'midnight-vault-paytable-v3' }
  const before: MidnightState = { bonusRound: old, queuedBonusRounds: [next], freeSpinsRemaining: 1,
    freeSpinWagerPoints: 3, freeSpinFeatureMode: old.featureMode, specialPointsBalance: 8, energyBalance: 0, sealCollections: [] }
  const after: MidnightState = { ...before, bonusRound: next, queuedBonusRounds: [], freeSpinsRemaining: 10,
    freeSpinWagerPoints: 40, freeSpinFeatureMode: null, slotsCreditsBalance: 72 }
  const result = {
    ...after, gameId: MIDNIGHT_GAME_ID, spinId: 'first-vault-spin', symbolSetId: MIDNIGHT_SYMBOL_SET_ID,
    paytableId: old.mathModelId, reelSetId: 'old-vault-reels', pointValueInCents: 25, wagerPoints: 3,
    reelStops: [0, 0, 0, 0, 0], reels: Array.from({ length: 5 }, () => ['2', '3', '4', '5', '6', '7']),
    payout: { totalPoints: 8, paylines: [] }, consecutiveFiveMisses: 0, fiveMatchPityTriggered: false,
    isFreeSpin: true, freeSpinsAwarded: 0, specialPointsAwarded: 0, energyAwarded: 0,
    energyMultiplierApplied: false, payoutMultiplier: 1, monkeyPawCount: 0, moneyGrabPoints: 0,
    bananaBonusPoints: 0, sealsAwarded: {}, specialBoostApplied: false, chargedWagerCents: 0,
  }
  const posts: RequestInit[] = []
  const transport = async (_path: string, init?: RequestInit) => {
    if (init?.method === 'GET') return Response.json(posts.length > 1 ? after : before, {
      headers: { 'X-FortuneForge-Prepared-Spin': '1' },
    })
    posts.push(init!)
    return posts.length === 1 ? Response.json({ code: 'retry-required' }, { status: 503 }) : Response.json(result)
  }
  expect(midnightRows(before)).toBe(6)
  expect(midnightRows(after)).toBe(4)
  const api = createMidnightApi(transport, player)
  await expect(api.spin({ wagerPoints: 3, useFreeSpin: true, state: await api.state(), demoState: null }, false))
    .rejects.toMatchObject({ status: 503 })
  const reloaded = createMidnightApi(transport, player)
  await reloaded.state()
  await expect(reloaded.resumeSpin()).resolves.toMatchObject({
    result: { isFreeSpin: true, wagerPoints: 3, paytableId: old.mathModelId, chargedWagerCents: 0 },
    state: { bonusRound: next, freeSpinWagerPoints: 40, slotsCreditsBalance: 72 },
  })
  expect(new Headers(posts[0].headers).get('X-FortuneForge-Expected-Account')).toBe(player)
  expect(new Headers(posts[0].headers).get('Idempotency-Key')).toMatch(/^[A-Za-z0-9_-]{16,128}$/)
  expect(posts[1]).toEqual(posts[0])
})
