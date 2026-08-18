import { createElement, createRef } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { WinHelpDialog } from '../../features/slots/WinHelpDialog'
import { WUKONG_FEATURE_SYMBOL_IDS } from './wukong/symbols'
import type { SlotSymbolId } from '../../features/slots/types/slots'
import { getSlotSymbolValueLabel, slotPointsToRand } from '../../features/slots/slotPagePresentation'
import {
  loadAllSlotGameManifests,
  SECOND_WAVE_SLOT_GAME_IDS,
  SLOT_ROUTE_DEFINITIONS,
} from '.'
import { createSlotExperienceRouteMap, type SlotGameManifest } from './shared/slotGameManifest'

const SLOT_GAME_MANIFESTS = await loadAllSlotGameManifests()

function requireGame(id: string): SlotGameManifest {
  const game = SLOT_GAME_MANIFESTS.find((candidate) => candidate.id === id)
  if (!game) throw new Error(`Missing test game '${id}'.`)
  return game
}

const WUKONG_SLOT_GAME = requireGame('wukong-journey-to-the-west')
const RAINBOW_REALM_SLOT_GAME = requireGame('rainbow-realm')
const PIRATES_FORTUNE_SLOT_GAME = requireGame('pirates-fortune')
const GODS_OF_OLYMPUS_SLOT_GAME = requireGame('gods-of-olympus')
const REEL_RICHES_SLOT_GAME = requireGame('reel-riches')
const HIGH_NOON_FORTUNE_SLOT_GAME = requireGame('high-noon-fortune')
const ROYAL_DRAW_SLOT_GAME = requireGame('royal-draw')
const ARCANE_ARCHIVES_SLOT_GAME = requireGame('arcane-archives')
const COSMIC_FORTUNE_SLOT_GAME = requireGame('cosmic-fortune')
const DINO_DOMINION_SLOT_GAME = requireGame('dino-dominion')
const SECOND_WAVE_SLOT_GAMES = SECOND_WAVE_SLOT_GAME_IDS.map(requireGame)

function expectDefinedSymbol(gameId: string, symbol: SlotSymbolId) {
  const game = SLOT_GAME_MANIFESTS.find((candidate) => candidate.id === gameId)
  expect(game, `missing game ${gameId}`).toBeDefined()
  expect(game?.experience.symbols.definitions[symbol], `${gameId} is missing ${symbol}`).toBeDefined()
}

