import { describe, expect, it } from 'vitest'
import { loadAllSlotGameManifests } from './routeRegistry'
import { createThemedSlotAudio } from '../../features/slots/config/themedSlotAudio'

describe('slot audio coverage', () => {
  it('gives standalone packages complete suites plus a dedicated Gold Foundry palette', async () => {
    // Neon verifies its package-owned audio through its independent client tests.
    const games = (await loadAllSlotGameManifests()).filter(game => game.id !== 'neon-nights')
    expect(games).toHaveLength(18)
    expect(games.filter(game => game.experience.sounds.events.themeSuite)).toHaveLength(15)
    const excluded = ['dragon-hoard', 'wukong-journey-to-the-west', 'pirates-fortune']
    for (const game of games) {
      const { cues, events } = game.experience.sounds
      if (excluded.includes(game.id)) {
        expect(events.themeSuite).toBeUndefined()
        expect(events.collectionArrival).toBeUndefined()
        expect(events.bonusComplete).toBeUndefined()
        expect(createThemedSlotAudio(game.id, game.experience.sounds)).toBe(game.experience.sounds)
        const requiredCueIds = [events.ambience, events.leverPull, events.reelSpin, events.reelStop,
          ...Object.values(events.results).flat()]
        expect(requiredCueIds.every(id => cues[id] !== undefined)).toBe(true)
        continue
      }
      if (game.id === 'gold-foundry') {
        const resultCues = [...Object.values(events.results).flat(), events.tripleSevens!]
        expect(resultCues).toHaveLength(5)
        expect(resultCues.every(id => /(?:win-small|win-medium|win-big|triple-sevens|no-win)\.ogg$/.test(cues[id]!.source))).toBe(true)
        expect(events.collectionArrival).toBeUndefined()
        expect(events.bonusComplete).toBeUndefined()
        continue
      }
      expect(events.autoSpinAmbience).toBeUndefined()
      expect(events.specialAmbience).toBeDefined()
      const musicIds = [events.ambience, ...(events.specialAmbience === undefined ? [] : [events.specialAmbience])]
      const ids = [...musicIds, events.buttonClick!, events.betStep!,
        events.leverPull, events.reelSpin, events.reelStop, events.specialReelSpin!, events.specialReelStop!,
        ...Object.values(events.collectionArrival!), ...Object.values(events.results).flat(),
        events.tripleSevens!, events.bonusComplete!]
      const sources = ids.map(id => cues[id]!.source)
      expect(new Set(sources).size).toBe(20)
      expect(sources.every(source => source.includes('assets/audio/'))).toBe(true)
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
