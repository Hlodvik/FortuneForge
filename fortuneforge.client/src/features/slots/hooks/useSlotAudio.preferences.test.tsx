// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { expect, it, vi } from 'vitest'
import { DEFAULT_SLOT_SOUNDS } from '../config/soundSets'
import { useSlotAudio, type AudioPreferences } from './useSlotAudio'

it('migrates, saves and reloads preferences through the public hook without starting audio', () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
  const Audio = vi.fn()
  vi.stubGlobal('Audio', Audio)
  const currentKey = 'fortune-forge.audio-preferences.v2'
  const legacyKey = 'fortune-forge.audio-preferences'
  window.localStorage.clear()
  window.localStorage.setItem(legacyKey, '{"mode":"muted","volume":9}')
  const host = document.createElement('div')
  document.body.append(host)
  let root = createRoot(host)
  let controls: ReturnType<typeof useSlotAudio> | undefined
  const current = () => {
    if (!controls) throw new Error('Audio hook is not mounted')
    return controls
  }
  function Harness() {
    controls = useSlotAudio(DEFAULT_SLOT_SOUNDS)
    return null
  }
  try {
    act(() => root.render(<Harness />))
    expect(current().preferences).toEqual({ mode: 'all', volume: 9 })
    expect(JSON.parse(window.localStorage.getItem(currentKey)!)).toEqual(current().preferences)
    act(() => current().setVolume(37.6))
    act(() => current().toggleResultsOnly())
    const expected: AudioPreferences = { mode: 'results-only', volume: 38 }
    expect(current().preferences).toEqual(expected)
    expect(JSON.parse(window.localStorage.getItem(currentKey)!)).toEqual(expected)
    expect(window.localStorage.getItem(legacyKey)).toBe('{"mode":"muted","volume":9}')

    act(() => root.unmount())
    root = createRoot(host)
    act(() => root.render(<Harness />))
    expect(current().preferences).toEqual(expected)
    expect(Audio).not.toHaveBeenCalled()
  } finally {
    act(() => root.unmount())
    host.remove()
    window.localStorage.clear()
    vi.unstubAllGlobals()
  }
})
