// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe('Sic Bo audio', () => {
  it.each([['click', 2], ['roll', 18], ['win', 10]] as const)('synthesizes a %s cue without loading cross-game assets', async (cue, expectedStrikes) => {
    const audio = installAudioContextMock()
    const { playSicBoSound } = await import('./sicBoAudio')

    playSicBoSound(cue)

    expect(audio.createOscillator).toHaveBeenCalledTimes(expectedStrikes)
    expect(audio.createGain).toHaveBeenCalledTimes(expectedStrikes + (cue === 'roll' ? 1 : 0))
    expect(audio.createBufferSource).toHaveBeenCalledTimes(cue === 'roll' ? 1 : 0)
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
  const createBuffer = vi.fn(() => ({ getChannelData: vi.fn(() => new Float32Array(480)) }))
  const createBufferSource = vi.fn(() => ({ connect: vi.fn().mockReturnThis(), start: vi.fn(), stop: vi.fn() }))
  const createBiquadFilter = vi.fn(() => ({ type: 'lowpass', frequency, Q: { value: 0 }, connect: vi.fn().mockReturnThis() }))
  const audio = { state: 'running', currentTime: 1, sampleRate: 800, destination, resume: vi.fn(), createOscillator, createGain, createBuffer, createBufferSource, createBiquadFilter }
  vi.stubGlobal('AudioContext', function AudioContextMock() { return audio })
  return audio
}
