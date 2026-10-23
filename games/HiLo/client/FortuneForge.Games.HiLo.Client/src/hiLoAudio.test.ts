import { afterEach, describe, expect, it, vi } from 'vitest'
import { playHiLoAudio, readHiLoMuted, writeHiLoMuted } from './hiLoAudio'

afterEach(() => { writeHiLoMuted(true); vi.unstubAllGlobals() })

function installAudio() {
  const clips: TestAudio[] = []
  class TestAudio extends EventTarget {
    volume = 1
    play = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
    pause = vi.fn()
    constructor(readonly src: string) { super(); clips.push(this) }
  }
  vi.stubGlobal('Audio', TestAudio)
  return clips
}

describe('Hi-Lo audio controls', () => {
  it('suppresses all cues while muted and tolerates unavailable media', () => {
    const audio = vi.fn()
    vi.stubGlobal('Audio', audio)
    playHiLoAudio('deal', true)
    expect(audio).not.toHaveBeenCalled()
    vi.stubGlobal('Audio', undefined)
    expect(() => playHiLoAudio('deal', false)).not.toThrow()
  })

  it('uses approved local media at restrained volume and tolerates autoplay rejection', async () => {
    const clips = installAudio()
    playHiLoAudio('collect', false)
    expect(clips[0].src).toContain('chips-collide-1.ogg')
    expect(clips[0].volume).toBe(.28)
    expect(clips[0].play).toHaveBeenCalledOnce()
    clips[0].dispatchEvent(new Event('ended'))
    const blockedClips: BlockedAudio[] = []
    class BlockedAudio extends EventTarget {
      volume = 1
      pause = vi.fn()
      play = vi.fn().mockRejectedValue(new DOMException('Autoplay blocked'))
      constructor() { super(); blockedClips.push(this) }
    }
    vi.stubGlobal('Audio', BlockedAudio)
    expect(() => playHiLoAudio('deal', false)).not.toThrow()
    await Promise.resolve()
    expect(() => writeHiLoMuted(true)).not.toThrow()
    expect(blockedClips[0].pause).not.toHaveBeenCalled()
  })

  it('immediately pauses every active clip when mute is enabled', () => {
    const clips = installAudio()
    playHiLoAudio('shuffle', false)
    playHiLoAudio('deal', false)
    expect(clips).toHaveLength(2)
    writeHiLoMuted(true)
    expect(clips.every(clip => clip.pause.mock.calls.length === 1)).toBe(true)
    writeHiLoMuted(true)
    expect(clips.every(clip => clip.pause.mock.calls.length === 1)).toBe(true)
  })

  it('releases finished and failed clips instead of retaining them until a later mute', () => {
    const clips = installAudio()
    playHiLoAudio('shuffle', false)
    playHiLoAudio('deal', false)
    clips[0].dispatchEvent(new Event('ended'))
    clips[1].dispatchEvent(new Event('error'))
    writeHiLoMuted(true)
    expect(clips.every(clip => clip.pause.mock.calls.length === 0)).toBe(true)
  })

  it('stops active sound even when persisting the mute preference is denied', () => {
    const clips = installAudio()
    vi.stubGlobal('localStorage', { setItem: () => { throw new Error('Denied') } })
    playHiLoAudio('collect', false)
    expect(() => writeHiLoMuted(true)).not.toThrow()
    expect(clips[0].pause).toHaveBeenCalledOnce()
  })

  it('persists mute and keeps the control functional when storage is unavailable', () => {
    const entries = new Map<string, string>()
    vi.stubGlobal('localStorage', { getItem: (key: string) => entries.get(key) ?? null, setItem: (key: string, value: string) => entries.set(key, value) })
    writeHiLoMuted(true)
    expect(readHiLoMuted()).toBe(true)
    writeHiLoMuted(false)
    expect(readHiLoMuted()).toBe(false)
    vi.stubGlobal('localStorage', { getItem: () => { throw new Error('Denied') }, setItem: () => { throw new Error('Denied') } })
    expect(() => writeHiLoMuted(true)).not.toThrow()
    expect(readHiLoMuted()).toBe(false)
  })
})
