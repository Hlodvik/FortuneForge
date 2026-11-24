import { describe, expect, it } from 'vitest'
import { SLOT_GAME_CATALOG } from './catalog'
import { canonicalSlotPath, findSlotRouteMetadata } from './routeMetadata'

describe('Sunstone Eclipse catalogue identity', () => {
  it('publishes the new identity while preserving the existing player activity key', () => {
    const game = SLOT_GAME_CATALOG.find(entry => entry.id === 'robot-revolution')
    expect(game).toMatchObject({
      title: 'Sunstone Eclipse',
      shortTitle: 'Sunstone Eclipse',
      playHref: '/slots/sunstone-eclipse',
      demoHref: '/slots/sunstone-eclipse/demo',
      serverGameIds: ['robot-revolution-v1'],
    })
    expect(game?.image).toContain('sunstone-eclipse-title-v1.webp')
    expect(game?.description).toMatch(/Sunwheel/)
    expect(SLOT_GAME_CATALOG.some(entry => entry.title === 'Robot Revolution')).toBe(false)
    expect(SLOT_GAME_CATALOG).toHaveLength(20)
  })

  it('keeps paid and demo bookmarks separate when resolving the old game address', () => {
    expect(canonicalSlotPath('/slots/robot-revolution')).toBe('/slots/sunstone-eclipse')
    expect(canonicalSlotPath('/slots/robot-revolution/demo')).toBe('/slots/sunstone-eclipse/demo')
    expect(findSlotRouteMetadata('/slots/sunstone-eclipse')?.serverGameIds).toEqual(['robot-revolution-v1'])
  })

  it('preserves the other themed games and leaves unrelated routes alone', () => {
    for (const title of ['Jade Dragon: The Golden Pearl', 'Neon Nights', 'Arcane Archives']) {
      expect(SLOT_GAME_CATALOG.filter(entry => entry.title === title)).toHaveLength(1)
    }
    for (const path of ['/slots/jade-dragon', '/slots/neon-nights', '/slots/arcane-archives', '/home']) {
      expect(canonicalSlotPath(path)).toBe(path)
    }
  })
})
