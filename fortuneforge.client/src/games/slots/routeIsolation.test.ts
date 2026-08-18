/// <reference types="node" />

import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { SLOT_GAME_CATALOG } from './catalog'
import { SLOT_ROUTE_DEFINITIONS } from './routeRegistry'

describe('slot route resource isolation', () => {
  it('keeps lightweight route and catalog registries in lockstep', () => {
    expect(SLOT_GAME_CATALOG.map((game) => game.id)).toEqual(
      SLOT_ROUTE_DEFINITIONS.map((route) => route.id),
    )
    for (const game of SLOT_GAME_CATALOG) {
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

    expect(appRoutesSource).not.toMatch(/^import .*\.\.\/pages\//m)
    expect(appRoutesSource).toContain("lazy(() => import('../pages/")
    expect(appSource).not.toContain('SLOT_EXPERIENCE_SETS_BY_ROUTE')
    expect(slotIndexSource).not.toMatch(/^import .*\/manifest/m)
    expect(routeRegistrySource).not.toMatch(/^import .*\/manifest/m)
    expect(routeRegistrySource).toContain("load: () => import('./wukong/manifest')")
    expect(catalogSource).not.toContain('/manifest')
  })
})

function source(relativePath: string): string {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8')
}
