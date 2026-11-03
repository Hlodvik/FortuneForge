import { existsSync } from 'node:fs'
import { createElement, createRef } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { WinHelpDialog } from '../../features/slots/WinHelpDialog'
import { describeSpinOutcome } from '../../features/slots/presentation/spinPresentation'
import { WUKONG_FEATURE_SYMBOL_IDS } from './wukong/symbols'
import type { SlotSymbolId } from '../../features/slots/types/slots'
import { getSlotSymbolValueLabel, slotPointsToRand } from '../../features/slots/slotPagePresentation'
import { getSlotAnimationSymbolIds } from '../../features/slots/config/symbolSets'
import {
  loadAllSlotGameManifests,
  SECOND_WAVE_SLOT_GAME_IDS,
  SLOT_ROUTE_DEFINITIONS,
} from '.'
import { SLOT_GAME_CATALOG } from './catalog'
import { createSlotExperienceRouteMap, type SlotGameManifest } from './shared/slotGameManifest'

const SLOT_GAME_MANIFESTS = await loadAllSlotGameManifests()

function requireGame(id: string): SlotGameManifest {
  const game = SLOT_GAME_MANIFESTS.find((candidate) => candidate.id === id)
  if (!game) throw new Error(`Missing test game '${id}'.`)
  return game
}

