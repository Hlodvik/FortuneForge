// @vitest-environment jsdom
import { act, createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { describe, expect, it, vi } from 'vitest'
import { GOLD_FOUNDRY_SOUNDS } from '../../../games/slots/goldFoundry/sounds'
import * as soundSets from './soundSets'
import { PIRATES_FORTUNE_SOUNDS, REEL_RICHES_SOUNDS, WUKONG_TREASURES_SOUNDS } from './soundSets'
import type { SlotSoundSet } from './soundSets'
import { useSlotAudio } from '../hooks/useSlotAudio'
import { loadAllSlotGameManifests } from '../../../games/slots/routeRegistry'

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
const pirateCueSources = Object.values(PIRATES_FORTUNE_SOUNDS.cues).map(({ source }) => source)
const pirateSpecialCueIds = Object.values(PIRATES_FORTUNE_SOUNDS.events.specialResults ?? {}).flat()

describe('Pirates\' Fortune sound set', () => {
  it('retains its voyage identity without playing it and keeps distinct CC0 Broadside Run cues', () => {
    const eventSources = pirateEventCueIds.map((cueId) => PIRATES_FORTUNE_SOUNDS.cues[cueId]!.source)

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
    expect(PIRATES_FORTUNE_SOUNDS.cues['soft-miss'].source).toContain('pirate-wave-ambience')
    expect(PIRATES_FORTUNE_SOUNDS.cues['special-soft-miss'].source).toContain('pirate-wave-ambience')
    expect(PIRATES_FORTUNE_SOUNDS.events.results['single-three']).toEqual(['low-win'])
    expect(PIRATES_FORTUNE_SOUNDS.events.autoSpinAmbience).toBe('auto-spin-ambience')
    expect(PIRATES_FORTUNE_SOUNDS.cues['auto-spin-ambience'].source).toContain('pirate-voyage')
    expect(PIRATES_FORTUNE_SOUNDS.cues['auto-spin-ambience'].source)
      .not.toContain('pirate-wave-ambience')
    expect(PIRATES_FORTUNE_SOUNDS.cues['auto-spin-ambience'].loop).toBe(true)
    expect(PIRATES_FORTUNE_SOUNDS.cues.ambience.enabled).toBe(false)
    expect(PIRATES_FORTUNE_SOUNDS.cues['auto-spin-ambience'].enabled).toBe(false)
    expect(pirateSpecialCueIds).toHaveLength(8)
    expect(pirateSpecialCueIds.every((cueId) => cueId.startsWith('special-'))).toBe(true)
    expect(PIRATES_FORTUNE_SOUNDS.cues['special-reel-spin'].source)
      .not.toBe(PIRATES_FORTUNE_SOUNDS.cues['reel-spin'].source)
    expect(PIRATES_FORTUNE_SOUNDS.cues['special-reel-stop'].source)
      .not.toBe(PIRATES_FORTUNE_SOUNDS.cues['reel-stop'].source)
    // A special-round loss remains a loss: it uses the same one-shot wave
    // as a normal manual miss, not a new music loop.
    expect(PIRATES_FORTUNE_SOUNDS.cues['special-soft-miss'].source)
      .toBe(PIRATES_FORTUNE_SOUNDS.cues['soft-miss'].source)
    expect(pirateSpecialCueIds
      .filter((cueId) => cueId !== 'special-soft-miss')
      .every((cueId) => !PIRATES_FORTUNE_SOUNDS.cues[cueId]!.source.includes('pirate-')),
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
    const effectSources = effectCueIds.map((cueId) => WUKONG_TREASURES_SOUNDS.cues[cueId]!.source)

    expect(WUKONG_TREASURES_SOUNDS.events.ambience).toBe('ambience')
    expect(WUKONG_TREASURES_SOUNDS.cues.ambience.source).toContain('asian-dragon')
    expect(WUKONG_TREASURES_SOUNDS.cues.ambience.enabled).toBe(false)
    expect(effectSources.every((source) => source.includes('wukong-'))).toBe(true)
    expect(WUKONG_TREASURES_SOUNDS.cues['lever-pull'].source)
      .not.toBe(WUKONG_TREASURES_SOUNDS.cues['reel-spin'].source)
    expect(WUKONG_TREASURES_SOUNDS.cues['reel-stop'].source)
      .not.toBe(WUKONG_TREASURES_SOUNDS.cues['soft-miss'].source)
    expect(WUKONG_TREASURES_SOUNDS.events.results['no-win']).toEqual(['soft-miss'])
  })
})

describe('Reel Riches sound set', () => {
  it('uses its original theme suite with distinct base/bonus music and semantic collections', () => {
    const { cues, events } = REEL_RICHES_SOUNDS
    expect(events.themeSuite).toBe(true)
    expect(cues[events.ambience]).toMatchObject({ source: '/assets/slots/audio-suites/v1/reel-riches/night-drive.ogg', enabled: true, loop: true })
    expect(cues[events.specialAmbience ?? 'ambience']!.source).toBe('/assets/slots/audio-suites/v1/reel-riches/midnight-mix.ogg')
    expect(events.specialCollect).toBeUndefined()
    expect(events.autoSpinAmbience).toBeUndefined()
    expect(Object.keys(events.collectionArrival ?? {})).toEqual(['sync', 'rows', 'paw', 'rand'])
    expect(Object.values(events.specialResults ?? {}).every(ids => ids.length === 1)).toBe(true)
  })
})

describe('catalogue audio provenance and allocation', () => {
  const allSoundSets = [...Object.values(soundSets), GOLD_FOUNDRY_SOUNDS]
  const arcaneMusicSource = '/assets/slots/audio-suites/v1/arcane-archives/magic-puzzle-in-game-1.ogg'
  const foundryAudioPath = '/assets/slots/gold-foundry/v1/audio'
  const foundryAmbienceSource = `${foundryAudioPath}/foundry-working-room.ogg`
  const foundrySources = new Set([
    foundryAmbienceSource,
    `${foundryAudioPath}/hammer-ring.wav`,
    `${foundryAudioPath}/spin-button-metal-hit.wav`,
    `${foundryAudioPath}/forge-bellows-and-crackling-fire.wav`,
    `${foundryAudioPath}/reel-brake.wav`,
    ...['win-small', 'win-medium', 'win-big', 'triple-sevens', 'no-win'].map(name => `${foundryAudioPath}/${name}.ogg`),
  ])
  const documentedLegacySources = /\/(?:chip-lay-1\.ogg|chips-handle-1\.ogg|chips-collide-1\.ogg|card-place-1\.ogg|card-shuffle\.ogg|pirate-helm-spin\.ogg|pirate-seagull-(?:miss|win)\.wav|pirate-wave-ambience\.flac|wukong-(?:celestial-chime|cloud-rush|gong-win|soft-step|staff-whoosh|temple-bell|woodblock)\.wav|reel-riches-(?:cast-land|line-tension|small-catch|special-collect|trophy-award|trophy-loop|trophy-win)\.wav|i-knew-a-guy-kevin-macleod\.mp3)$/
  const documentedSuiteSources = /^\/assets\/slots\/audio-suites\/v1\/[a-z-]+\/(?:night-drive|midnight-mix|button-click|bet-step|spin-launch|reel-roll|reel-land|bonus-reel-roll|bonus-reel-land|collect-flamingo|collect-dice|collect-sunset|collect-arcade|win-small|win-medium|win-big|triple-sevens|no-win|bonus-entry|bonus-complete)\.ogg$/

  it('enables seventeen themed suites while retaining the three excluded title selections', async () => {
    const manifests = await loadAllSlotGameManifests()
    expect(manifests).toHaveLength(20)
    expect(allSoundSets).toHaveLength(21)
    for (const game of manifests) {
      const sounds = game.experience.sounds
      expect(allSoundSets).toContain(sounds)
      expect(sounds.cues[sounds.events.ambience]).toMatchObject(game.id === 'dragon-hoard'
        ? {
          source: '/assets/slots/midnight-vault/v3/audio/i-knew-a-guy-kevin-macleod.mp3',
          category: 'effect', loop: true, enabled: true,
        }
        : ['wukong-journey-to-the-west', 'pirates-fortune'].includes(game.id) ? {
          source: expect.stringMatching(/^disabled:opentracks\/[^/]+\.mp3$/),
          category: 'effect', loop: true, enabled: false,
        } : {
          source: game.id === 'gold-foundry' ? foundryAmbienceSource
            : game.id === 'arcane-archives' ? arcaneMusicSource : `/assets/slots/audio-suites/v1/${game.id}/night-drive.ogg`,
          category: 'effect', loop: true, enabled: true,
        })
      if (game.id === 'arcane-archives') {
        expect(sounds.events.specialAmbience).toBeUndefined()
        expect(sounds.cues['special-ambience'].source).toBe(arcaneMusicSource)
      }
    }
    for (const sounds of allSoundSets) {
      for (const cue of Object.values(sounds.cues)) {
        if (cue.source.startsWith('disabled:')) {
          expect(cue.enabled).toBe(false)
        } else {
          expect(documentedLegacySources.test(cue.source) || documentedSuiteSources.test(cue.source) ||
            (sounds === GOLD_FOUNDRY_SOUNDS && foundrySources.has(cue.source)) || cue.source === arcaneMusicSource, cue.source).toBe(true)
        }
      }
    }
  }, 30_000)

  it('gives Gold Foundry its own workshop ambience and button and reel cues', () => {
    const { cues, events } = GOLD_FOUNDRY_SOUNDS
    expect(cues.ambience.source).toBe(foundryAmbienceSource)
    expect(cues['special-ambience']).toBe(cues.ambience)
    expect(events).toMatchObject({
      buttonClick: 'button-click', betStep: 'button-click', stopButtonClick: 'button-click',
      spinButtonClick: 'spin-button-click', specialReelSpin: 'reel-spin', specialReelStop: 'reel-stop',
    })
    expect(cues['button-click']?.source).toBe(`${foundryAudioPath}/hammer-ring.wav`)
    expect(cues['spin-button-click']?.source).toBe(`${foundryAudioPath}/spin-button-metal-hit.wav`)
    expect(cues['lever-pull'].enabled).toBe(false)
    expect(cues['reel-spin']).toMatchObject({ source: `${foundryAudioPath}/forge-bellows-and-crackling-fire.wav`, loop: true })
    expect(cues['reel-stop'].source).toBe(`${foundryAudioPath}/reel-brake.wav`)
    expect(Object.values(cues).some(cue => /wukong/i.test(cue.source))).toBe(false)
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
            if (cue.enabled === false) {
              current().startLoop(cueId as keyof SlotSoundSet['cues'])
              current().playCue(cueId as keyof SlotSoundSet['cues'])
            }
          }
        })
        expect(audio).not.toHaveBeenCalled()
        expect(play).not.toHaveBeenCalled()
      }
      act(() => root.render(createElement(Harness, { soundSet: soundSets.DEFAULT_SLOT_SOUNDS })))
      act(() => current().playCue(soundSets.DEFAULT_SLOT_SOUNDS.events.leverPull))
      expect(audio).toHaveBeenCalledExactlyOnceWith(expect.stringContaining('chips-handle-1.ogg'))
      expect(play).toHaveBeenCalledOnce()

      act(() => root.render(createElement(Harness, { soundSet: REEL_RICHES_SOUNDS })))
      expect(audio).toHaveBeenCalledOnce()
      act(() => current().startLoop('special-ambience'))
      expect(audio).toHaveBeenCalledTimes(2)
      expect(audio).toHaveBeenLastCalledWith('/assets/slots/audio-suites/v1/reel-riches/midnight-mix.ogg')
      expect(play).toHaveBeenCalledTimes(2)
    } finally {
      act(() => root.unmount())
      host.remove()
      window.localStorage.clear()
      vi.unstubAllGlobals()
    }
  })
})
