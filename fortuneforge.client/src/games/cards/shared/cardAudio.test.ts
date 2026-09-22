import { afterEach, describe, expect, it, vi } from 'vitest'
import { playCardAudio } from './cardAudio'

describe('playCardAudio', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('plays the matching low-volume cue without making sound availability part of gameplay', () => {
    const play = vi.fn(() => Promise.resolve())
    const created: FakeAudio[] = []

    class FakeAudio {
      preload = ''
      volume = 1
      source: string

      constructor(source: string) {
        this.source = source
        created.push(this)
      }

      play = play
    }

    vi.stubGlobal('Audio', FakeAudio)

    playCardAudio('shuffle')

    expect(created).toHaveLength(1)
    expect(created[0]?.source).toContain('card-shuffle')
    expect(created[0]?.preload).toBe('auto')
    expect(created[0]?.volume).toBe(0.32)
    expect(play).toHaveBeenCalledOnce()
  })

  it('does nothing when the browser does not provide an audio API', () => {
    vi.stubGlobal('Audio', undefined)

    expect(() => playCardAudio('deal')).not.toThrow()
  })
})
