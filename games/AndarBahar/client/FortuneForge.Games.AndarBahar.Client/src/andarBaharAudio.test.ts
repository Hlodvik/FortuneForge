import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { playAndarBaharAudio, stopAndarBaharAudio } from './andarBaharAudio'

class FakeAudio {
  static instances: FakeAudio[] = []
  volume = 1
  playbackRate = 1
  currentTime = 1
  pause = vi.fn()
  play = vi.fn(() => Promise.resolve())
  addEventListener = vi.fn()
  constructor(readonly src: string) { FakeAudio.instances.push(this) }
}

beforeEach(() => { stopAndarBaharAudio(); FakeAudio.instances = []; vi.stubGlobal('Audio', FakeAudio) })
afterEach(() => { stopAndarBaharAudio(); vi.unstubAllGlobals() })

describe('Andar Bahar soundscape', () => {
  it('stays silent while muted and does not play a restored win before interaction', () => {
    playAndarBaharAudio('chip', true)
    playAndarBaharAudio('win', false)
    expect(FakeAudio.instances).toHaveLength(0)
  })

  it('uses the short palace cue only for win and a neutral card tap for result', () => {
    playAndarBaharAudio('click', false)
    playAndarBaharAudio('win', false)
    playAndarBaharAudio('result', false)
    expect(FakeAudio.instances.map(audio => audio.src)).toEqual([
      expect.stringContaining('chip-lay-1.ogg'), expect.stringContaining('palace-pluck-win.wav'), expect.stringContaining('card-place-1.ogg'),
    ])
    expect(FakeAudio.instances[1].volume).toBeLessThan(.4)
  })

  it('replaces rapid utility clicks instead of building a chorus', () => {
    playAndarBaharAudio('click', false)
    playAndarBaharAudio('click', false)
    expect(FakeAudio.instances[0].pause).toHaveBeenCalledOnce()
    expect(FakeAudio.instances[1].playbackRate).toBe(1.6)
  })

  it('caps concurrent sounds and stops them all on mute or unmount', () => {
    for (let i = 0; i < 10; i++) playAndarBaharAudio('chip', false)
    expect(FakeAudio.instances.filter(audio => audio.pause.mock.calls.length === 0)).toHaveLength(6)
    stopAndarBaharAudio()
    expect(FakeAudio.instances.every(audio => audio.pause.mock.calls.length > 0)).toBe(true)
    playAndarBaharAudio('win', false)
    expect(FakeAudio.instances).toHaveLength(10)
  })

  it('treats missing or blocked browser audio as non-fatal', () => {
    vi.stubGlobal('Audio', undefined)
    expect(() => playAndarBaharAudio('click', false)).not.toThrow()
    vi.stubGlobal('Audio', class { constructor() { throw new Error('blocked') } })
    expect(() => playAndarBaharAudio('click', false)).not.toThrow()
  })

  it('safely releases rejected playback', async () => {
    const blocked = vi.fn(() => Promise.reject(new Error('autoplay blocked')))
    vi.stubGlobal('Audio', class extends FakeAudio { play = blocked })
    playAndarBaharAudio('click', false)
    await Promise.resolve()
    stopAndarBaharAudio()
    expect(blocked).toHaveBeenCalledOnce()
  })
})
