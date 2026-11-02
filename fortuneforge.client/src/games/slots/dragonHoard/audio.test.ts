// @vitest-environment jsdom
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { describe, expect, it, vi } from 'vitest'
import { DEFAULT_SLOT_SOUNDS, DRAGON_HOARD_SOUNDS, type SlotSoundSet } from '../../../features/slots/config/soundSets'
import { useSlotAudio } from '../../../features/slots/hooks/useSlotAudio'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'

describe('Midnight Vault sound mapping', () => {
  it('ships the unmodified licensed recording with its matching attribution', () => {
    const base = resolve('public/assets/slots/midnight-vault/v3/audio')
    const recording = readFileSync(resolve(base, 'i-knew-a-guy-kevin-macleod.mp3'))
    const credits = readFileSync(resolve(base, 'CREDITS.txt'), 'utf8')
    expect(recording.byteLength).toBe(6_044_335)
    expect(recording.subarray(0, 3).toString()).toBe('ID3')
    expect(createHash('sha256').update(recording).digest('hex'))
      .toBe('10e3480eefcbefe839f69dfff27d99991e16c23981072fca769f38eb21aab50c')
    expect(credits).toContain('"I Knew a Guy" Kevin MacLeod (incompetech.com)')
    expect(credits).toContain('https://creativecommons.org/licenses/by/4.0/')
    expect(credits).toContain('No edits, encoding, normalization,')
  })

  it('uses the licensed noir score and selected CC0 effects at restrained levels', () => {
    const { cues, events } = DRAGON_HOARD_SOUNDS
    expect(DRAGON_HOARD_SOUNDS.id).toBe('dragon-hoard-audio-v1')
    expect(DRAGON_HOARD_SLOT_GAME.experience.sounds).toBe(DRAGON_HOARD_SOUNDS)
    expect(cues[events.ambience]).toMatchObject({
      source: '/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3',
      baseVolume: 0.04, category: 'effect', loop: true, enabled: true,
    })
    expect(cues[events.leverPull]).toMatchObject({
      source: expect.stringContaining('chip-lay-1.ogg'),
      baseVolume: 0.26, category: 'effect',
    })
    expect(events.results).toMatchObject({
      bonus: ['five-jewel'], five: ['five-twinkle'], premium: ['premium-win'],
      'single-three': ['low-win'], 'no-win': ['soft-miss'],
    })
  })

  it('uses documented CC0 effects while preserving event IDs, categories and loop behavior', () => {
    const { cues, events } = DRAGON_HOARD_SOUNDS
    expect(events.reelSpin).toBe(DEFAULT_SLOT_SOUNDS.events.reelSpin)
    expect(events.reelStop).toBe(DEFAULT_SLOT_SOUNDS.events.reelStop)
    expect(cues['reel-spin']).toMatchObject({
      source: expect.stringContaining('card-shuffle.ogg'),
      category: DEFAULT_SLOT_SOUNDS.cues['reel-spin'].category,
      loop: DEFAULT_SLOT_SOUNDS.cues['reel-spin'].loop,
    })
    expect(cues['reel-stop'].source).toContain('chip-lay-1.ogg')
    expect(cues['reel-stop'].category).toBe(DEFAULT_SLOT_SOUNDS.cues['reel-stop'].category)
    expect(cues['reel-stop'].loop).toBe(DEFAULT_SLOT_SOUNDS.cues['reel-stop'].loop)
    for (const cueId of Object.values(events.results).flat()) {
      expect(cues[cueId].category).toBe('result')
      expect(cues[cueId].source).toMatch(/(?:chips-handle-1|chips-collide-1|card-place-1)\.ogg/)
      expect(cues[cueId].baseVolume).toBeLessThanOrEqual(0.3)
    }
    expect(DEFAULT_SLOT_SOUNDS.cues['reel-spin'].source).toContain('card-shuffle.ogg')
    expect(DEFAULT_SLOT_SOUNDS.cues['low-win'].source).toContain('chips-handle-1.ogg')
    expect(DRAGON_HOARD_SLOT_GAME.experience.rules.gameId).toBe('dragon-hoard-v1')
    expect(DRAGON_HOARD_SLOT_GAME.routes.demo).toBe('/slots/dragon-hoard/demo')
  })

  it('loads its licensed music only on demand while unresolved shared music stays disabled', () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
    window.localStorage.clear()
    const play = vi.fn(() => Promise.resolve())
    const audio = vi.fn(function FakeAudio(this: Partial<HTMLAudioElement>, source: string) {
      this.src = source
      this.play = play
      this.pause = vi.fn()
      this.addEventListener = vi.fn()
      this.currentTime = 0
    })
    vi.stubGlobal('Audio', audio)
    let controls: ReturnType<typeof useSlotAudio> | undefined
    function Harness({ soundSet }: { soundSet: SlotSoundSet }) {
      controls = useSlotAudio(soundSet)
      return null
    }
    const current = () => {
      if (!controls) throw new Error('Audio hook is not mounted')
      return controls
    }
    const host = document.createElement('div')
    document.body.append(host)
    const root = createRoot(host)
    try {
      act(() => root.render(createElement(Harness, { soundSet: DRAGON_HOARD_SOUNDS })))
      expect(audio).not.toHaveBeenCalled()
      expect(play).not.toHaveBeenCalled()
      act(() => current().startLoop('ambience'))
      expect(audio).toHaveBeenCalledExactlyOnceWith('/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3')
      expect(play).toHaveBeenCalledOnce()
      act(() => current().playCue(DRAGON_HOARD_SOUNDS.events.leverPull))
      expect(audio).toHaveBeenCalledTimes(2)
      expect(audio).toHaveBeenLastCalledWith(expect.stringContaining('chip-lay-1.ogg'))
      expect(play).toHaveBeenCalledTimes(2)

      act(() => root.render(createElement(Harness, { soundSet: DEFAULT_SLOT_SOUNDS })))
      act(() => current().startLoop(DEFAULT_SLOT_SOUNDS.events.ambience))
      expect(audio).toHaveBeenCalledTimes(2)
      expect(play).toHaveBeenCalledTimes(2)
      act(() => current().playCue(DEFAULT_SLOT_SOUNDS.events.leverPull))
      expect(audio).toHaveBeenCalledTimes(3)
      expect(audio).toHaveBeenLastCalledWith(expect.stringContaining('chips-handle-1.ogg'))
      expect(play).toHaveBeenCalledTimes(3)
    } finally {
      act(() => root.unmount())
      host.remove()
      vi.unstubAllGlobals()
    }
  })
})