function decodeSymbolImage(image: string | undefined): string {
  const source = image ?? ''
  const base64Svg = /^data:image\/svg\+xml;base64,(.+)$/.exec(source)
  return base64Svg ? Buffer.from(base64Svg[1], 'base64').toString('utf8') : decodeURIComponent(source)
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
const SAMURAI_FORTUNE_SLOT_GAME = requireGame('samurai-fortune')
const ROBOT_REVOLUTION_SLOT_GAME = requireGame('robot-revolution')
const PHANTOM_MANOR_SLOT_GAME = requireGame('phantom-manor')
const OCEAN_ODYSSEY_SLOT_GAME = requireGame('ocean-odyssey')
const DRAGON_HOARD_SLOT_GAME = requireGame('dragon-hoard')
const JUNGLE_JACKPOT_SLOT_GAME = requireGame('jungle-jackpot')
const CANDY_CARNIVAL_SLOT_GAME = requireGame('candy-carnival')
const DESERT_TREASURES_SLOT_GAME = requireGame('desert-treasures')
const NEON_NIGHTS_SLOT_GAME = requireGame('neon-nights')
const NORDIC_LEGENDS_SLOT_GAME = requireGame('nordic-legends')
const SECOND_WAVE_SLOT_GAMES = SECOND_WAVE_SLOT_GAME_IDS.map(requireGame)
const REEL_SYMBOL_CATALOG_GAME_IDS = [
  'arcane-archives',
  'cosmic-fortune',
  'dino-dominion',
  'rainbow-realm',
  'reel-riches',
  ...SECOND_WAVE_SLOT_GAME_IDS,
] as const
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
    expect(serverSymbolSetIds).toEqual(SLOT_GAME_MANIFESTS.map(game =>
      game.id === 'dragon-hoard' ? 'midnight-vault-symbols-v1'
        : game.id === 'neon-nights' ? 'neon-arcade-symbols-v2'
        : game.id === 'rainbow-realm' ? 'rainbow-realm-symbols-v1' : 'wukong-treasures-v3'))
    for (const game of SLOT_GAME_MANIFESTS.filter(candidate => candidate.id !== 'dragon-hoard')) {
      expect(game.experience.symbols.definitions['8'], game.id).toBeUndefined()
      expect(game.experience.rules.initialReels.flat(), game.id).not.toContain('8')
    }
    expect([...paylineCounts].sort((left, right) => left - right)).toEqual([
      14, 16, 19, ...Array(17).fill(23),
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
      [GODS_OF_OLYMPUS_SLOT_GAME, null, 'divine-offering', 28],
      [REEL_RICHES_SLOT_GAME, 'fishing net', 'tackle-creel', 40],
      [HIGH_NOON_FORTUNE_SLOT_GAME, 'golden lasso', null, null],
      [ROYAL_DRAW_SLOT_GAME, 'dealer chip tray', 'chip-stack', 24],
    ] as const

    for (const [game, actorName, presentation, collectionTarget] of themedGames) {
      expect(game.routes.play).not.toBeNull()
      if (actorName) expect(game.experience.features.moneyGrab?.actorName).toContain(actorName)
      else expect(game.experience.features.moneyGrab).toBeUndefined()
      if (presentation && collectionTarget) {
        expect(game.experience.features.collections?.presentation).toBe(presentation)
        expect(game.experience.features.collections?.entries).toHaveLength(4)
        expect(game.experience.features.collections?.entries.every(
          (collection) => collection.requiredCount === collectionTarget,
        )).toBe(true)
      } else {
        expect(game.experience.features.collections).toBeUndefined()
      }
      expect(game.experience.help.extraSections).toHaveLength(2)
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'BOLT')).toBe(Boolean(game.experience.features.energy))
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'PAW')).toBe(Boolean(game.experience.features.moneyGrab))
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }
  })

  it('registers Arcane Archives and Cosmic Fortune with distinct collection presentations', () => {
    const newGames = [
      [ARCANE_ARCHIVES_SLOT_GAME, 'enchanted book satchel', 'spellbook-shelf'],
      [COSMIC_FORTUNE_SLOT_GAME, null, 'star-orbit'],
    ] as const

    expect(SLOT_GAME_MANIFESTS).toHaveLength(20)
    for (const [game, actorName, presentation] of newGames) {
      expect(game.routes.play).toBe(`/slots/${game.id}`)
      expect(game.routes.demo).toBe(`/slots/${game.id}/demo`)
      if (actorName) expect(game.experience.features.moneyGrab?.actorName).toContain(actorName)
      else expect(game.experience.features.moneyGrab).toBeUndefined()
      expect(game.experience.features.collections?.presentation).toBe(presentation)
      expect(game.experience.features.collections?.entries).toHaveLength(4)
      expect(game.experience.help.extraSections).toHaveLength(game.id === 'arcane-archives' ? 3 : 2)
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'BOLT')).toBe(
        Boolean(game.experience.features.energy || game.experience.features.lightningCollection),
      )
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'PAW')).toBe(Boolean(game.experience.features.moneyGrab))
      expect(new Set(
        Object.values(game.experience.symbols.definitions)
          .flatMap((definition) => definition ? [definition.label] : []),
      ).size).toBe(23)
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }

    const { features, symbols } = ARCANE_ARCHIVES_SLOT_GAME.experience
    expect(features.energy).toBeUndefined()
    expect(features.lightningCollection?.collectorSymbol).toBe('BOLT')
    expect(symbols.definitions['7']?.wagerMultiplier).toBe(0.5)
    expect(symbols.definitions.POWER?.wagerMultiplier).toBe(1)
    expect(symbols.definitions.SEAL_RAND?.wagerMultiplier).toBe(2)
  })

  it('registers Gold Foundry with cash ingots and no new free-spin features', () => {
    const { experience, routes } = DINO_DOMINION_SLOT_GAME
    const { features, help, symbols } = experience
    expect(routes.demo).toBe('/slots/dino-dominion/demo')
    expect(features.collections).toBeUndefined()
    expect(features.specialRound).toBeUndefined()
    expect(help.freeGames).toBeUndefined()
    expect(features.energy?.symbol).toBe('BOLT')
    expect(features.moneyGrab).toMatchObject({
      collectorSymbol: 'PAW',
      presentation: 'separate-award',
      valueSymbols: ['SEAL_ROWS', 'SEAL_SYNC', 'SEAL_PAW', 'SEAL_RAND'],
    })
    expect(symbols.guideEntries.filter(entry => entry.firstLabel === '3 / 4')
      .map(entry => entry.symbol)).toEqual(expect.arrayContaining(['SEAL_ROWS', 'SEAL_SYNC', 'SEAL_PAW', 'SEAL_RAND']))
    expect(symbols.guideEntries.find(entry => entry.symbol === 'PAW')?.firstLabel).toBe('1 crucible')
    expect(symbols.definitions.RAND_1).toBeUndefined()
  })

  it('registers ten second-wave games with complete distinct themed contracts', () => {
    expect(SECOND_WAVE_SLOT_GAMES).toHaveLength(10)
    expect(new Set(SECOND_WAVE_SLOT_GAMES.map((game) => game.id)).size).toBe(10)

    for (const game of SECOND_WAVE_SLOT_GAMES) {
      expect(game.routes.play).toBe(`/slots/${game.id}`)
      expect(game.routes.demo).toBe(`/slots/${game.id}/demo`)
      expect(game.experience.cabinet.backdropImage).toMatch(/\.webp$/)
      const hasNoCollections = new Set([
        'robot-revolution',
        'phantom-manor',
        'candy-carnival',
        'nordic-legends',
        'neon-nights',
      ]).has(game.id)
      expect(game.experience.features.collections?.entries ?? []).toHaveLength(hasNoCollections ? 0 : 4)
      if (game.id !== 'neon-nights') {
        expect(game.experience.help.extraSections).toHaveLength(game.id === 'dragon-hoard' ? 4 : 2)
      }
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'BOLT')).toBe(Boolean(game.experience.features.energy))
      expect(game.experience.symbols.guideEntries.some(entry => entry.symbol === 'PAW')).toBe(Boolean(game.experience.features.moneyGrab))
      expect(new Set(
        Object.values(game.experience.symbols.definitions)
          .flatMap((definition) => definition ? [definition.label] : []),
      ).size).toBe(23)
      expect(Object.keys(game.experience.symbols.definitions)).toHaveLength(game.id === 'dragon-hoard' ? 24 : 23)
      for (const symbol of WUKONG_FEATURE_SYMBOL_IDS) {
        expect(game.experience.symbols.definitions[symbol]).toBeDefined()
      }
    }
  })

  it('gives every non-Wukong/Pirates game its own settled-outcome narrative', () => {
    const themedGames = SLOT_GAME_MANIFESTS.filter((game) =>
      game.id !== 'wukong-journey-to-the-west' && game.id !== 'pirates-fortune',
    )

    expect(themedGames).toHaveLength(18)
    for (const game of themedGames) {
      const narrative = game.experience.outcomeNarrative
      expect(narrative, `${game.id} is missing outcome copy`).toBeDefined()
      expect(narrative?.lossTitle).toBeTruthy()
      expect(narrative?.winTitle).toBeTruthy()
      expect(narrative?.greatWinTitle).toBeTruthy()
      expect(narrative?.bigWinTitle).toBeTruthy()
      expect(narrative?.freeGameSingular).toBeTruthy()
      expect(narrative?.freeGamePlural).toBeTruthy()
      expect(narrative?.lossNextAction).toBeTruthy()
      expect(narrative?.winNextAction).toBeTruthy()
      expect(narrative?.bonusNextAction).toBeTruthy()
      expect(game.experience.cabinet.celebrationEffect).toBeTruthy()
      if (!narrative) continue

      expect(describeSpinOutcome({
        awardRand: 500,
        wagerRand: 10,
        freeSpinsAwarded: 0,
        narrative,
      })).toMatchObject({
        kind: 'big-win',
        title: narrative.bigWinTitle,
        awardRand: 500,
        nextAction: narrative.winNextAction,
      })
      expect(describeSpinOutcome({
        awardRand: 0,
        wagerRand: 10,
        freeSpinsAwarded: 1,
        narrative,
      })).toMatchObject({
        kind: 'bonus',
        title: `1 ${narrative.freeGameSingular} won`,
        nextAction: narrative.bonusNextAction,
      })
    }
    expect(new Set(themedGames.map((game) => game.experience.outcomeNarrative?.bigWinTitle)).size)
      .toBe(18)
    expect(new Set(themedGames.map((game) => game.experience.cabinet.celebrationEffect)).size)
      .toBe(18)
  })

  it('gives every cabinet a named, persistent feature contract', () => {
    const features = SLOT_GAME_MANIFESTS.map((game) => game.experience.features.specialRound)

    expect(features.every(Boolean)).toBe(true)
    expect(new Set(features.map((feature) => feature?.id)).size).toBe(20)
    for (const feature of features) {
      expect(feature?.title).toBeTruthy()
      expect(feature?.earnLabel).toBeTruthy()
      expect(feature?.earnHint).toBeTruthy()
      expect(Object.keys(feature?.activeModes ?? {})).not.toHaveLength(0)
    }
  })

  it('uses a high-paying reel symbol for every slot catalog card', () => {
    for (const gameId of REEL_SYMBOL_CATALOG_GAME_IDS) {
      const game = requireGame(gameId)
      const reelSymbol = game.experience.symbols.definitions['7']
      const catalogEntry = SLOT_GAME_CATALOG.find((candidate) => candidate.id === gameId)

      expect(reelSymbol).toBeDefined()
      expect(game.catalog.image).toBe(reelSymbol?.image)
      expect(catalogEntry?.image).toBe(reelSymbol?.image)
      expect(decodeSymbolImage(reelSymbol?.image)).not.toContain('Segoe UI Emoji')
    }
  })

  it('uses artwork rather than emoji-rendered artwork for every reel symbol', () => {
    for (const game of SLOT_GAME_MANIFESTS) {
      for (const cabinetImage of [
        game.experience.cabinet.emblemImage,
        game.experience.cabinet.accentImage,
        game.experience.cabinet.backdropImage,
      ]) {
        expect(decodeSymbolImage(cabinetImage)).not.toContain('Segoe UI Emoji')
      }
      for (const symbol of Object.values(game.experience.symbols.definitions)) {
        expect(symbol?.image, `${game.id} is missing artwork for ${symbol?.label ?? 'a reel symbol'}`).toBeTruthy()
        expect(decodeSymbolImage(symbol?.image)).not.toContain('Segoe UI Emoji')
      }
    }
  })

  it('uses dedicated candy artwork for every Candy Carnival symbol', () => {
    const symbols = Object.values(CANDY_CARNIVAL_SLOT_GAME.experience.symbols.definitions)

    expect(symbols).not.toHaveLength(0)
    for (const symbol of symbols) {
      expect(symbol?.image).toContain('/src/assets/slots/games/candy-carnival/')
    }
  })

  it('maps the pirate skin to gems and a skull-and-crossbones collector', () => {
    const { collections, moneyGrab } = PIRATES_FORTUNE_SLOT_GAME.experience.features

    expect(collections?.ariaLabel).toBe('Treasure gem collections')
    expect(collections?.itemLabel).toBe('gems')
    expect(collections?.presentation).toBe('gem-hoard')
    expect(collections?.entries.map((collection) => collection.id)).toEqual([
      'sync',
      'rows',
      'paw',
      'rand',
    ])
    expect(collections?.entries.map((collection) => collection.shortLabel)).toEqual([
      'Ruby',
      'Lapis',
      'Orange',
      'Emerald',
    ])
    expect(collections?.entries.map((collection) => collection.containerImage)).toEqual([
      undefined, undefined, undefined, undefined,
    ])
    const symbolImages = Object.values(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions)
      .flatMap((symbol) => symbol ? [symbol.image, symbol.animatedImage ?? ''] : [])
    expect(symbolImages.join('\n')).not.toContain('/chests/')
    expect(existsSync(new URL('../../assets/slots/games/pirates-fortune/optimized/chests/ruby/level-1.png', import.meta.url))).toBe(false)
    expect(collections?.entries.map((collection) => collection.requiredCount)).toEqual([25, 25, 25, 25])
    expect(existsSync(new URL('../../assets/slots/games/pirates-fortune/chests', import.meta.url))).toBe(false)
    expect(moneyGrab?.collectorSymbol).toBe('PAW')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.PAW?.label).toContain('Purse')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.POWER?.label).toContain('Jolly Roger')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.FREE?.label).toContain('Treasure Map')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.SEAL_SYNC?.label).toContain('gem')
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.guideEntries).toContainEqual(
      expect.objectContaining({ symbol: 'RAND_05', firstValue: '0.5×–5× wager' }),
    )
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.symbols.definitions.PAW?.image).not.toBe(
      WUKONG_SLOT_GAME.experience.symbols.definitions.PAW?.image,
    )
  })

  it('gives five featured games named special rounds with matching server schemas and sound sets', () => {
    const featured = [
      [COSMIC_FORTUNE_SLOT_GAME, 'cosmic-orbit', 'cosmic-fortune-audio-v1', 24, 3, 6, true],
      [HIGH_NOON_FORTUNE_SLOT_GAME, 'high-noon-showdown', 'high-noon-fortune-audio-v1', 20, 3, 5, false],
      [GODS_OF_OLYMPUS_SLOT_GAME, 'olympus-trial', 'gods-of-olympus-audio-v1', 28, 4, 6, true],
      [PIRATES_FORTUNE_SLOT_GAME, 'pirates-broadside', 'pirates-fortune-audio-v9', 25, 3, 7, true],
      [ROYAL_DRAW_SLOT_GAME, 'royal-high-stakes', 'royal-draw-audio-v1', 24, 3, 6, true],
    ] as const

    for (const [game, roundId, soundId, collectionTarget, scatterCount, scatterAward, usesCollections] of featured) {
      expect(game.experience.features.specialRound?.id).toBe(roundId)
      expect(game.experience.features.collections?.entries.every(
        (collection) => collection.requiredCount === collectionTarget,
      ) ?? false).toBe(usesCollections)
      expect(game.experience.help.freeGames).toMatchObject({
        requiredSymbols: scatterCount,
        awardedSpins: scatterAward,
      })
      expect(game.experience.symbols.serverSymbolSetId).toBe(game.id === 'dragon-hoard' ? 'midnight-vault-symbols-v1' : 'wukong-treasures-v3')
      expect(game.experience.sounds.id).toBe(soundId)
    }
  })

  it('gives five more themed games distinct special rounds through the shared showcase contract', () => {
    const featured = [
      [SAMURAI_FORTUNE_SLOT_GAME, 'samurai-blade-trial', 'samurai-fortune-audio-v1', 26, 4, 6, true],
      [ROBOT_REVOLUTION_SLOT_GAME, 'robot-overclock', 'robot-revolution-audio-v1', 24, 3, 7, false],
      [PHANTOM_MANOR_SLOT_GAME, 'phantom-midnight-seance', 'phantom-manor-audio-v1', 18, 3, 5, false],
      [OCEAN_ODYSSEY_SLOT_GAME, 'ocean-pearl-voyage', 'ocean-odyssey-audio-v1', 28, 4, 6, true],
      [DRAGON_HOARD_SLOT_GAME, 'dragon-ember-siege', 'dragon-hoard-audio-v1', 30, 3, 6, true],
    ] as const

    for (const [game, roundId, soundId, collectionTarget, scatterCount, scatterAward, usesCollections] of featured) {
      expect(game.experience.features.specialRound?.id).toBe(roundId)
      expect(game.experience.features.collections?.entries.every(
        (collection) => collection.requiredCount === collectionTarget,
      ) ?? false).toBe(usesCollections)
      expect(game.experience.help.freeGames).toMatchObject({
        requiredSymbols: scatterCount,
        awardedSpins: scatterAward,
      })
      expect(game.experience.symbols.serverSymbolSetId).toBe(game.id === 'dragon-hoard' ? 'midnight-vault-symbols-v1' : 'wukong-treasures-v3')
      expect(game.experience.sounds.id).toBe(soundId)
    }
  })

  it('gives the remaining showcase slots matched special rounds and audio', () => {
    const featured = [
      [JUNGLE_JACKPOT_SLOT_GAME, 'jungle-temple-trek', 'jungle-jackpot-audio-v1', 22, 3, 6, true],
      [CANDY_CARNIVAL_SLOT_GAME, 'candy-sugar-parade', 'candy-carnival-audio-v1', 20, 4, 5, false],
      [DESERT_TREASURES_SLOT_GAME, 'desert-pharaoh-passage', 'desert-treasures-audio-v1', 24, 3, 7, true],
      [NORDIC_LEGENDS_SLOT_GAME, 'nordic-valhalla-voyage', 'nordic-legends-audio-v1', 28, 4, 6, false],
    ] as const

    for (const [game, roundId, soundId, collectionTarget, scatterCount, scatterAward, usesCollections] of featured) {
      expect(game.experience.features.specialRound?.id).toBe(roundId)
      expect(game.experience.features.collections?.entries.every(
        (collection) => collection.requiredCount === collectionTarget,
      ) ?? false).toBe(usesCollections)
      expect(game.experience.help.freeGames).toMatchObject({
        requiredSymbols: scatterCount,
        awardedSpins: scatterAward,
      })
      expect(game.experience.symbols.serverSymbolSetId).toBe('wukong-treasures-v3')
      expect(game.experience.sounds.id).toBe(soundId)
    }
  })

  it('varies earn paths while limiting energy and direct multiplier tokens', () => {
    const featured = [
      COSMIC_FORTUNE_SLOT_GAME, HIGH_NOON_FORTUNE_SLOT_GAME, GODS_OF_OLYMPUS_SLOT_GAME,
      PIRATES_FORTUNE_SLOT_GAME, ROYAL_DRAW_SLOT_GAME, SAMURAI_FORTUNE_SLOT_GAME,
      ROBOT_REVOLUTION_SLOT_GAME, PHANTOM_MANOR_SLOT_GAME, OCEAN_ODYSSEY_SLOT_GAME,
      DRAGON_HOARD_SLOT_GAME, JUNGLE_JACKPOT_SLOT_GAME, CANDY_CARNIVAL_SLOT_GAME,
      DESERT_TREASURES_SLOT_GAME, NEON_NIGHTS_SLOT_GAME, NORDIC_LEGENDS_SLOT_GAME,
    ]

    expect(featured.filter((game) => game.experience.features.energy)).toHaveLength(4)
    expect(featured.filter((game) => !game.experience.features.moneyGrab)).toHaveLength(10)
    expect(featured.filter((game) => !game.experience.features.collections)).toHaveLength(6)
    expect(new Set(featured.map((game) => game.experience.features.specialRound?.earnStyle)).size).toBeGreaterThan(7)
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

    expect(renderedPaylines).toHaveLength(23)
    expect(markup).toContain('aria-label="Valid five-symbol payline 23"')
    expect(markup).toContain('aria-label="Valid five-symbol payline 21"')
    expect(markup).toContain('aria-label="Valid five-symbol payline 22"')
  })

  it('gives Wukong a complete celestial feature set without duplicate Nimbus symbols', () => {
    const collections = WUKONG_SLOT_GAME.experience.features.collections?.entries
    expect(WUKONG_SLOT_GAME.experience.rules.gameId).toBe('wukong-journey-v1')
    expect(collections).toHaveLength(4)
    expect(collections?.map((collection) => collection.label)).toEqual([
      'Synced reels',
      'Extra rows',
      'Monkey paw rush',
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
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions['2']?.label).toBe('Staff strike')
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions['2']?.image).toBe(
      WUKONG_SLOT_GAME.experience.symbols.definitions['7']?.image,
    )
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions['2']?.label).not.toContain('Nimbus')
    expect(Object.values(WUKONG_SLOT_GAME.experience.symbols.definitions)
      .filter((definition) => definition.label.toLowerCase().includes('nimbus')),
    ).toHaveLength(1)
    expect(collections?.every((collection) => collection.rewardDescription)).toBe(true)
    expect(collections?.map((collection) => collection.displayLabel)).toEqual([
      'Mirror Reel',
      '+2 Rows',
      'Paw Rush',
      'Rand Reel',
    ])
    expect(WUKONG_SLOT_GAME.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
    expect(WUKONG_SLOT_GAME.experience.symbols.definitions.FREE).toBeUndefined()
    expect(WUKONG_SLOT_GAME.experience.symbols.guideEntries.map((entry) => entry.symbol)).not.toContain('FREE')
    expect(WUKONG_SLOT_GAME.experience.rules.initialReels.flat()).not.toContain('FREE')
    expect(WUKONG_SLOT_GAME.experience.rules.initialReels.flat()).toContain('PAW')
    expect(WUKONG_SLOT_GAME.experience.help.freeGames).toBeUndefined()
    expect(WUKONG_SLOT_GAME.experience.features.specialRound?.earnLabel).not.toContain('FREE GAME')
    expect(WUKONG_SLOT_GAME.experience.help.extraSections).toHaveLength(2)
    expect(WUKONG_SLOT_GAME.experience.shellBackdrop).toBe('theme')
    expect(WUKONG_SLOT_GAME.experience.cabinet.pageBackdropImage).toBeTruthy()
    expect(WUKONG_SLOT_GAME.experience.cabinet.visualsBackdropImage).toBeTruthy()

    const rainbowCollections = RAINBOW_REALM_SLOT_GAME.experience.features.collections
    expect(rainbowCollections?.ariaLabel).toBe('Orchard charm collections')
    expect(rainbowCollections?.presentation).toBe('juice-glass')
    expect(rainbowCollections?.entries.map((collection) => collection.id)).toEqual([
      'sync',
      'rows',
      'paw',
      'rand',
    ])
    expect(rainbowCollections?.entries.every((collection) => collection.requiredCount === 60)).toBe(true)
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.energy).toBeUndefined()
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.guideEntries.some((entry) => entry.symbol === 'BOLT')).toBe(false)
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions.BOLT).toBeDefined()
    expect(RAINBOW_REALM_SLOT_GAME.experience.rules.initialReels.flat()).not.toContain('BOLT')
    expect(getSlotAnimationSymbolIds(RAINBOW_REALM_SLOT_GAME.experience.symbols)).not.toContain('BOLT')
    expect(RAINBOW_REALM_SLOT_GAME.experience.help.freeGames).toEqual({
      requiredSymbols: 3,
      awardedSpins: 10,
      badge: 'BONUS',
      title: 'Orchard Rush bonus',
      symbolLabel: 'RAINBOW',
      awardLabel: 'Orchard Rush bonus spins',
    })
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.specialRound?.earnLabel)
      .toContain('3 rainbows or fill a 60-charm glass to start a 10-spin Orchard Rush bonus round')
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.guideEntries.find((entry) => entry.symbol === 'FREE')?.secondValue)
      .toBe('10 bonus spins')
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.moneyGrab?.collectorSymbol).toBe('PAW')
    expect(RAINBOW_REALM_SLOT_GAME.experience.features.moneyGrab?.valueSymbols)
      .toEqual(['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'])
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
    expect(RAINBOW_REALM_SLOT_GAME.experience.symbols.guideEntries).toHaveLength(19)
    const rainbowSymbols = RAINBOW_REALM_SLOT_GAME.experience.symbols
    expect(getSlotAnimationSymbolIds(rainbowSymbols)).toEqual(expect.arrayContaining(['A', 'J', 'K', 'Q']))
    expect(getSlotAnimationSymbolIds(rainbowSymbols).some(symbol => symbol.startsWith('RAND_'))).toBe(false)
    expect(rainbowSymbols.guideEntries.some(({ symbol }) => symbol.startsWith('RAND_'))).toBe(false)
    expect(PIRATES_FORTUNE_SLOT_GAME.experience.features.collections?.presentation).toBe('gem-hoard')
    expect(WUKONG_SLOT_GAME.experience.features.collections?.presentation).toBe('celestial-orbit')
    expect(new Set(
      Object.values(RAINBOW_REALM_SLOT_GAME.experience.symbols.definitions)
        .flatMap((definition) => definition ? [definition.label] : []),
    ).size).toBe(27)
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
