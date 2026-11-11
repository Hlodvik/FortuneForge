// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { describe, expect, it, vi } from 'vitest'
import { ARCANE_ARCHIVES_SOUNDS } from '@fortuneforge/games-arcane-archives/sounds'
import { CANDY_CARNIVAL_SOUNDS } from '@fortuneforge/games-candy-carnival/sounds'
import { COSMIC_FORTUNE_SOUNDS } from '@fortuneforge/games-cosmic-fortune/sounds'
import { DESERT_TREASURES_SOUNDS } from '@fortuneforge/games-desert-treasures/sounds'
import { GODS_OF_OLYMPUS_SOUNDS } from '@fortuneforge/games-gods-of-olympus/sounds'
import { GOLD_FOUNDRY_SOUNDS } from '@fortuneforge/games-gold-foundry/manifest'
import { HIGH_NOON_FORTUNE_SOUNDS } from '@fortuneforge/games-high-noon-fortune/sounds'
import { NORDIC_LEGENDS_SOUNDS } from '@fortuneforge/games-nordic-legends/sounds'
import { OCEAN_ODYSSEY_SOUNDS } from '@fortuneforge/games-ocean-odyssey/sounds'
import { PHANTOM_MANOR_SOUNDS } from '@fortuneforge/games-phantom-manor/sounds'
import { PIRATES_FORTUNE_SOUNDS } from '@fortuneforge/games-pirates-fortune/sounds'
import { RAINBOW_REALM_SOUNDS } from '@fortuneforge/games-rainbow-realm/sounds'
import { REEL_RICHES_SOUNDS } from '@fortuneforge/games-reel-riches/sounds'
import { ROBOT_REVOLUTION_SOUNDS } from '@fortuneforge/games-robot-revolution/sounds'
import { ROYAL_DRAW_SOUNDS } from '@fortuneforge/games-royal-draw/sounds'
import { SAMURAI_FORTUNE_SOUNDS } from '@fortuneforge/games-samurai-fortune/sounds'
import { WUKONG_TREASURES_SOUNDS } from '@fortuneforge/games-wukong/sounds'
import { DEFAULT_SLOT_SOUNDS, type SlotSoundCue, type SlotSoundCueId, type SlotSoundSet } from './soundSets'
import { useSlotAudio } from '../hooks/useSlotAudio'
import { loadAllSlotGameManifests } from '../../../games/slots/routeRegistry'

const requiredCue = (sounds: SlotSoundSet, id: SlotSoundCueId): SlotSoundCue => {
  const cue = sounds.cues[id]
  if (!cue) throw new Error(`Sound set '${sounds.id}' is missing cue '${id}'.`)
  return cue
}

const definedCues = (sounds: SlotSoundSet): readonly SlotSoundCue[] =>
  Object.values(sounds.cues).filter((cue): cue is SlotSoundCue => cue !== undefined)

const pirateEventCueIds = [
  PIRATES_FORTUNE_SOUNDS.events.ambience,
  PIRATES_FORTUNE_SOUNDS.events.autoSpinAmbience ?? 'ambience',
  PIRATES_FORTUNE_SOUNDS.events.leverPull,
  PIRATES_FORTUNE_SOUNDS.events.reelSpin,
  PIRATES_FORTUNE_SOUNDS.events.reelStop,
  PIRATES_FORTUNE_SOUNDS.events.specialReelSpin ?? 'reel-spin',
  PIRATES_FORTUNE_SOUNDS.events.specialReelStop ?? 'reel-stop',
  ...Object.values(PIRATES_FORTUNE_SOUNDS.events.results).flat(),
]
const pirateCueSources = definedCues(PIRATES_FORTUNE_SOUNDS).map(({ source }) => source)
const pirateSpecialCueIds = Object.values(PIRATES_FORTUNE_SOUNDS.events.specialResults ?? {}).flat()

