import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadAudioPreferences, saveAudioPreferences } from './audioPreferences'
import capturedCases from './__fixtures__/audio-preferences.json'

const currentKey = 'fortune-forge.audio-preferences.v2'
const legacyKey = 'fortune-forge.audio-preferences'
afterEach(() => vi.unstubAllGlobals())

describe('audio preference storage compatibility', () => {
  it.each(capturedCases)('$name', scenario => {
    const reads: string[] = []
    const storage = {
      getItem(key: string) {
        reads.push(key)
        if (scenario.failure === 'read') throw new Error('Blocked read')
        return key === currentKey ? scenario.current : scenario.legacy
      },
    }
    const browser = { localStorage: storage }
    if (scenario.failure === 'access') {
      Object.defineProperty(browser, 'localStorage', { get() { throw new Error('Blocked access') } })
    }
    vi.stubGlobal('window', scenario.failure === 'missing-window' ? undefined : browser)

    expect(loadAudioPreferences()).toEqual(scenario.expected)
    expect(reads).toEqual(scenario.reads)
  })

  it('writes only the current version and retains legacy data', () => {
    const values = new Map([[legacyKey, '{"mode":"muted","volume":9}']])
    const storage = {
      getItem: vi.fn((key: string) => values.get(key) ?? null),
      setItem: vi.fn((key: string, value: string) => { values.set(key, value) }),
      removeItem: vi.fn(),
    }
    vi.stubGlobal('window', { localStorage: storage })
    saveAudioPreferences({ mode: 'results-only', volume: 0 })

    expect(storage.setItem).toHaveBeenCalledExactlyOnceWith(currentKey, '{"mode":"results-only","volume":0}')
    expect(storage.getItem).not.toHaveBeenCalled()
    expect(storage.removeItem).not.toHaveBeenCalled()
    expect(values.get(legacyKey)).toBe('{"mode":"muted","volume":9}')
  })

  it.each(['access', 'write', 'missing-window'] as const)('keeps persistence optional when %s fails', failure => {
    const browser = { localStorage: { setItem: vi.fn(() => { throw new Error('Blocked write') }) } }
    if (failure === 'access') {
      Object.defineProperty(browser, 'localStorage', { get() { throw new Error('Blocked access') } })
    }
    vi.stubGlobal('window', failure === 'missing-window' ? undefined : browser)
    expect(() => saveAudioPreferences({ mode: 'muted', volume: 65 })).not.toThrow()
  })
})
