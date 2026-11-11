/// <reference types="node" />

import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { SLOT_GAME_CATALOG } from './catalog'
import { SLOT_ROUTE_DEFINITIONS } from './routeRegistry'

describe('slot route resource isolation', () => {
  it('keeps generic routes in lockstep with the independently embedded Jade Dragon client', () => {
    const jade = SLOT_GAME_CATALOG.find(game => game.id === 'empty-slots-game')!
    expect(jade).toMatchObject({ title: 'Jade Dragon: The Golden Pearl', shortTitle: 'Jade Dragon',
      description: expect.stringContaining('jade sanctuary'),
      playHref: '/slots/jade-dragon', demoHref: '/slots/jade-dragon', serverGameIds: ['empty-slots-v1'] })
    expect(jade.image).toMatch(/jade-dragon-title-v1\.webp$/)
    expect(SLOT_ROUTE_DEFINITIONS.some(route => route.id === jade.id)).toBe(false)
    const playableCatalog = SLOT_GAME_CATALOG.filter(game => game.id !== jade.id)
    expect(playableCatalog.map((game) => game.id)).toEqual(
      SLOT_ROUTE_DEFINITIONS.map((route) => route.id),
    )
    for (const game of playableCatalog) {
      const route = SLOT_ROUTE_DEFINITIONS.find((candidate) => candidate.id === game.id)
      expect(route).toBeDefined()
      expect(game.playHref).toBe(route?.playPath)
      expect(game.demoHref).toBe(route?.demoPath)
      expect(game.serverGameIds).toEqual(route?.serverGameIds)
    }
  })

  it('does not restore eager page or full-manifest imports in entry registries', () => {
    const appRoutesSource = source('../../app/AppRoutes.tsx')
    const appSource = source('../../app/App.tsx')
    const slotIndexSource = source('./index.ts')
    const routeRegistrySource = source('./routeRegistry.ts')
    const catalogSource = source('./catalog.ts')
    const catalogLoadersSource = source('./catalogLoaders.ts')

    expect(appRoutesSource).not.toMatch(/^import .*\.\.\/pages\//m)
    expect(appRoutesSource).toContain("lazy(() => import('../pages/")
    expect(appSource).not.toContain('SLOT_EXPERIENCE_SETS_BY_ROUTE')
    expect(slotIndexSource).not.toMatch(/^import .*\/manifest/m)
    expect(routeRegistrySource).not.toMatch(/^import .*\/manifest/m)
    expect(routeRegistrySource).not.toContain('@fortuneforge/games-')
    expect(existsSync(new URL('./slotManifestLoaders.ts', import.meta.url))).toBe(false)
    for (const route of SLOT_ROUTE_DEFINITIONS) {
      const loaderPath = `./manifestLoaders/${route.id}.ts`
      const loaderSource = source(loaderPath)
      expect(routeRegistrySource).toContain(`import('./manifestLoaders/${route.id}')`)
      expect(loaderSource.match(/\bimport\(/g)).toHaveLength(1)
      expect(loaderSource).toContain('export const loadSlotManifest')
    }
    expect(catalogSource).not.toContain('/manifest')
    expect(catalogLoadersSource).not.toContain('/manifest')
    expect(catalogLoadersSource).not.toContain('@fortuneforge/games-')
  })
})

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
