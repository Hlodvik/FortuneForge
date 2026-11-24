import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { SLOT_GAME_CATALOG } from './catalog'
import { canonicalSlotPath, findSlotRouteMetadata } from './routeMetadata'
import { findSlotRoute } from './routeRegistry'

describe('Pharaoh’s Ascent catalogue replacement', () => {
  it('replaces the public identity while retaining the existing account and package key', () => {
    const game = SLOT_GAME_CATALOG.find(entry => entry.id === 'cosmic-fortune')
    expect(game).toMatchObject({
      title: 'Pharaoh’s Ascent',
      shortTitle: 'Pharaoh’s Ascent',
      playHref: '/slots/pharaohs-ascent',
      demoHref: null,
      serverGameIds: ['cosmic-fortune-v1'],
    })
    expect(game?.image).toContain('pharaohs-ascent-title-v1.webp')
    expect(game?.description).toMatch(/pyramid/)
    expect(SLOT_GAME_CATALOG.filter(entry => entry.id === 'cosmic-fortune')).toHaveLength(1)
    expect(SLOT_GAME_CATALOG.some(entry => entry.title === 'Cosmic Fortune')).toBe(false)
    const loader = readFileSync(new URL('./manifestLoaders/cosmic-fortune.ts', import.meta.url), 'utf8')
    expect(loader).toContain("import('@fortuneforge/games-cosmic-fortune/manifest')")
  })

  it('sends both historical bookmarks to the authenticated paid route', () => {
    for (const legacy of ['/slots/cosmic-fortune', '/slots/cosmic-fortune/demo']) {
      expect(canonicalSlotPath(legacy)).toBe('/slots/pharaohs-ascent')
      expect(findSlotRoute(legacy)).toBeNull()
    }
    expect(findSlotRouteMetadata('/slots/pharaohs-ascent')).toMatchObject({
      id: 'cosmic-fortune', demoPath: null, serverGameIds: ['cosmic-fortune-v1'],
    })
    expect(findSlotRoute('/slots/pharaohs-ascent/demo')).toBeNull()
    expect(findSlotRouteMetadata('/slots/pharaohs-ascent/demo')).toBeNull()
    expect(canonicalSlotPath('/slots/pharaohs-ascent')).toBe('/slots/pharaohs-ascent')
  })

  it('uses matching Egyptian catalogue and loading artwork without a public demo entry', () => {
    const art = getCataloguePresentationArt('/slots/pharaohs-ascent')
    expect(art?.src).toContain('pharaohs-ascent-title-v1.webp')
    expect(art).toMatchObject({ width: 768, height: 384 })
    for (const legacy of ['/slots/cosmic-fortune', '/slots/cosmic-fortune/demo']) {
      expect(getCataloguePresentationArt(legacy)).toBe(art)
    }
    expect(resolveGameLoadingEntry('/slots/pharaohs-ascent')).toMatchObject({
      id: 'cosmic-fortune', title: 'Pharaoh’s Ascent', routes: ['/slots/pharaohs-ascent'], image: art?.src,
    })
    expect(resolveGameLoadingEntry('/slots/pharaohs-ascent/demo')).toBeUndefined()
    expect(getCataloguePresentationArt('/slots/pharaohs-ascent/demo')).toBeUndefined()
  })
})
