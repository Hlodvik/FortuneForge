import { afterEach, describe, expect, it, vi } from 'vitest'
import { isGameActivityApiEnabled } from './gameActivityCapability'

afterEach(() => vi.unstubAllEnvs())

describe('game activity API release capability', () => {
  it.each([undefined, '', 'false', 'TRUE', '1'])('stays disabled unless explicitly enabled: %s', value => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', value)
    expect(isGameActivityApiEnabled()).toBe(false)
  })
  it('requires the explicit build-time true value', () => {
    vi.stubEnv('VITE_GAME_ACTIVITY_API_ENABLED', 'true')
    expect(isGameActivityApiEnabled()).toBe(true)
  })
})
