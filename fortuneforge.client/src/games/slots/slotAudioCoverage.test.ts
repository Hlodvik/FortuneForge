import { describe, expect, it } from 'vitest'
import { loadAllSlotGameManifests } from './routeRegistry'
import { isGameUnderMaintenance } from '../../features/catalog/gameMaintenance'
import { SLOT_GAME_CATALOG } from './catalog'

const references = (value: unknown): string[] => typeof value === 'string' ? [value]
  : Array.isArray(value) ? value.flatMap(references) : value && typeof value === 'object' ? Object.values(value).flatMap(references) : []

describe('active slot audio metadata', () => {
  it('keeps every standard cue reference playable without imposing one game’s arrangement on another', async () => {
    // Neon and Frontier own different audio controllers, verified by their package suites.
    // Maintenance entries are explicitly unavailable and are not certified as functional games.
    const active = (await loadAllSlotGameManifests()).filter(game => !['neon-nights', 'high-noon-fortune'].includes(game.id) &&
      !isGameUnderMaintenance(SLOT_GAME_CATALOG.find(entry => entry.id === game.id)!))
    expect(active.map(game => game.id).sort()).toEqual(['arcane-archives','crooked-road','dragon-hoard','gold-foundry','pharaohs-ascent','pirates-fortune','robot-revolution'])
    for (const game of active) {
      const { cues, events } = game.experience.sounds
      expect(events, game.id).toBeDefined()
      expect(events.results, game.id).toBeDefined()
      for (const id of new Set(references(events))) {
        expect(cues[id], `${game.id}: ${id}`).toBeDefined()
        expect(cues[id]!.source, `${game.id}: ${id}`).toEqual(expect.any(String))
      }
      for (const [id, cue] of Object.entries(cues)) if (cue) {
        expect(cue.source.length, `${game.id}: ${id}`).toBeGreaterThan(0)
        expect(cue.baseVolume, `${game.id}: ${id}`).toBeGreaterThanOrEqual(0)
        expect(cue.baseVolume, `${game.id}: ${id}`).toBeLessThanOrEqual(1)
        expect(['effect','result']).toContain(cue.category)
      }
    }
  }, 30000)
})
