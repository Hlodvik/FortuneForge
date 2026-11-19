// Edited: 2026-11-18T22:51:39-06:00 | Computer: Vinnabok
import { describe, expect, it } from 'vitest'
import { assertSiteModuleBoundary } from './siteBuildBoundary.mjs'

describe('site build boundary', () => {
  it.each([
    'games/ArcaneArchives/client/FortuneForge.Games.ArcaneArchives.Client/dist/index.js',
    'games/JadeDragon/client/jade-dragon/src/main.js',
    'packages/slot-game-runtime/src/index.ts',
    'fortuneforge.client/src/games/slots/routeRegistry.ts',
    'fortuneforge.client/src/games/slots/manifestLoaders/arcane-archives.ts',
    'fortuneforge.client/src/app/routes/BlackjackRoute.tsx',
    'node_modules/@fortuneforge/games-three-card-poker/dist/index.js',
  ])('rejects a playable dependency even when it is a lazy module: %s', path => {
    expect(() => assertSiteModuleBoundary([path])).toThrow('playable game modules')
    expect(() => assertSiteModuleBoundary([path.replaceAll('/', '\\')])).toThrow('playable game modules')
  })
  it('allows host catalogue metadata, artwork and account history', () => {
    expect(() => assertSiteModuleBoundary([
      'fortuneforge.client/src/games/slots/routeMetadata.ts',
      'fortuneforge.client/src/games/slots/catalog.ts',
      'fortuneforge.client/src/games/slots/goldFoundry/catalog.ts',
      'fortuneforge.client/src/games/cards/shared/cardRoomHistoryApi.ts',
      'fortuneforge.client/src/app/routes/AdminPortalRoute.tsx',
      'fortuneforge.client/src/assets/catalogue/slots/arcane-archives-title-v1.webp',
    ])).not.toThrow()
  })
})
