// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe('Sic Bo audio', () => {
  it.each([['click', 2], ['roll', 16]] as const)('synthesizes a %s cue without loading cross-game assets', async (cue, expectedStrikes) => {
    const audio = installAudioContextMock()
    const { playSicBoSound } = await import('./sicBoAudio')

    playSicBoSound(cue)

    expect(audio.createOscillator).toHaveBeenCalledTimes(expectedStrikes)
    expect(audio.createGain).toHaveBeenCalledTimes(expectedStrikes)
  })

  it('keeps gameplay functional when Web Audio is unavailable', async () => {
    vi.stubGlobal('AudioContext', undefined)
    const { playSicBoSound } = await import('./sicBoAudio')
    expect(() => playSicBoSound('roll')).not.toThrow()
  })
})

function installAudioContextMock() {
  const frequency = { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }
  const gainValue = { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }
  const destination = {}
  const createOscillator = vi.fn(() => ({
    type: 'sine', frequency, connect: vi.fn().mockReturnThis(), start: vi.fn(), stop: vi.fn(),
  }))
  const createGain = vi.fn(() => ({ gain: gainValue, connect: vi.fn().mockReturnValue(destination) }))
  const audio = { state: 'running', currentTime: 1, destination, resume: vi.fn(), createOscillator, createGain }
  vi.stubGlobal('AudioContext', function AudioContextMock() { return audio })
  return audio
}
