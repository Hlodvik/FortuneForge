// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createArcaneApi, isArcaneSpinResult } from '../src/api.js'
import { potions, reply, spin, state, tome } from './fixtures.js'

beforeEach(() => sessionStorage.clear())
const terms = { wagerPoints: 12, useFreeSpin: false, useSpecialBoost: false }

describe('Arcane paid transport ownership and recovery', () => {
  it('replays the saved operation and original terms after a lost reply, never a new selected wager', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(state())).mockRejectedValueOnce(new TypeError('lost reply'))
      .mockResolvedValueOnce(reply(spin()))
    const api = createArcaneApi(fetch, 'arcane-lost-reply')
    await api.state()
    await expect(api.spin(terms)).rejects.toThrow('lost reply')
    const saved = api.pendingSpin()
    expect(saved?.operationId).toBeTruthy()
    await api.spin({ ...terms, wagerPoints: 100 })
    const first = fetch.mock.calls[1]
    const retry = fetch.mock.calls[2]
    expect(first[0]).toBe('/api/slots/arcane/prepared-spins')
    expect(retry[0]).toBe(first[0])
    expect(retry[1].headers['Idempotency-Key']).toBe(saved?.operationId)
    expect(JSON.parse(retry[1].body)).toEqual({ gameId: 'arcane-archives-v1', ...terms })
    expect(api.pendingSpin()).toBeUndefined()
    expect(fetch.mock.calls.some(([path]) => String(path).includes('/demo/'))).toBe(false)
  })
  it('adopts the authenticated server pending round after a different-tab conflict', async () => {
    const pending = { operationId: 'server-owned-round-0001', wagerPoints: 20, useFreeSpin: false, useSpecialBoost: false }
    const fetch = vi.fn().mockResolvedValueOnce(reply(state()))
      .mockResolvedValueOnce(reply({ error: 'resume', pendingOperation: pending }, true, 409))
      .mockResolvedValueOnce(reply(spin({ wagerPoints: 20 })))
    const api = createArcaneApi(fetch, 'arcane-other-tab')
    await api.state()
    const result = await api.spin(terms)
    expect(result.wagerPoints).toBe(20)
    expect(fetch.mock.calls[2][1].headers['Idempotency-Key']).toBe(pending.operationId)
    expect(JSON.parse(fetch.mock.calls[2][1].body).wagerPoints).toBe(20)
  })
  it('retains recovery identity when a reply is malformed or belongs to different spin terms', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(state())).mockResolvedValueOnce(reply(spin({ wagerPoints: 100 })))
    const api = createArcaneApi(fetch, 'arcane-malformed')
    await api.state()
    await expect(api.spin(terms)).rejects.toThrow('mismatched')
    expect(api.pendingSpin()?.wagerPoints).toBe(12)
    await expect(api.activatePotion(7)).rejects.toThrow('Recover the current')
    expect(fetch).toHaveBeenCalledTimes(2)
  })
  it('blocks an unsafe second legacy paid POST after an ambiguous unkeyed response', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(reply(state(), false)).mockRejectedValueOnce(new TypeError('network'))
    const api = createArcaneApi(fetch, 'arcane-legacy')
    await api.state()
    await expect(api.spin(terms)).rejects.toThrow('network')
    await expect(api.spin(terms)).rejects.toThrow('Reload Arcane')
    expect(fetch).toHaveBeenCalledTimes(2)
  })
  it('owns authoritative Tome and potion commands and restores their returned state', async () => {
    const activeTome = tome({ activePage: 'embers', boostedSpinsRemaining: 5 })
    const bottled = potions()
    const activePotions = potions({ active: { bottle: bottled.bottles[0], spinsRemaining: 5 } })
    const fetch = vi.fn().mockResolvedValueOnce(reply(activeTome)).mockResolvedValueOnce(reply(activePotions))
    const api = createArcaneApi(fetch, 'arcane-workbench')
    expect(await api.changeTomePage('embers', true)).toEqual(activeTome)
    expect(await api.activatePotion(7)).toEqual(activePotions)
    expect(fetch.mock.calls[0][0]).toBe('/api/slots/arcane/tome')
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ page: 'embers', activate: true })
    expect(fetch.mock.calls[1][0]).toBe('/api/slots/arcane/potions/activate')
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ potionId: 7 })
  })
  it('rejects collector totals that disagree with the settled payout without evaluating odds', () => {
    const value = spin()
    value.reels[0][0] = 'WAND'; value.reels[1][0] = 'POWER'
    value.arcaneWandCollection = { wand: { reel: 0, row: 0 }, cells: [{ reel: 1, row: 0, symbolId: 'POWER', multiplier: 2, amountPoints: 24 }], totalPoints: 24 }
    expect(isArcaneSpinResult(value)).toBe(false)
    value.payout = { totalPoints: 24, paylines: [{ paylineId: 903, amountPoints: 24,
      matches: [{ amountPoints: 24, multiplier: 2, match: { paylineId: 903, symbolId: 'WAND', matchLength: 2,
        positions: [{ reel: 0, row: 0 }, { reel: 1, row: 0 }], wildPositions: [] } }] }] }
    expect(isArcaneSpinResult(value)).toBe(true)
    value.arcaneWandCollection.cells[0].amountPoints = 25
    expect(isArcaneSpinResult(value)).toBe(false)
  })
})
