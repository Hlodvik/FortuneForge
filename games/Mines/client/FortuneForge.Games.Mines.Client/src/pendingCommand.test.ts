import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { MinesCommand } from './contracts'
import { clearPending, newRequestId, pendingKey, readPending, savePending } from './pendingCommand'

const start: MinesCommand = { action: 'start', requestId: 'mines-original-0001', wagerCents: 1000, mineCount: 3 }
const reveal: MinesCommand = { action: 'reveal', requestId: 'mines-original-0002', roundId: 'round-0001', tile: 6 }
const cashout: MinesCommand = { action: 'cashout', requestId: 'mines-original-0003', roundId: 'round-0001' }
let values: Map<string, string>
let storage: { getItem: ReturnType<typeof vi.fn>; setItem: ReturnType<typeof vi.fn>; removeItem: ReturnType<typeof vi.fn> }

beforeEach(() => {
  values = new Map()
  storage = {
    getItem: vi.fn((key: string) => values.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => { values.set(key, value) }),
    removeItem: vi.fn((key: string) => { values.delete(key) }),
  }
  vi.stubGlobal('localStorage', storage)
})
afterEach(() => vi.unstubAllGlobals())

describe('pending Mines commands', () => {
  it.each([start, reveal, cashout])('round-trips the original $action command without changing its requestId', command => {
    savePending('player-a', command)
    expect(storage.setItem).toHaveBeenCalledWith(pendingKey('player-a'), JSON.stringify(command))
    expect(readPending('player-a')).toEqual(command)
    expect(values.get(pendingKey('player-a'))).toBe(JSON.stringify(command))
  })

  it('keeps recovery scoped to the signed-in player and clears only that player', () => {
    savePending('player-a', start)
    savePending('player-b', reveal)
    expect(readPending('player-c')).toBeNull()
    clearPending('player-a')
    expect(readPending('player-a')).toBeNull()
    expect(readPending('player-b')).toEqual(reveal)
    expect(storage.removeItem).toHaveBeenCalledExactlyOnceWith(pendingKey('player-a'))
  })

  it.each([
    ['invalid JSON', '{'], ['null', 'null'], ['number', '12'], ['array', '[]'],
    ['unknown action', JSON.stringify({ ...start, action: 'settle' })],
    ['missing requestId', JSON.stringify({ ...start, requestId: undefined })],
    ['short requestId', JSON.stringify({ ...start, requestId: 'short' })],
    ['unsafe requestId characters', JSON.stringify({ ...start, requestId: 'mines original 0001' })],
    ['overlong requestId', JSON.stringify({ ...start, requestId: 'a'.repeat(129) })],
    ['string wager', JSON.stringify({ ...start, wagerCents: '1000' })],
    ['fractional wager', JSON.stringify({ ...start, wagerCents: 100.5 })],
    ['unsafe wager', JSON.stringify({ ...start, wagerCents: Number.MAX_SAFE_INTEGER + 1 })],
    ['zero wager', JSON.stringify({ ...start, wagerCents: 0 })],
    ['negative wager', JSON.stringify({ ...start, wagerCents: -1000 })],
    ['below minimum wager', JSON.stringify({ ...start, wagerCents: 9 })],
    ['above maximum wager', JSON.stringify({ ...start, wagerCents: 10_001 })],
    ['zero mines', JSON.stringify({ ...start, mineCount: 0 })],
    ['too many mines', JSON.stringify({ ...start, mineCount: 25 })],
    ['fractional mine count', JSON.stringify({ ...start, mineCount: 3.5 })],
    ['string tile', JSON.stringify({ ...reveal, tile: '6' })],
    ['negative tile', JSON.stringify({ ...reveal, tile: -1 })],
    ['out of board tile', JSON.stringify({ ...reveal, tile: 25 })],
    ['empty reveal round', JSON.stringify({ ...reveal, roundId: '' })],
    ['empty cash-out round', JSON.stringify({ ...cashout, roundId: '' })],
    ['missing cash-out round', JSON.stringify({ ...cashout, roundId: undefined })],
  ])('blocks play on corrupt saved data: %s', (_reason, raw) => {
    values.set(pendingKey('player-a'), raw)
    expect(() => readPending('player-a')).toThrow(/Restore your round/)
    expect(values.get(pendingKey('player-a'))).toBe(raw)
    expect(storage.removeItem).not.toHaveBeenCalled()
  })

  it('surfaces a storage write failure so the caller can block a wager before sending it', () => {
    storage.setItem.mockImplementationOnce(() => { throw new DOMException('Quota exceeded', 'QuotaExceededError') })
    expect(() => savePending('player-a', start)).toThrow('Quota exceeded')
    expect(readPending('player-a')).toBeNull()
  })

  it('surfaces unavailable storage during recovery and never clears the pending command', () => {
    storage.getItem.mockImplementationOnce(() => { throw new DOMException('Storage disabled', 'SecurityError') })
    expect(() => readPending('player-a')).toThrow('Storage disabled')
    expect(storage.removeItem).not.toHaveBeenCalled()
  })

  it('surfaces clear failure rather than claiming a previous request was removed', () => {
    savePending('player-a', start)
    storage.removeItem.mockImplementationOnce(() => { throw new DOMException('Storage disabled', 'SecurityError') })
    expect(() => clearPending('player-a')).toThrow('Storage disabled')
    expect(readPending('player-a')).toEqual(start)
  })

  it('creates different valid retry identifiers for distinct commands', () => {
    const a = newRequestId(), b = newRequestId()
    expect(a).not.toBe(b)
    expect(a).toMatch(/^[a-zA-Z0-9_-]{16,128}$/)
    expect(b).toMatch(/^[a-zA-Z0-9_-]{16,128}$/)
  })
})
