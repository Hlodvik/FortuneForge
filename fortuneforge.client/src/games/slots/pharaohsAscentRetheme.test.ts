import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import { resolveGameLoadingEntry } from '../../features/game-loading/gameLoadingRegistry'
import { SLOT_GAME_CATALOG } from './catalog'
import { canonicalSlotPath, findSlotRouteMetadata } from './routeMetadata'
import { findSlotRoute } from './routeRegistry'

describe('Pharaoh’s Ascent catalogue replacement', () => {
  it('owns a fresh public, package and account identity', () => {
    const game = SLOT_GAME_CATALOG.find(entry => entry.id === 'pharaohs-ascent')
    expect(game).toMatchObject({
      title: 'Pharaoh’s Ascent',
      shortTitle: 'Pharaoh’s Ascent',
      playHref: '/slots/pharaohs-ascent',
      demoHref: null,
      serverGameIds: ['pharaohs-ascent-v1'],
    })
    expect(game?.image).toContain('pharaohs-ascent-title-v1.webp')
    expect(game?.description).toMatch(/pyramid/)
    expect(SLOT_GAME_CATALOG.filter(entry => entry.id === 'pharaohs-ascent')).toHaveLength(1)
    expect(SLOT_GAME_CATALOG.filter(entry => entry.title === 'Pharaoh’s Ascent')).toHaveLength(1)
    const loader = readFileSync(new URL('./manifestLoaders/pharaohs-ascent.ts', import.meta.url), 'utf8')
    expect(loader).toContain("import('@fortuneforge/games-pharaohs-ascent/manifest')")
  })

  it('exposes only its authenticated paid route', () => {
    expect(findSlotRouteMetadata('/slots/pharaohs-ascent')).toMatchObject({
      id: 'pharaohs-ascent', demoPath: null, serverGameIds: ['pharaohs-ascent-v1'],
    })
    expect(findSlotRoute('/slots/pharaohs-ascent/demo')).toBeNull()
    expect(findSlotRouteMetadata('/slots/pharaohs-ascent/demo')).toBeNull()
    expect(canonicalSlotPath('/slots/pharaohs-ascent')).toBe('/slots/pharaohs-ascent')
  })

  it('uses matching Egyptian catalogue and loading artwork without a public demo entry', () => {
    const art = getCataloguePresentationArt('/slots/pharaohs-ascent')
    expect(art?.src).toContain('pharaohs-ascent-title-v1.webp')
    expect(art).toMatchObject({ width: 768, height: 384 })
    expect(resolveGameLoadingEntry('/slots/pharaohs-ascent')).toMatchObject({
      id: 'pharaohs-ascent', title: 'Pharaoh’s Ascent', routes: ['/slots/pharaohs-ascent'], image: art?.src,
    })
    expect(resolveGameLoadingEntry('/slots/pharaohs-ascent/demo')).toBeUndefined()
    expect(getCataloguePresentationArt('/slots/pharaohs-ascent/demo')).toBeUndefined()
  })
})
