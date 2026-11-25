import { describe, expect, it, vi } from 'vitest'

import { createJadeGameApi, validateJadeOutcome, validateJadeSpinRequest } from '../src/api'
const fetchWithAccountSession = vi.fn<(path: string, init?: RequestInit) => Promise<Response>>()
const getCurrentAccount = vi.fn<() => Promise<{ userId: string; balances: { slotsCredits: number } }>>()
const ports = { authenticatedFetch: fetchWithAccountSession, loadAccount: getCurrentAccount }

const outcome = () => ({
  gameId: 'empty-slots-v1',
  spinId: 'round-1',
  slotsCreditsBalance: 99.5,
  pointValueInCents: 25,
  payout: { totalPoints: 0, paylines: [] },
  reels: Array.from({ length: 5 }, () => ['S01', 'S07', 'S08', 'S23']),
  freeSpinsRemaining: 0,
  freeSpinWagerPoints: null,
  freeSpinFeatureMode: null,
  specialPointsBalance: 0,
  sealCollections: [],
})

describe('package-owned Jade Dragon paid contract', () => {
  it('fixes the server game ID and caps new paid wagers at R100', () => {
    expect(validateJadeSpinRequest({
      gameId: 'another-game', wagerPoints: 400, useFreeSpin: false, useSpecialBoost: false,
    }).gameId).toBe('empty-slots-v1')
    expect(() => validateJadeSpinRequest({
      wagerPoints: 402, useFreeSpin: false, useSpecialBoost: false,
    })).toThrow()
    expect(() => validateJadeSpinRequest({
      wagerPoints: 3, useFreeSpin: false, useSpecialBoost: false,
    })).toThrow()
  })

  it('allows an existing higher-wager entitlement but never a client boost on a free spin', () => {
    expect(validateJadeSpinRequest({
      wagerPoints: 2000, useFreeSpin: true, useSpecialBoost: false,
    }).wagerPoints).toBe(2000)
    expect(() => validateJadeSpinRequest({
      wagerPoints: 20, useFreeSpin: true, useSpecialBoost: true,
    })).toThrow()
  })

  it('accepts all opaque IDs in four- and six-row server grids', () => {
    expect(validateJadeOutcome(outcome()).reels[0]).toHaveLength(4)
    expect(validateJadeOutcome({
      ...outcome(),
      reels: Array.from({ length: 5 }, () => ['S01', 'S07', 'S08', 'S20', 'S21', 'S23']),
    }).reels[0]).toHaveLength(6)
  })

  it('rejects foreign symbols, dimensions, currency, missing wallet, and invalid win positions', () => {
    for (const patch of [
      { reels: [['dragon']] },
      { pointValueInCents: 100 },
      { slotsCreditsBalance: null },
      { slotsCreditsBalance: -1 },
      { payout: { totalPoints: 1, paylines: [{
        amountPoints: 1,
        matches: [{ match: { positions: [{ reel: 5, row: 0 }] } }],
      }] } },
    ]) expect(() => validateJadeOutcome({ ...outcome(), ...patch })).toThrow()
  })

  it('recovers the same paid spin after reload only through a prepared-spin gateway', async () => {
    const player = 'jade-recover-player'
    let capability = true
    const posts: RequestInit[] = []
    vi.mocked(getCurrentAccount).mockResolvedValue({ userId: player, balances: { slotsCredits: 99.5 } } as Awaited<ReturnType<typeof getCurrentAccount>>)
    vi.mocked(fetchWithAccountSession).mockImplementation(async (_path, init) => {
      if (_path.endsWith('/presentation')) return Response.json({ gameId: 'empty-slots-v1', symbols: [] })
      if (init?.method === 'GET') return Response.json(outcome(), {
        headers: capability ? { 'X-FortuneForge-Prepared-Spin': '1' } : undefined,
      })
      posts.push(init!)
      return posts.length === 1 ? Response.json({ code: 'retry-required' }, { status: 503 })
        : Response.json({ ...outcome(), wagerPoints: 2, isFreeSpin: false })
    })
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ gameId: 'empty-slots-v1', symbols: [] })))

    try {
      const tab = createJadeGameApi(player, ports)
      await tab.loadSession()
      await expect(tab.spin({ wagerPoints: 2, useFreeSpin: false, useSpecialBoost: false })).rejects.toMatchObject({ status: 503 })
      expect(new Headers(posts[0].headers).get('X-FortuneForge-Expected-Account')).toBe(player)
      expect(new Headers(posts[0].headers).get('Idempotency-Key')).toMatch(/^[A-Za-z0-9_-]{16,128}$/)
      capability = false
      await expect(createJadeGameApi(player, ports).loadSession()).rejects.toThrow('saved spin')
      expect(posts).toHaveLength(1)
      capability = true
      await expect(createJadeGameApi(player, ports).loadSession()).resolves.toMatchObject({ balanceCents: 9950 })
      expect(posts).toHaveLength(2)
      expect(posts[1]).toEqual(posts[0])
    } finally {
      vi.mocked(fetchWithAccountSession).mockReset()
      vi.mocked(getCurrentAccount).mockReset()
      vi.unstubAllGlobals()
    }
  })
})
