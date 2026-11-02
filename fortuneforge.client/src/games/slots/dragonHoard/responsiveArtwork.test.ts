import { describe, expect, it } from 'vitest'
import { getSlotAnimationSymbolIds } from '../../../features/slots/config/symbolSets'
import { DRAGON_HOARD_DEFINITION } from './definition'
import { DRAGON_HOARD_SLOT_GAME } from './manifest'
import { DRAGON_HOARD_RESPONSIVE_IMAGES, DRAGON_HOARD_SYMBOL_IMAGES } from './symbols'

describe('Midnight Vault artwork on the historical Dragon Hoard game', () => {
  it('preserves game identity, routes, award counts and server feature contracts', () => {
    const game = DRAGON_HOARD_SLOT_GAME
    expect(game.id).toBe('dragon-hoard')
    expect(game.routes).toEqual({ play: '/slots/dragon-hoard', demo: '/slots/dragon-hoard/demo' })
    expect(game.experience.rules.gameId).toBe('dragon-hoard-v1')
    expect(game.experience.symbols.serverSymbolSetId).toBe('midnight-vault-symbols-v1')
    expect(game.experience.features.specialRound?.id).toBe('dragon-ember-siege')
    expect(game.experience.sounds.id).toBe('dragon-hoard-audio-v1')
    expect(game.experience.help.paylinePatternIds).toEqual(Array.from({ length: 23 }, (_, index) => index + 1))
    expect(game.experience.help.freeGames).toMatchObject({ requiredSymbols: 3, awardedSpins: 10 })
    expect(game.experience.features.collections?.entries.map((entry) => entry.requiredCount)).toEqual([30, 30, 30, 30])
    expect(DRAGON_HOARD_DEFINITION.specialRound?.collectionAwardedSpins).toBe(10)
    expect(game.experience.features.energy).toBeUndefined()
    expect(game.experience.features.moneyGrab).toBeUndefined()
  })

  it('gives all fifteen active reel symbols distinct approved art with measured width candidates', () => {
    expect(Object.keys(DRAGON_HOARD_RESPONSIVE_IMAGES)).toHaveLength(15)
    for (const [fallback, variants] of Object.entries(DRAGON_HOARD_RESPONSIVE_IMAGES)) {
      expect(fallback).toMatch(/^\/assets\/slots\/midnight-vault\/v4\/symbols\/96\/midnight-vault-.+\.webp$/)
      expect(variants).toEqual({
        96: fallback,
        192: fallback.replace('/96/', '/192/'),
        320: fallback.replace('/96/', '/320/'),
      })
    }
    expect(DRAGON_HOARD_DEFINITION.responsiveImages).toEqual(DRAGON_HOARD_RESPONSIVE_IMAGES)
    for (const symbol of Object.values(DRAGON_HOARD_SLOT_GAME.experience.symbols.definitions)) {
      expect(symbol?.responsiveImages).toEqual(DRAGON_HOARD_RESPONSIVE_IMAGES[symbol!.image])
    }
    expect(DRAGON_HOARD_SYMBOL_IMAGES.BACKDROP).toContain('/midnight-vault-backdrop-1440x810.webp')
    expect(DRAGON_HOARD_SLOT_GAME.catalog.title).toBe('Midnight Vault')
    const activeSymbols = DRAGON_HOARD_SLOT_GAME.experience.symbols.guideEntries.map(entry => entry.symbol)
    expect(activeSymbols).toHaveLength(15)
    expect(activeSymbols).toContain('8')
    expect(getSlotAnimationSymbolIds(DRAGON_HOARD_SLOT_GAME.experience.symbols)).toEqual(activeSymbols)
    expect(getSlotAnimationSymbolIds(DRAGON_HOARD_SLOT_GAME.experience.symbols)).not.toContain('BOLT')
    expect(getSlotAnimationSymbolIds(DRAGON_HOARD_SLOT_GAME.experience.symbols)).not.toContain('PAW')
    expect(new Set(activeSymbols.map(id => DRAGON_HOARD_SLOT_GAME.experience.symbols.definitions[id]!.image)).size).toBe(15)
    expect(DRAGON_HOARD_SLOT_GAME.experience.symbols.definitions['8']?.label).toBe('Cash briefcase')
    expect(DRAGON_HOARD_SYMBOL_IMAGES['8']).toContain('cash-briefcase.webp')
    expect(DRAGON_HOARD_SYMBOL_IMAGES.BANANA).toContain('triple-sevens.webp')
    expect(Object.keys(DRAGON_HOARD_RESPONSIVE_IMAGES).join(' ')).not.toContain('access-pulse')
  })
})