describe('Pirates\' Fortune sound set', () => {
  it('retains its voyage identity without playing it and keeps distinct CC0 Broadside Run cues', () => {
    const eventSources = pirateEventCueIds.map((cueId) => requiredCue(PIRATES_FORTUNE_SOUNDS, cueId).source)

    expect(eventSources).toHaveLength(15)
    expect(eventSources).toHaveLength(pirateEventCueIds.length)
    expect(eventSources.filter((source) => source.includes('pirate-seagull'))).toHaveLength(5)
    expect(eventSources.some((source) => source.includes('pirate-seagull-miss'))).toBe(true)
    expect(eventSources.some((source) => source.includes('pirate-seagull-win'))).toBe(true)
    expect(eventSources.some((source) => source.includes('pirate-voyage'))).toBe(true)
    expect(eventSources.some((source) => source.includes('pirate-wave-ambience'))).toBe(true)
    expect(eventSources.some((source) => source.includes('pirate-helm-spin'))).toBe(true)
    expect(eventSources.some((source) => source.includes('pirate-small-win'))).toBe(false)
    expect(eventSources.some((source) => source.includes('pirate-arr-bonus'))).toBe(false)
    expect(PIRATES_FORTUNE_SOUNDS.events.specialReelSpin).not.toBe(PIRATES_FORTUNE_SOUNDS.events.reelSpin)
    expect(PIRATES_FORTUNE_SOUNDS.events.specialReelStop).not.toBe(PIRATES_FORTUNE_SOUNDS.events.reelStop)
    expect(PIRATES_FORTUNE_SOUNDS.events.results['no-win']).toEqual(['soft-miss'])
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'soft-miss').source).toContain('pirate-wave-ambience')
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'special-soft-miss').source).toContain('pirate-wave-ambience')
    expect(PIRATES_FORTUNE_SOUNDS.events.results['single-three']).toEqual(['low-win'])
    expect(PIRATES_FORTUNE_SOUNDS.events.autoSpinAmbience).toBe('auto-spin-ambience')
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'auto-spin-ambience').source).toContain('pirate-voyage')
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'auto-spin-ambience').source)
      .not.toContain('pirate-wave-ambience')
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'auto-spin-ambience').loop).toBe(true)
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'ambience').enabled).toBe(false)
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'auto-spin-ambience').enabled).toBe(false)
    expect(pirateSpecialCueIds).toHaveLength(8)
    expect(pirateSpecialCueIds.every((cueId) => cueId.startsWith('special-'))).toBe(true)
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'special-reel-spin').source)
      .not.toBe(requiredCue(PIRATES_FORTUNE_SOUNDS, 'reel-spin').source)
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'special-reel-stop').source)
      .not.toBe(requiredCue(PIRATES_FORTUNE_SOUNDS, 'reel-stop').source)
    // A special-round loss remains a loss: it uses the same one-shot wave
    // as a normal manual miss, not a new music loop.
    expect(requiredCue(PIRATES_FORTUNE_SOUNDS, 'special-soft-miss').source)
      .toBe(requiredCue(PIRATES_FORTUNE_SOUNDS, 'soft-miss').source)
    expect(pirateSpecialCueIds
      .filter((cueId) => cueId !== 'special-soft-miss')
      .every((cueId) => !requiredCue(PIRATES_FORTUNE_SOUNDS, cueId).source.includes('pirate-')),
    ).toBe(true)
    expect(pirateCueSources.some((source) => source.includes('pirate-helm-spin'))).toBe(true)
  })
})