describe('slot game manifests', () => {
  it('maps every lightweight route definition to exactly one matching manifest loader', async () => {
    expect(SLOT_ROUTE_DEFINITIONS).toHaveLength(20)
    for (const route of SLOT_ROUTE_DEFINITIONS) {
      const game = await route.load()
      expect(game.id).toBe(route.id)
      expect(game.catalog.title).toBe(route.title)
      expect(game.catalog.shortTitle).toBe(route.shortTitle)
      expect(game.routes.play).toBe(route.playPath)
      expect(game.routes.demo).toBe(route.demoPath)
      expect(game.experience.shellBackdrop).toBe(route.shellBackdrop)
      expect(route.serverGameIds).toContain(game.experience.rules.gameId)
    }
  })

  it('keeps game, catalog, experience, and route identifiers unique', () => {
    const gameIds = SLOT_GAME_MANIFESTS.map((game) => game.id)
    const catalogIds = SLOT_GAME_MANIFESTS.map((game) => game.catalog.id)
    const experienceIds = SLOT_GAME_MANIFESTS.map((game) => game.experience.id)
    const serverGameIds = SLOT_GAME_MANIFESTS.map((game) => game.experience.rules.gameId)
    const serverSymbolSetIds = SLOT_GAME_MANIFESTS.map((game) =>
      game.experience.symbols.serverSymbolSetId ?? game.experience.symbols.id)
    const paylineCounts = SLOT_GAME_MANIFESTS.map((game) => game.experience.help.paylineCount)
    const routes = SLOT_GAME_MANIFESTS.flatMap((game) =>
      [game.routes.play, game.routes.demo].filter((route): route is string => route !== null),
    )

    expect(new Set(gameIds).size).toBe(gameIds.length)
    expect(new Set(catalogIds).size).toBe(catalogIds.length)
    expect(new Set(experienceIds).size).toBe(experienceIds.length)
    expect(new Set(serverGameIds).size).toBe(serverGameIds.length)
    expect(new Set(serverSymbolSetIds).size).toBe(serverSymbolSetIds.length)
    expect([...paylineCounts].sort((left, right) => left - right)).toEqual([
      14, 14, 15, 15, 16, 16, 17, 17, 18, 18,
      19, 19, 20, 20, 21, 21, 22, 22, 23, 23,
    ])
    for (const game of SLOT_GAME_MANIFESTS) {
      const patternIds = game.experience.help.paylinePatternIds ??
        Array.from({ length: game.experience.help.paylineCount }, (_, index) => index + 1)
      expect(patternIds).toHaveLength(game.experience.help.paylineCount)
      expect(new Set(patternIds).size).toBe(patternIds.length)
      expect(patternIds.every((id) => id >= 1 && id <= 23)).toBe(true)
    }
    expect(new Set(routes).size).toBe(routes.length)
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/wukong/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/pirates-fortune/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/gods-of-olympus/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/reel-riches/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/high-noon-fortune/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/royal-draw/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/arcane-archives/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/cosmic-fortune/demo')
    expect(createSlotExperienceRouteMap(SLOT_GAME_MANIFESTS)).toHaveProperty('/slots/dino-dominion/demo')
  })

  it('registers four complete new themed games with distinct collectors', () => {
    const themedGames = [
      [GODS_OF_OLYMPUS_SLOT_GAME, 'Gauntlet of Zeus', 'divine-offering'],
      [REEL_RICHES_SLOT_GAME, 'fishing net', 'tackle-creel'],
      [HIGH_NOON_FORTUNE_SLOT_GAME, 'golden lasso', 'frontier-trail'],
      [ROYAL_DRAW_SLOT_GAME, 'dealer chip tray', 'chip-stack'],
    ] as const

    for (const [game, actorName, presentation] of themedGames) {
      expect(game.routes.play).not.toBeNull()
      expect(game.experience.features.moneyGrab?.actorName).toContain(actorName)
      expect(game.experience.features.collections?.presentation).toBe(presentation)
      expect(game.experience.features.collections?.entries).toHaveLength(4)
      expect(game.experience.features.collections?.entries.every(
        (collection) => collection.requiredCount === 40,
      )).toBe(true)
      expect(game.experience.help.extraSections).toHaveLength(2)
      expect(game.experience.symbols.guideEntries).toHaveLength(16)
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }
  })

  it('registers three additional playable themes with distinct special-game presentations', () => {
    const newGames = [
      [ARCANE_ARCHIVES_SLOT_GAME, 'enchanted book satchel', 'spellbook-shelf'],
      [COSMIC_FORTUNE_SLOT_GAME, 'tractor-beam saucer', 'star-orbit'],
      [DINO_DOMINION_SLOT_GAME, 'paleontologist field kit', 'fossil-dig'],
    ] as const

    expect(SLOT_GAME_MANIFESTS).toHaveLength(20)
    for (const [game, actorName, presentation] of newGames) {
      expect(game.routes.play).toBe(`/slots/${game.id}`)
      expect(game.routes.demo).toBe(`/slots/${game.id}/demo`)
      expect(game.experience.features.moneyGrab?.actorName).toContain(actorName)
      expect(game.experience.features.collections?.presentation).toBe(presentation)
      expect(game.experience.features.collections?.entries).toHaveLength(4)
      expect(game.experience.help.extraSections).toHaveLength(2)
      expect(game.experience.symbols.guideEntries).toHaveLength(16)
      expect(new Set(
        Object.values(game.experience.symbols.definitions)
          .flatMap((definition) => definition ? [definition.label] : []),
      ).size).toBe(23)
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }
  })

  it('registers ten second-wave games with complete distinct themed contracts', () => {
    expect(SECOND_WAVE_SLOT_GAMES).toHaveLength(10)
    expect(new Set(SECOND_WAVE_SLOT_GAMES.map((game) => game.id)).size).toBe(10)

    for (const game of SECOND_WAVE_SLOT_GAMES) {
      expect(game.routes.play).toBe(`/slots/${game.id}`)
      expect(game.routes.demo).toBe(`/slots/${game.id}/demo`)
      expect(game.experience.cabinet.backdropImage).toContain('data:image/svg+xml')
      expect(game.experience.features.collections?.entries).toHaveLength(4)
      expect(game.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
      expect(game.experience.help.extraSections).toHaveLength(2)
      expect(game.experience.symbols.guideEntries).toHaveLength(16)
      expect(new Set(
        Object.values(game.experience.symbols.definitions)
          .flatMap((definition) => definition ? [definition.label] : []),
      ).size).toBe(23)
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }
  })

  it('maps the pirate skin to gems and a skull-and-crossbones collector', () => {
    const { collections, moneyGrab } = PIRATES_FORTUNE_SLOT_GAME.experience.features

    expect(collections?.ariaLabel).toBe('Treasure gem collections')
    expect(collections?.itemLabel).toBe('gems')
    expect(collections?.entries.map((collection) => collection.id)).toEqual([
      'sync',
      'rows',
      'paw',
      'rand',
    ])
    expect(collections?.entries.map((collection) => collection.shortLabel)).toEqual([
      'Ruby',
      'Sapphire',
      'Amber',
      'Emerald',
    ])
    expect(collections?.entries.every((collection) => collection.requiredCount === 40)).toBe(true)
    expect(moneyGrab?.collectorSymbol).toBe('PAW')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.PAW?.label).toContain('Skull')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.SEAL_SYNC?.label).toContain('gem')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.PAW?.image).not.toBe(
      WUKONG_SLOT_GAME.experience.symbols.definitions.PAW?.image,
    )
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

  it('renders only the winning-line patterns configured for the game', () => {
    const markup = renderToStaticMarkup(createElement(WinHelpDialog, {
      isOpen: true,
      closeButtonRef: createRef<HTMLButtonElement>(),
      help: PIRATES_FORTUNE_SLOT_GAME.experience.help,
      symbolSet: PIRATES_FORTUNE_SLOT_GAME.experience.symbols,
      onClose: () => undefined,
    }))
    const renderedPaylines = markup.match(/aria-label="Valid five-symbol payline \d+"/g) ?? []

    expect(renderedPaylines).toHaveLength(21)
    expect(markup).toContain('aria-label="Valid five-symbol payline 23"')
    expect(markup).not.toContain('aria-label="Valid five-symbol payline 21"')
    expect(markup).not.toContain('aria-label="Valid five-symbol payline 22"')
  })

  it('gives Rainbow Realm a complete fruit-specific feature set and wicker basket collector', () => {
    const collections = WUKONG_SLOT_GAME.experience.features.collections?.entries
    expect(collections).toHaveLength(4)
    expect(collections?.map((collection) => collection.label)).toEqual([
      'Synced reels',
      'Extra rows',
      'Monkey paw',
      'Rand column',
    ])
    expect(collections?.every(
      (collection) => collection.requiredCount === 40,
    )).toBe(true)
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.POWER?.image).not.toBe(
      WUKONG_SLOT_GAME.experience.symbols.definitions.SEAL_SYNC?.image,
    )
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.POWER?.label.toLowerCase()).not.toContain('hammer')
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.POWER?.label).toContain('Nimbus')
    expect(WUKONG_SLOT_GAME.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
    expect(WUKONG_SLOT_GAME.experience.help.extraSections).toHaveLength(2)

    const rainbowCollections = RAINBOW_REALM_SLOT_GAME.experience.features.collections
    expect(rainbowCollections?.ariaLabel).toBe('Orchard charm collections')
    expect(rainbowCollections?.presentation).toBe('juice-glass')
    expect(rainbowCollections?.entries.map((collection) => collection.id)).toEqual([
      'sync',
      'rows',
      'paw',
      'rand',
    ])
    expect(rainbowCollections?.entries.every((collection) => collection.requiredCount === 40)).toBe(true)
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions.PAW?.label).toContain('Wicker')
    expect(RAINBOW_REALM_SLOT_GAME.experience.help.extraSections).toHaveLength(2)
    for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
      expect(WUKONG_SLOT_GAME.experience.symbols.definitions[symbol]).toBeDefined()
      expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions[symbol]).toBeDefined()
      expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions[symbol]?.image).not.toBe(
        WUKONG_SLOT_GAME.experience.symbols.definitions[symbol]?.image,
      )
    }
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions.PAW?.image).not.toBe(
      PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.PAW?.image,
    )
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.guideEntries).toHaveLength(16)
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.features.collections?.presentation).toBe('gem-hoard')
    expect(WUKONG_SLOT_GAME.experience.features.collections?.presentation).toBe('celestial-orbit')
    expect(new Set(
      Object.values(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions)
        .flatMap((definition) => definition ? [definition.label] : []),
    ).size).toBe(23)
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
    expect(getSlotSymbolValueLabel(
      RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions.RAND_15!,
      50,
    )).toBe('R75')
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
