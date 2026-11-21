import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchWithAccountSession } from '../../account/services/accountsApi'
import { readPendingSlotSpin, requestSlotState, requestSpin, SpinRequestError } from './slotsApi'

vi.mock('../../account/services/accountsApi', () => ({ fetchWithAccountSession: vi.fn() }))

const fetcher = vi.mocked(fetchWithAccountSession)
const gameId = 'arcane-archives-v1'
const request = { gameId, wagerPoints: 12, useFreeSpin: false, useSpecialBoost: false }
const slotState = {
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: 0,
  energyBalance: 0, sealCollections: [], freeSpinFeatureMode: null,
}

function enablePreparedSpin() {
  fetcher.mockResolvedValueOnce(Response.json(slotState, { headers: { 'X-FortuneForge-Prepared-Spin': '1' } }))
  return requestSlotState(gameId)
}

function postedSpin(callIndex: number) {
  const [path, init] = fetcher.mock.calls[callIndex]
  return { path, key: new Headers(init?.headers).get('Idempotency-Key'), body: JSON.parse(String(init?.body)) }
}

afterEach(() => fetcher.mockReset())

describe('Arcane paid spin recovery', () => {
  it('replays the original wager and operation ID after an interrupted response', async () => {
    await enablePreparedSpin()
    fetcher.mockRejectedValueOnce(new TypeError('Disconnected'))
    await expect(requestSpin(request, undefined, undefined, 'retry-account')).rejects.toThrow('Disconnected')
    fetcher.mockResolvedValueOnce(Response.json({ error: 'Unavailable' }, { status: 503 }))
    await expect(requestSpin({ ...request, wagerPoints: 20 }, undefined, undefined, 'retry-account')).rejects.toBeInstanceOf(SpinRequestError)

    expect(postedSpin(1)).toMatchObject({ path: '/api/slots/arcane/prepared-spins', body: request, key: expect.any(String) })
    expect(postedSpin(2)).toEqual(postedSpin(1))
  })

  it('resumes the account’s existing server round when this page has a different operation', async () => {
    await enablePreparedSpin()
    const pending = { operationId: 'stored-arcane-operation', wagerPoints: 40, useFreeSpin: false, useSpecialBoost: false }
    fetcher.mockResolvedValueOnce(Response.json({ code: 'slot-operation-conflict', pendingOperation: pending }, { status: 409 }))
    fetcher.mockResolvedValueOnce(Response.json({ error: 'Unavailable' }, { status: 503 }))
    await expect(requestSpin(request, undefined, undefined, 'second-tab-account')).rejects.toBeInstanceOf(SpinRequestError)

    expect(postedSpin(2)).toEqual({
      path: '/api/slots/arcane/prepared-spins', key: pending.operationId,
      body: { ...request, wagerPoints: pending.wagerPoints },
    })
  })

  it('uses the existing slot route with a release that does not advertise prepared rounds', async () => {
    fetcher.mockResolvedValueOnce(Response.json(slotState))
    await requestSlotState(gameId)
    fetcher.mockResolvedValueOnce(Response.json({ error: 'Unavailable' }, { status: 503 }))
    await expect(requestSpin(request, undefined, undefined, 'old-release-account')).rejects.toBeInstanceOf(SpinRequestError)

    expect(postedSpin(1)).toEqual({ path: '/api/slots/spins', key: null, body: request })
  })
})

describe('Wukong paid spin recovery', () => {
  const wukongId = 'wukong-journey-v1'
  const headers = { 'X-FortuneForge-Prepared-Spin': '1' }
  const pending = { operationId: 'saved-wukong-operation', wagerPoints: 11, useFreeSpin: true, useSpecialBoost: false }
  const wukongRequest = { ...request, gameId: wukongId, useFreeSpin: true }

  it('binds the state read to the player and replays an odd saved wager across interrupted replies', async () => {
    const accountId = 'wukong-recovery-account'
    fetcher.mockResolvedValueOnce(Response.json({ ...slotState, freeSpinsRemaining: 5,
      freeSpinWagerPoints: 11, pendingOperation: pending }, { headers }))
    expect(await requestSlotState(wukongId, accountId)).toMatchObject({ freeSpinsRemaining: 5, freeSpinWagerPoints: 11 })
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-FortuneForge-Expected-Account')).toBe(accountId)

    fetcher.mockRejectedValueOnce(new TypeError('Disconnected'))
    await expect(requestSpin(wukongRequest, undefined, undefined, accountId)).rejects.toThrow('Disconnected')
    fetcher.mockResolvedValueOnce(Response.json({ error: 'Unavailable' }, { status: 503 }))
    await expect(requestSpin(wukongRequest, undefined, undefined, accountId)).rejects.toBeInstanceOf(SpinRequestError)
    expect(postedSpin(1)).toEqual({ path: '/api/slots/prepared-spins', key: pending.operationId, body: { ...wukongRequest, wagerPoints: 11 } })
    expect(postedSpin(2)).toEqual(postedSpin(1))

    fetcher.mockResolvedValueOnce(Response.json(slotState))
    await requestSlotState(wukongId, accountId)
    await expect(requestSpin(wukongRequest, undefined, undefined, accountId)).rejects.toMatchObject({ status: 503 })
    expect(fetcher).toHaveBeenCalledTimes(4)
    expect(readPendingSlotSpin(wukongId, accountId)?.operationId).toBe(pending.operationId)
  })

  it('does not adopt a saved operation from a rejected account state request', async () => {
    const accountId = 'wukong-changed-account'
    fetcher.mockResolvedValueOnce(Response.json({ ...slotState, pendingOperation: pending }, { status: 403, headers }))
    await expect(requestSlotState(wukongId, accountId)).rejects.toThrow('403')
    fetcher.mockResolvedValueOnce(Response.json({ error: 'Unavailable' }, { status: 503 }))
    await expect(requestSpin(wukongRequest, undefined, undefined, accountId)).rejects.toBeInstanceOf(SpinRequestError)
    expect(postedSpin(1).key).not.toBe(pending.operationId)
  })

  it('defers a different round to the next attempt so the UI can load its saved free-spin mode', async () => {
    const accountId = 'wukong-two-tabs'
    fetcher.mockResolvedValueOnce(Response.json(slotState, { headers }))
    await requestSlotState(wukongId, accountId)
    fetcher.mockResolvedValueOnce(Response.json({ code: 'slot-operation-conflict', pendingOperation: pending }, { status: 409 }))
    await expect(requestSpin({ ...wukongRequest, useFreeSpin: false }, undefined, undefined, accountId))
      .rejects.toMatchObject({ code: 'slot-operation-conflict' })
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(readPendingSlotSpin(wukongId, accountId)).toEqual(pending)
  })
})
