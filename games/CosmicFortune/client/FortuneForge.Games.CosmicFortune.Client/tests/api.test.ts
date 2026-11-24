// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPharaohApi, decodePharaohSlotState, decodePharaohSpinResult } from '../src/api'
import { jsonReply, slotState, spinResult, tombRun, tombState } from './fixtures'

let counter = 0
const account = () => `pharaoh-api-test-${++counter}`
beforeEach(() => sessionStorage.clear())

describe('standalone Pharaoh paid transport', () => {
  it.runIf(process.env.PHARAOH_CONTRACT_URL)('decodes the live local production state without placing a wager', async () => {
    const target = new URL(process.env.PHARAOH_CONTRACT_URL!)
    expect(['localhost', '127.0.0.1']).toContain(target.hostname)
    const response = await fetch(target)
    expect(response.ok).toBe(true)
    expect(decodePharaohSlotState(await response.json())).not.toBeNull()
  })
  it('requires both authenticated transport and account identity', async () => {
    const fetch = vi.fn()
    await expect(createPharaohApi(fetch).getState()).rejects.toThrow(/Sign in/)
    expect(fetch).not.toHaveBeenCalled()
    expect(() => createPharaohApi(undefined, account()).spin(12, false)).toThrow(/Sign in/)
  })

  it('rejects conflicting state, unsafe payouts, unknown symbols and wrong game identities', () => {
    expect(decodePharaohSlotState(slotState())).not.toBeNull()
    expect(decodePharaohSlotState({ ...slotState(), featureActionRequired: true })).toBeNull()
    expect(decodePharaohSpinResult(spinResult())).not.toBeNull()
    for (const result of [
      { ...spinResult(), gameId: 'another-game' }, { ...spinResult(), pointValueInCents: 1 },
      { ...spinResult(), chargedWagerCents: 0 }, { ...spinResult(), isFreeSpin: true, chargedWagerCents: 300 },
      { ...spinResult(), reels: Array(5).fill(['UNKNOWN', '2', '3', '4']) },
      { ...spinResult(), payout: { totalPoints: 5, paylines: [] } },
    ]) expect(decodePharaohSpinResult(result)).toBeNull()
  })

  it('admits new paid wagers only inside the current configured picker', async () => {
    const fetch = vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
      if (String(path).includes('/state?')) return jsonReply(slotState())
      const body = JSON.parse(String(init!.body)) as { wagerPoints: number }
      return jsonReply({ ...spinResult(), wagerPoints: body.wagerPoints, chargedWagerCents: body.wagerPoints * 25 })
    })
    const api = createPharaohApi(fetch, account())
    await api.getState()
    for (const invalid of [0, 1, 3, 302, 2000]) expect(() => api.spin(invalid, false)).toThrow('Choose an available wager')
    expect(fetch).toHaveBeenCalledTimes(1)
    await expect(api.spin(2, false)).resolves.toMatchObject({ wagerPoints: 2, chargedWagerCents: 50 })
    await expect(api.spin(300, false)).resolves.toMatchObject({ wagerPoints: 300, chargedWagerCents: 7500 })
  })

  it('retains historical funded wagers and recovers an old prepared paid operation above the new picker maximum', async () => {
    const oldOperation = { operationId: 'legacy-paid-operation-0001', wagerPoints: 2000, useFreeSpin: false, useSpecialBoost: false }
    const historical = { ...spinResult(), wagerPoints: 2000, chargedWagerCents: 50000 }
    expect(decodePharaohSpinResult(historical)).not.toBeNull()
    const freeState = { ...slotState(), freeSpinsRemaining: 1, freeSpinWagerPoints: 1501,
      gameFeatureState: { ...slotState().gameFeatureState, freeSpinsRemaining: 1, freeSpinWagerPoints: 1501 } }
    expect(decodePharaohSlotState(freeState)).not.toBeNull()
    expect(decodePharaohSpinResult({ ...spinResult(), wagerPoints: 1501, isFreeSpin: true, chargedWagerCents: 0 })).not.toBeNull()
    const posts: RequestInit[] = []
    const fetch = vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
      if (String(path).includes('/state?')) return jsonReply({ ...slotState(), pendingOperation: oldOperation })
      posts.push(init!)
      return jsonReply(historical)
    })
    const api = createPharaohApi(fetch, account())
    await api.getState()
    expect(api.pending()).toEqual(oldOperation)
    await expect(api.resumeSpin()).resolves.toMatchObject({ wagerPoints: 2000 })
    expect(JSON.parse(String(posts[0].body)).wagerPoints).toBe(2000)
    expect(new Headers(posts[0].headers).get('Idempotency-Key')).toBe(oldOperation.operationId)
    expect(api.pending()).toBeUndefined()
    expect(() => api.spin(2000, false)).toThrow('Choose an available wager')
  })

  it('retains the exact wager and operation key across lost replies and a new API instance', async () => {
    const user = account()
    const calls: RequestInit[] = []
    let fail = true
    const fetch = vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
      if (String(path).includes('/state?')) return jsonReply(slotState())
      calls.push(init!)
      if (fail) throw new TypeError('Reply lost')
      return jsonReply(spinResult())
    })
    const api = createPharaohApi(fetch, user)
    await api.getState()
    await expect(api.spin(12, false)).rejects.toThrow('Reply lost')
    expect(api.pending()?.wagerPoints).toBe(12)
    const recovered = createPharaohApi(fetch, user)
    await recovered.getState()
    fail = false
    await recovered.spin(100, false)
    expect(calls[1].body).toBe(calls[0].body)
    expect(new Headers(calls[1].headers).get('Idempotency-Key')).toBe(new Headers(calls[0].headers).get('Idempotency-Key'))
    expect(new Headers(calls[1].headers).get('X-FortuneForge-Expected-Account')).toBe(user)
    expect(recovered.pending()).toBeUndefined()
  })

  it('deduplicates simultaneous paid spins and refuses to use another account’s saved wager', async () => {
    let finish!: (value: Response) => void
    const fetch = vi.fn(async (path: RequestInfo | URL) => String(path).includes('/state?') ? jsonReply(slotState())
      : new Promise<Response>(resolve => { finish = resolve }))
    const api = createPharaohApi(fetch, account())
    await api.getState()
    const first = api.spin(12, false)
    const second = api.spin(100, false)
    expect(second).toBe(first)
    expect(createPharaohApi(fetch, account()).pending()).toBeUndefined()
    finish(jsonReply(spinResult()))
    await first
    expect(fetch.mock.calls.filter(([path]) => String(path).includes('prepared-spins'))).toHaveLength(1)
  })

  it('retains exact expedition actions and refuses new rooms or wagers before recovery', async () => {
    const user = account()
    const active = slotState(4, { ...tombState(4), activeRun: tombRun() })
    const calls: RequestInit[] = []
    let fail = true
    const fetch = vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
      if (String(path).includes('/state?')) return jsonReply(active)
      calls.push(init!)
      if (fail) throw new TypeError('Action reply lost')
      return jsonReply(slotState(5))
    })
    const api = createPharaohApi(fetch, user)
    await api.getState()
    const intent = { action: 'collect', expectedRevision: 4 } as const
    await expect(api.action(intent)).rejects.toThrow('Action reply lost')
    const remounted = createPharaohApi(fetch, user)
    expect(remounted.pendingAction()).toEqual(intent)
    expect(() => remounted.spin(12, false)).toThrow(/saved expedition/)
    expect(() => remounted.action({ action: 'open-room', expectedRevision: 4, choiceId: 'other' })).toThrow(/saved expedition/)
    fail = false
    await remounted.action(intent)
    expect(calls[1].body).toBe(calls[0].body)
    expect(new Headers(calls[1].headers).get('Idempotency-Key')).toBe(new Headers(calls[0].headers).get('Idempotency-Key'))
    expect(remounted.pendingAction()).toBeNull()
  })
})
