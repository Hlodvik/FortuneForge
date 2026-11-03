import { describe, expect, it } from 'vitest'
import { loadAllSlotGameManifests } from './routeRegistry'
import { createThemedSlotAudio } from '../../features/slots/config/themedSlotAudio'
import { DEFAULT_SLOT_SOUNDS } from '../../features/slots/config/soundSets'

describe('slot audio coverage', () => {
  it('gives sixteen titles complete suites plus a dedicated Gold Foundry palette', async () => {
    const games = await loadAllSlotGameManifests()
    expect(games).toHaveLength(20)
    expect(games.filter(game => game.experience.sounds.events.themeSuite)).toHaveLength(17)
    const excluded = ['dragon-hoard', 'wukong-journey-to-the-west', 'pirates-fortune']
    for (const game of games) {
      const { cues, events } = game.experience.sounds
      if (excluded.includes(game.id)) {
        expect(events.themeSuite).toBeUndefined()
        expect(events.collectionArrival).toBeUndefined()
        expect(events.bonusComplete).toBeUndefined()
        expect(createThemedSlotAudio(game.id, game.experience.sounds)).toBe(game.experience.sounds)
        expect(Object.keys(cues).sort()).toEqual(Object.keys(DEFAULT_SLOT_SOUNDS.cues).sort())
        continue
      }
      if (game.id === 'gold-foundry') {
        const resultCues = [...Object.values(events.results).flat(), events.tripleSevens!]
        expect(resultCues).toHaveLength(5)
        expect(resultCues.every(id => cues[id]!.source.startsWith('/assets/slots/gold-foundry/v1/audio/'))).toBe(true)
        expect(events.collectionArrival).toBeUndefined()
        expect(events.bonusComplete).toBeUndefined()
        continue
      }
      expect(events.autoSpinAmbience).toBeUndefined()
      if (game.id === 'arcane-archives') expect(events.specialAmbience).toBeUndefined()
      else expect(events.specialAmbience).toBeDefined()
      const musicIds = [events.ambience, ...(events.specialAmbience === undefined ? [] : [events.specialAmbience])]
      const ids = [...musicIds, events.buttonClick!, events.betStep!,
        events.leverPull, events.reelSpin, events.reelStop, events.specialReelSpin!, events.specialReelStop!,
        ...Object.values(events.collectionArrival!), ...Object.values(events.results).flat(),
        events.tripleSevens!, events.bonusComplete!]
      const sources = ids.map(id => cues[id]!.source)
      const expectedSourceCount = { 'arcane-archives': 19, 'neon-nights': 17 } as Record<string, number>
      expect(new Set(sources).size).toBe(expectedSourceCount[game.id] ?? 20)
      expect(sources.every(source => source.startsWith(`/assets/slots/audio-suites/v1/${game.id}/`))).toBe(true)
      for (const id of ids) {
        expect(cues[id]!.enabled).toBe(true)
      }
      for (const id of musicIds) {
        expect(cues[id]!.loop).toBe(true)
        expect(cues[id]!.baseVolume).toBeGreaterThan(0)
      }
      expect(Object.values(events.results).every(ids => ids.length === 1)).toBe(true)
      expect(Object.values(events.specialResults!).every(ids => ids.length === 1)).toBe(true)
    }
  }, 30_000)
})