describe('Wukong sound set', () => {
  it('preserves its original procedural effects while its recorded music stays disabled', () => {
    const resultCueIds = Object.values(WUKONG_TREASURES_SOUNDS.events.results).flat()
    const effectCueIds = [
      WUKONG_TREASURES_SOUNDS.events.leverPull,
      WUKONG_TREASURES_SOUNDS.events.reelSpin,
      WUKONG_TREASURES_SOUNDS.events.reelStop,
      ...resultCueIds,
    ]
    const effectSources = effectCueIds.map((cueId) => requiredCue(WUKONG_TREASURES_SOUNDS, cueId).source)

    expect(WUKONG_TREASURES_SOUNDS.events.ambience).toBe('ambience')
    expect(requiredCue(WUKONG_TREASURES_SOUNDS, 'ambience').source).toContain('asian-dragon')
    expect(requiredCue(WUKONG_TREASURES_SOUNDS, 'ambience').enabled).toBe(false)
    expect(effectSources.every((source) => source.includes('wukong-'))).toBe(true)
    expect(requiredCue(WUKONG_TREASURES_SOUNDS, 'lever-pull').source)
      .not.toBe(requiredCue(WUKONG_TREASURES_SOUNDS, 'reel-spin').source)
    expect(requiredCue(WUKONG_TREASURES_SOUNDS, 'reel-stop').source)
      .not.toBe(requiredCue(WUKONG_TREASURES_SOUNDS, 'soft-miss').source)
    expect(WUKONG_TREASURES_SOUNDS.events.results['no-win']).toEqual(['soft-miss'])
  })
})

describe('Reel Riches sound set', () => {
  it('uses its original theme suite with distinct base/bonus music and semantic collections', () => {
    const { events } = REEL_RICHES_SOUNDS
    expect(events.themeSuite).toBe(true)
    expect(requiredCue(REEL_RICHES_SOUNDS, events.ambience)).toMatchObject({ enabled: true, loop: true })
    expect(requiredCue(REEL_RICHES_SOUNDS, events.ambience).source).toContain('assets/audio/night-drive.ogg')
    expect(requiredCue(REEL_RICHES_SOUNDS, events.specialAmbience ?? 'ambience').source).toContain('assets/audio/reel-riches-trophy-loop.wav')
    expect(events.specialCollect).toBe('special-collect')
    expect(events.autoSpinAmbience).toBeUndefined()
    expect(Object.keys(events.collectionArrival ?? {})).toEqual(['sync', 'rows', 'paw', 'rand'])
    expect(Object.values(events.specialResults ?? {}).every(ids => ids.length === 1)).toBe(true)
  })
})

