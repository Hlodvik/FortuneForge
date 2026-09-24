import { describe, expect, it } from 'vitest'
import { PIRATES_FORTUNE_SOUNDS, WUKONG_TREASURES_SOUNDS } from './soundSets'

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
  it('uses dedicated voyage cues normally and a separate intense palette for a Broadside Run', () => {
    const eventSources = pirateEventCueIds.map((cueId) => PIRATES_FORTUNE_SOUNDS.cues[cueId].source)

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
      .every((cueId) => !PIRATES_FORTUNE_SOUNDS.cues[cueId].source.includes('pirate-')),
    ).toBe(true)
    expect(pirateCueSources.some((source) => source.includes('pirate-helm-spin'))).toBe(true)
  })
})

describe('Wukong sound set', () => {
  it('uses an original Wukong effects palette around its music bed', () => {
    const resultCueIds = Object.values(WUKONG_TREASURES_SOUNDS.events.results).flat()
    const effectCueIds = [
      WUKONG_TREASURES_SOUNDS.events.leverPull,
      WUKONG_TREASURES_SOUNDS.events.reelSpin,
      WUKONG_TREASURES_SOUNDS.events.reelStop,
      ...resultCueIds,
    ]
    const effectSources = effectCueIds.map((cueId) => WUKONG_TREASURES_SOUNDS.cues[cueId].source)

    expect(WUKONG_TREASURES_SOUNDS.events.ambience).toBe('ambience')
    expect(WUKONG_TREASURES_SOUNDS.cues.ambience.source).toContain('asian-dragon')
    expect(effectSources.every((source) => source.includes('wukong-'))).toBe(true)
    expect(WUKONG_TREASURES_SOUNDS.cues['lever-pull'].source)
      .not.toBe(WUKONG_TREASURES_SOUNDS.cues['reel-spin'].source)
    expect(WUKONG_TREASURES_SOUNDS.cues['reel-stop'].source)
      .not.toBe(WUKONG_TREASURES_SOUNDS.cues['soft-miss'].source)
    expect(WUKONG_TREASURES_SOUNDS.events.results['no-win']).toEqual(['soft-miss'])
  })
})
