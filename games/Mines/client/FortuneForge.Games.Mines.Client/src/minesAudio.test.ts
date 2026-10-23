import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

let play: ReturnType<typeof vi.fn>, audio: ReturnType<typeof vi.fn>, contextConstructor: ReturnType<typeof vi.fn>
let oscillators: Array<{ type: string; frequency: { setValueAtTime: ReturnType<typeof vi.fn>; exponentialRampToValueAtTime: ReturnType<typeof vi.fn> }; connect: ReturnType<typeof vi.fn>; start: ReturnType<typeof vi.fn>; stop: ReturnType<typeof vi.fn> }>
let gains: Array<{ gain: { setValueAtTime: ReturnType<typeof vi.fn>; linearRampToValueAtTime: ReturnType<typeof vi.fn>; exponentialRampToValueAtTime: ReturnType<typeof vi.fn> }; connect: ReturnType<typeof vi.fn> }>
let context: { state: string; currentTime: number; destination: object; resume: ReturnType<typeof vi.fn>; createOscillator: ReturnType<typeof vi.fn>; createGain: ReturnType<typeof vi.fn> }

beforeEach(() => {
  vi.resetModules()
  play = vi.fn().mockResolvedValue(undefined)
  audio = vi.fn(function () { return { volume: 1, play } })
  oscillators = []; gains = []
  context = {
    state: 'running', currentTime: 5, destination: {}, resume: vi.fn().mockResolvedValue(undefined),
    createOscillator: vi.fn(() => {
      const oscillator = { type: '', frequency: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }, connect: vi.fn(), start: vi.fn(), stop: vi.fn() }
      oscillators.push(oscillator); return oscillator
    }),
    createGain: vi.fn(() => {
      const gain = { gain: { setValueAtTime: vi.fn(), linearRampToValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() }, connect: vi.fn() }
      gains.push(gain); return gain
    }),
  }
  contextConstructor = vi.fn(function () { return context })
  vi.stubGlobal('Audio', audio); vi.stubGlobal('AudioContext', contextConstructor)
})
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('Mines optional sound', () => {
  it('plays the packaged wager chip asset at a quiet volume', async () => {
    const { playMinesAudio } = await import('./minesAudio')
    playMinesAudio('wager', false)
    expect(audio).toHaveBeenCalledOnce()
    expect(audio.mock.calls[0][0]).toContain('chip-lay-1.ogg')
    expect(audio.mock.results[0].value.volume).toBe(.3)
    expect(play).toHaveBeenCalledOnce()
    expect(contextConstructor).not.toHaveBeenCalled()
  })

  it.each(['wager', 'gem', 'mine', 'cashout'] as const)('does not create or schedule any sound while muted: %s', async cue => {
    const { playMinesAudio, unlockMinesAudio } = await import('./minesAudio')
    unlockMinesAudio()
    playMinesAudio(cue, true)
    expect(audio).not.toHaveBeenCalled()
    expect(play).not.toHaveBeenCalled()
    expect(context.createOscillator).not.toHaveBeenCalled()
  })

  it.each([['gem', [660, 990, 1320]], ['cashout', [523.25, 659.25, 783.99, 1046.5]], ['mine', [100, 62, 38]]] as const)('schedules a bounded %s cue after a user gesture', async (cue, frequencies) => {
    const { playMinesAudio, unlockMinesAudio } = await import('./minesAudio')
    unlockMinesAudio(); unlockMinesAudio()
    expect(contextConstructor).toHaveBeenCalledOnce()
    playMinesAudio(cue, false)
    expect(context.createOscillator).toHaveBeenCalledTimes(frequencies.length)
    frequencies.forEach((frequency, index) => {
      const oscillator = oscillators[index], gain = gains[index]
      expect(oscillator.frequency.setValueAtTime).toHaveBeenCalledWith(frequency, 5 + index * .055)
      expect(oscillator.start).toHaveBeenCalledOnce()
      expect(oscillator.stop).toHaveBeenCalledWith(5 + index * .055 + .32)
      expect(oscillator.connect).toHaveBeenCalledWith(gain)
      expect(gain.connect).toHaveBeenCalledWith(context.destination)
      expect(gain.gain.exponentialRampToValueAtTime).toHaveBeenCalledWith(.001, 5 + index * .055 + .3)
    })
    expect(audio).not.toHaveBeenCalled()
  })

  it('silently skips cues until browser audio has been unlocked', async () => {
    const { playMinesAudio, unlockMinesAudio } = await import('./minesAudio')
    playMinesAudio('gem', false)
    expect(contextConstructor).not.toHaveBeenCalled()
    unlockMinesAudio(); context.state = 'suspended'
    playMinesAudio('mine', false)
    expect(context.createOscillator).not.toHaveBeenCalled()
  })

  it('absorbs browser play and resume promise rejection without affecting game flow', async () => {
    play.mockRejectedValue(new DOMException('User gesture required', 'NotAllowedError'))
    context.resume.mockRejectedValue(new DOMException('Audio blocked', 'NotAllowedError'))
    const { playMinesAudio, unlockMinesAudio } = await import('./minesAudio')
    expect(() => unlockMinesAudio()).not.toThrow()
    expect(() => playMinesAudio('wager', false)).not.toThrow()
    await Promise.resolve(); await Promise.resolve()
  })

  it('absorbs synchronous media setup or play failure so a wager can continue', async () => {
    const { playMinesAudio } = await import('./minesAudio')
    audio.mockImplementationOnce(function () { throw new DOMException('Media disabled', 'SecurityError') })
    expect(() => playMinesAudio('wager', false)).not.toThrow()
    play.mockImplementationOnce(() => { throw new DOMException('Audio device failed', 'NotSupportedError') })
    expect(() => playMinesAudio('wager', false)).not.toThrow()
  })

  it('absorbs unavailable WebAudio or a failed oscillator', async () => {
    const { playMinesAudio, unlockMinesAudio } = await import('./minesAudio')
    contextConstructor.mockImplementationOnce(() => { throw new Error('Unsupported audio context') })
    expect(() => unlockMinesAudio()).not.toThrow()
    expect(() => playMinesAudio('gem', false)).not.toThrow()
    unlockMinesAudio()
    context.createOscillator.mockImplementationOnce(() => { throw new Error('Device unavailable') })
    expect(() => playMinesAudio('cashout', false)).not.toThrow()
  })
})