describe('catalogue audio provenance and allocation', () => {
  const packageSoundSetsByGameId = {
    'wukong-journey-to-the-west': WUKONG_TREASURES_SOUNDS,
    'rainbow-realm': RAINBOW_REALM_SOUNDS,
    'pirates-fortune': PIRATES_FORTUNE_SOUNDS,
    'gods-of-olympus': GODS_OF_OLYMPUS_SOUNDS,
    'reel-riches': REEL_RICHES_SOUNDS,
    'high-noon-fortune': HIGH_NOON_FORTUNE_SOUNDS,
    'royal-draw': ROYAL_DRAW_SOUNDS,
    'arcane-archives': ARCANE_ARCHIVES_SOUNDS,
    'cosmic-fortune': COSMIC_FORTUNE_SOUNDS,
    'gold-foundry': GOLD_FOUNDRY_SOUNDS,
    'ocean-odyssey': OCEAN_ODYSSEY_SOUNDS,
    'samurai-fortune': SAMURAI_FORTUNE_SOUNDS,
    'candy-carnival': CANDY_CARNIVAL_SOUNDS,
    'phantom-manor': PHANTOM_MANOR_SOUNDS,
    'nordic-legends': NORDIC_LEGENDS_SOUNDS,
    'desert-treasures': DESERT_TREASURES_SOUNDS,
    'robot-revolution': ROBOT_REVOLUTION_SOUNDS,
  } satisfies Readonly<Record<string, SlotSoundSet>>
  const packageSoundSets = Object.values(packageSoundSetsByGameId)
  const allSoundSets: readonly SlotSoundSet[] = [DEFAULT_SLOT_SOUNDS, ...packageSoundSets]
  const foundryAmbienceSource = requiredCue(GOLD_FOUNDRY_SOUNDS, 'ambience').source

  it('uses package-owned audio contracts for every standalone game', async () => {
    const manifests = (await loadAllSlotGameManifests()).filter(game => !['neon-nights', 'dragon-hoard'].includes(game.id))
    expect(manifests).toHaveLength(17)
    expect(packageSoundSets).toHaveLength(17)
    expect(allSoundSets).toHaveLength(18)
    for (const game of manifests) {
      const sounds = game.experience.sounds
      expect(sounds).toBe(packageSoundSetsByGameId[game.id as keyof typeof packageSoundSetsByGameId])
      if (game.id !== 'wukong-journey-to-the-west') {
        expect(definedCues(sounds).map(cue => cue.source).join('\n')).not.toMatch(/\/wukong-[^/]+\.wav/)
      }
      expect(requiredCue(sounds, sounds.events.ambience)).toMatchObject(['wukong-journey-to-the-west', 'pirates-fortune'].includes(game.id) ? {
          source: expect.stringMatching(/^disabled:opentracks\/[^/]+\.mp3$/),
          category: 'effect', loop: true, enabled: false,
        } : {
          category: 'effect', loop: true, enabled: true,
        })
    }
    for (const sounds of allSoundSets) {
      for (const cue of definedCues(sounds)) {
        if (cue.source.startsWith('disabled:')) {
          expect(cue.enabled).toBe(false)
        } else {
          expect(cue.source).not.toMatch(/^\/assets\/slots\/audio-suites\/v1\//)
          expect(cue.source.length).toBeGreaterThan(0)
        }
      }
    }
  }, 30_000)

  it('gives Gold Foundry its own workshop ambience and button and reel cues', () => {
    const { cues, events } = GOLD_FOUNDRY_SOUNDS
    expect(requiredCue(GOLD_FOUNDRY_SOUNDS, 'ambience').source).toBe(foundryAmbienceSource)
    expect(cues['special-ambience']).toBe(cues.ambience)
    expect(events).toMatchObject({
      buttonClick: 'button-click', betStep: 'button-click', stopButtonClick: 'button-click',
      spinButtonClick: 'spin-button-click', specialReelSpin: 'reel-spin', specialReelStop: 'reel-stop',
    })
    expect(cues['button-click']?.source).toContain('hammer-ring.wav')
    expect(cues['spin-button-click']?.source).toContain('spin-button-metal-hit.wav')
    expect(requiredCue(GOLD_FOUNDRY_SOUNDS, 'lever-pull').enabled).toBe(false)
    expect(cues['reel-spin']).toMatchObject({ source: expect.stringContaining('forge-bellows-and-crackling-fire.wav'), loop: true })
    expect(requiredCue(GOLD_FOUNDRY_SOUNDS, 'reel-stop').source).toContain('reel-brake.wav')
    expect(definedCues(GOLD_FOUNDRY_SOUNDS).some(cue => /wukong/i.test(cue.source))).toBe(false)
  })

  it('creates no Audio for disabled cues or on hook mount, then allocates effects and theme music only on demand', () => {
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
      for (const soundSet of allSoundSets) {
        act(() => root.render(createElement(Harness, { soundSet })))
        expect(audio).not.toHaveBeenCalled()
        act(() => {
          for (const [cueId, cue] of Object.entries(soundSet.cues)) {
            if (cue?.enabled === false) {
              current().startLoop(cueId)
              current().playCue(cueId)
            }
          }
        })
        expect(audio).not.toHaveBeenCalled()
        expect(play).not.toHaveBeenCalled()
      }
      act(() => root.render(createElement(Harness, { soundSet: DEFAULT_SLOT_SOUNDS })))
      act(() => current().playCue(DEFAULT_SLOT_SOUNDS.events.leverPull))
      expect(audio).toHaveBeenCalledExactlyOnceWith(expect.stringContaining('chips-handle-1.ogg'))
      expect(play).toHaveBeenCalledOnce()

      act(() => root.render(createElement(Harness, { soundSet: REEL_RICHES_SOUNDS })))
      expect(audio).toHaveBeenCalledOnce()
      act(() => current().startLoop('special-ambience'))
      expect(audio).toHaveBeenCalledTimes(2)
      expect(audio).toHaveBeenLastCalledWith(expect.stringContaining('assets/audio/reel-riches-trophy-loop.wav'))
      expect(play).toHaveBeenCalledTimes(2)
    } finally {
      act(() => root.unmount())
      host.remove()
      window.localStorage.clear()
      vi.unstubAllGlobals()
    }
  })
})
