import { describe, expect, it } from 'vitest'
import { createAsteroidsPaidAdmission, readAsteroidsPaidAdmission } from './asteroidsPaidAdmission'

describe('Asteroids paid admission recovery', () => {
  it('preserves an original expired window and the exact serialized body across browser recovery', () => {
    const admission = createAsteroidsPaidAdmission({ startsAtUtc: '2026-09-03T22:00:00Z', endsAtUtc: '2026-09-04T22:00:00Z' })
    const encoded = JSON.stringify(admission)
    expect(JSON.stringify(readAsteroidsPaidAdmission(JSON.parse(encoded)))).toBe(encoded)
    expect(readAsteroidsPaidAdmission(admission)).toEqual(admission)
  })

  it.each([undefined, null, {}, { expectedWindowStartsAtUtc: 'bad' },
    { expectedWindowStartsAtUtc: '2026-09-03T22:00:00Z', expectedWindowEndsAtUtc: '2026-09-02T22:00:00Z' }])(
    'makes legacy or malformed pending state recover-only instead of opening a new wager (%j)', (value) => {
      expect(readAsteroidsPaidAdmission(value)).toEqual({ recoverOnly: true })
    },
  )

  it('preserves explicit recovery mode and rejects invalid newly chosen windows', () => {
    expect(readAsteroidsPaidAdmission({ recoverOnly: true })).toEqual({ recoverOnly: true })
    expect(() => createAsteroidsPaidAdmission({ startsAtUtc: '2026-09-03T22:00:00Z', endsAtUtc: '2026-09-03T22:00:00Z' })).toThrow('window is invalid')
  })
})
