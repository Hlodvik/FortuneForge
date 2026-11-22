import { readFileSync } from 'node:fs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ROBOT_REVOLUTION_SLOT_GAME } from '@fortuneforge/games-robot-revolution/manifest'
import { fetchWithAccountSession } from '../../account/services/accountsApi'
import { requestSpin } from './slotsApi'
import type { SpinResult } from '../types/slots'

vi.mock('../../account/services/accountsApi', () => ({ fetchWithAccountSession: vi.fn() }))
const fetcher = vi.mocked(fetchWithAccountSession)
const native = JSON.parse(readFileSync(new URL(
  '../../../../../tools/fixtures/slot-contracts/fifth-sun/native-paid-spin.json', import.meta.url,
), 'utf8')) as SpinResult
const request = { gameId: native.gameId, wagerPoints: native.wagerPoints, useFreeSpin: false, useSpecialBoost: false }
const definitions = ROBOT_REVOLUTION_SLOT_GAME.experience.symbols.definitions

afterEach(() => fetcher.mockReset())

describe('Fifth Sun payout vocabulary', () => {
  it('accepts the native ordinary and guardian matches using game-owned symbols', async () => {
    fetcher.mockResolvedValueOnce(Response.json(native))
    await expect(requestSpin(request, undefined, definitions, 'fifth-sun-vocabulary-accept')).resolves.toEqual(native)
    expect(native.payout.paylines.some(line => line.paylineId < 1000)).toBe(true)
    expect(native.payout.paylines.some(line => line.paylineId >= 1000)).toBe(true)
  })

  it('rejects an undeclared payout symbol while all reel symbols remain valid', async () => {
    const tampered = structuredClone(native)
    tampered.payout.paylines[0].matches[0].match.symbolId = 'UNDECLARED_SYMBOL'
    fetcher.mockResolvedValueOnce(Response.json(tampered))
    await expect(requestSpin(request, undefined, definitions, 'fifth-sun-vocabulary-reject'))
      .rejects.toThrow('invalid reel result')
  })
})

describe('paid Eclipse mode transport', () => {
  it('preserves the charged paid round and its saved mode separately from free spins', async () => {
    const response = { ...native, isFreeSpin: false,
      paidMode: { mode: 'lunar-eclipse', remaining: 4, wagerPoints: native.wagerPoints } }
    fetcher.mockResolvedValueOnce(Response.json(response))
    await expect(requestSpin(request, undefined, definitions, 'eclipse-paid-mode-accept')).resolves.toEqual(response)
  })

  it.each([
    { mode: 'solar-eclipse', remaining: 0, wagerPoints: 12 },
    { mode: 'solar-eclipse', remaining: 1.5, wagerPoints: 12 },
    { mode: '', remaining: 5, wagerPoints: 12 },
    { mode: 'lunar-eclipse', remaining: 5, wagerPoints: -12 },
  ])('rejects malformed saved paid mode %j', async paidMode => {
    fetcher.mockResolvedValueOnce(Response.json({ ...native, paidMode }))
    await expect(requestSpin(request, undefined, definitions, 'eclipse-paid-mode-reject'))
      .rejects.toThrow('invalid reel result')
  })
})
