import { describe, expect, it } from 'vitest'
import { WUKONG_FEATURE_SYMBOL_IDS } from '../config/symbolSets'
import type { SlotSymbolId } from '../types/slots'
import { getSlotSymbolValueLabel, slotPointsToRand } from '../slotPagePresentation'
import { RAINBOW_REALM_SLOT_GAME, SLOT_GAME_MANIFESTS, WUKONG_SLOT_GAME } from '.'
import { createSlotExperienceRouteMap } from './slotGameManifest'

function expectDefinedSymbol(gameId: string, symbol: SlotSymbolId) {
  const game = SLOT_GAME_MANIFESTS.find((candidate) => candidate.id === gameId)
  expect(game, `missing game ${gameId}`).toBeDefined()
  expect(game?.experience.symbols.definitions[symbol], `${gameId} is missing ${symbol}`).toBeDefined()
}

describe('slot game manifests', () => {
  it('keeps game, catalog, experience, and route identifiers unique', () => {
    const gameIds = SLOT_GAME_MANIFESTS.map((game) => game.id)
    const catalogIds = SLOT_GAME_MANIFESTS.map((game) => game.catalog.id)
    const experienceIds = SLOT_GAME_MANIFESTS.map((game) => game.experience.id)
    const routes = SLOT_GAME_MANIFESTS.flatMap((game) =>
      [game.routes.play, game.routes.demo].filter((route): route is string => route !== null),
    )

    expect(new Set(gameIds).size).toBe(gameIds.length)
    expect(new Set(catalogIds).size).toBe(catalogIds.length)
    expect(new Set(experienceIds).size).toBe(experienceIds.length)
    expect(new Set(routes).size).toBe(routes.length)
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/wukong/demo')
  })

  it('defines every symbol referenced by rules, help, and optional features', () => {
    for (const game of SLOT_GAME_MANIFESTS) {
      for (const reel of game.experience.rules.initialReels) {
        for (const symbol of reel) expectDefinedSymbol(game.id, symbol)
      }
      for (const entry of game.experience.symbols.guideEntries) {
        expectDefinedSymbol(game.id, entry.symbol)
      }

      const { collections, energy, moneyGrab } = game.experience.features
      if (energy) expectDefinedSymbol(game.id, energy.symbol)
      if (moneyGrab) expectDefinedSymbol(game.id, moneyGrab.collectorSymbol)
      for (const collection of collections?.entries ?? []) {
        expectDefinedSymbol(game.id, collection.symbol)
      }
    }
  })

  it('keeps seals, paw grabs, Rand tokens, and their help exclusive to Wukong', () => {
    expect(WUKONG_SLOT_GAME.experience.features.collections?.entries).toHaveLength(4)
    expect(WUKONG_SLOT_GAME.experience.features.collections?.entries.every(
      (collection) => collection.requiredCount === 40,
    )).toBe(true)
    expect(WUKONG_SLOT_GAME.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
    expect(WUKONG_SLOT_GAME.experience.help.extraSections).toHaveLength(2)

    expect(RAINBOW_REALM_SLOT_GAME.experience.features.collections).toBeUndefined()
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.moneyGrab).toBeUndefined()
    expect(RAINBOW_REALM_SLOT_GAME.experience.help.extraSections).toBeUndefined()
    for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
      expect(WUKONG_SLOT_GAME.experience.symbols.definitions[symbol]).toBeDefined()
      expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions[symbol]).toBeUndefined()
    }
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.RAND_05?.wagerMultiplier).toBe(0.5)
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.RAND_5?.wagerMultiplier).toBe(5)
    expect(getSlotSymbolValueLabel(
      WUKONG_SLOT_GAME.experience.symbols.definitions.RAND_05!,
      50,
    )).toBe('R25')
    expect(getSlotSymbolValueLabel(
      WUKONG_SLOT_GAME.experience.symbols.definitions.RAND_5!,
      50,
    )).toBe('R250')
  })

  it('offers wagers from R0.50 through R500 in R0.50 steps', () => {
    const { pointValueInCents, wagerOptions } = WUKONG_SLOT_GAME.experience.rules
    const randValues = wagerOptions.map((points) => slotPointsToRand(points, pointValueInCents))

    expect(randValues[0]).toBe(0.5)
    expect(randValues.at(-1)).toBe(500)
    expect(slotPointsToRand(1, pointValueInCents)).toBe(0.25)
    expect(randValues.every((value, index) => index === 0 || value - randValues[index - 1] === 0.5)).toBe(true)
  })

  it('rejects duplicate routes before the application starts', () => {
    expect(() => createSlotExperienceRouteMap([
      WUKONG_SLOT_GAME,
      { ...RAINBOW_REALM_SLOT_GAME, routes: { ...RAINBOW_REALM_SLOT_GAME.routes, demo: '/slots/wukong/demo' } },
    ])).toThrow("Duplicate slot route '/slots/wukong/demo'.")
  })
})
