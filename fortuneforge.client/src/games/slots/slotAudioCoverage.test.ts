import { describe, expect, it } from 'vitest'
import { loadAllSlotGameManifests } from './routeRegistry'

describe('slot audio coverage', () => {
  it('gives every non-Pirates slot cabinet a looping ambient source', async () => {
    const games = await loadAllSlotGameManifests()
    const nonPiratesGames = games.filter((game) => game.id !== 'pirates-fortune')

    expect(nonPiratesGames).toHaveLength(games.length - 1)

    for (const game of nonPiratesGames) {
      const ambienceCueId = game.experience.sounds.events.ambience
      const ambienceCue = game.experience.sounds.cues[ambienceCueId]

      expect(ambienceCue.source.length).toBeGreaterThan(0)
      expect(ambienceCue.baseVolume).toBeGreaterThan(0)
      expect(ambienceCue.loop).toBe(true)
    }
  }, 15_000)
})
