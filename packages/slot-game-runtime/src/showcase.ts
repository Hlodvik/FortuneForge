import type {
  SlotCabinetTheme,
  SlotResponsiveImages,
  SlotSoundSet,
  SlotSymbolGuideEntry,
} from '@fortuneforge/slot-client-core'
import { withEngineGuide, type SlotEngineGuidePolicy } from './engineGuide.js'
import { createSlotRulesSet, type SlotExperienceSet, type SlotRulesSet } from './experience.js'
import type {
  SlotCollectionPresentation,
  SlotFeatureSet,
  SlotHelpDefinition,
  SlotSpecialRoundFeature,
} from './features.js'
import { defineSlotGame, type SlotGameCatalogDefinition, type SlotGameManifest } from './manifest.js'
import type { SlotOutcomeNarrative } from './outcomeNarratives.js'
import { NO_SLOT_SOUNDS } from './sounds.js'
import {
  CLASSIC_SLOT_SYMBOL_SET_ID,
  createThemedSymbolSet,
  type AdditionalThemeSymbolId,
} from './symbols.js'

const BASE_SYMBOL_IDS = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BOLT', 'BANANA', 'PAW',
] as const

type BaseSymbolId = (typeof BASE_SYMBOL_IDS)[number]
export type ShowcaseSymbolSpec = readonly [label: string, glyph: string, image: string]

export type ShowcaseSpecialRound = {
  collectionTarget: number
  collectionAwardedSpins: number
  freeGames: NonNullable<SlotHelpDefinition['freeGames']>
  feature: SlotSpecialRoundFeature
  sounds?: SlotSoundSet
  usesCollections?: boolean
  usesEnergy?: boolean
  usesDirectValueTokens?: boolean
  earnHelp?: string
  collectionRewardDescription?: string
}

export type ShowcaseSlotGameDefinition = {
  id: string
  title: string
  subtitle: string
  description: string
  serverGameId: string
  /** Omitted themes retain their existing server symbol-set identity. */
  serverSymbolSetId?: string
  /** A game-specific idle grid; never changes another theme's initial symbols. */
  initialReels?: SlotRulesSet['initialReels']
  paylineTrace?: SlotCabinetTheme['paylineTrace']
  paylinePatternIds: readonly number[]
  presentation: SlotCollectionPresentation
  collectionAriaLabel: string
  itemLabel: string
  energyLabel: string
  actorName: string
  awardLabel: string
  outcomeNarrative: SlotOutcomeNarrative
  celebrationEffect: string
  valueToken: ShowcaseSymbolSpec
  collectionLabels: readonly [
    ShowcaseSymbolSpec,
    ShowcaseSymbolSpec,
    ShowcaseSymbolSpec,
    ShowcaseSymbolSpec,
  ]
  symbolSpecs: Readonly<Record<BaseSymbolId, ShowcaseSymbolSpec>>
  additionalSymbolSpecs?: Readonly<Partial<Record<AdditionalThemeSymbolId, ShowcaseSymbolSpec>>>
  symbolGuide?: { entries: readonly SlotSymbolGuideEntry[]; note: string }
  /** Game-owned guide copy may opt out of the generic audited guide rewrite. */
  guidePolicy?: SlotEngineGuidePolicy
  helpSections?: SlotHelpDefinition['extraSections']
  fiveMatchPayoutNote?: string
  fourMatchPayoutNote?: string
  /** Opt-in candidates indexed by the existing fallback image URL. */
  responsiveImages?: Readonly<Record<string, SlotResponsiveImages>>
  artwork: {
    emblem: string
    accent: string
    backdrop: string
    backdropImageSet?: string
    title?: SlotCabinetTheme['titleArtwork']
  }
  motif: string
  accentGlyph: string
  /** Owned sound set for a base-only game. Feature games may keep it with specialRound. */
  sounds?: SlotSoundSet
  specialRound?: ShowcaseSpecialRound
  colors: {
    skyTop: string
    skyBottom: string
    horizon: string
    ground: string
    primary: string
    secondary: string
    deep: string
    rim: string
    glow: string
    text: string
  }
}

const collectionSymbols = ['SEAL_SYNC', 'SEAL_ROWS', 'SEAL_PAW', 'SEAL_RAND'] as const

export function createShowcaseSlotGame(
  definition: ShowcaseSlotGameDefinition,
): SlotGameManifest {
  const { artwork, colors } = definition
  const specialRound = definition.specialRound
  const usesCollections = specialRound?.usesCollections ?? true
  const usesEnergy = specialRound?.usesEnergy ?? true
  const usesDirectValueTokens = specialRound?.usesDirectValueTokens ?? true
  const baseImages = Object.fromEntries(BASE_SYMBOL_IDS.map((id) => [
    id,
    definition.symbolSpecs[id][2],
  ])) as Record<BaseSymbolId, string>
  const collectionImages = definition.collectionLabels.map((spec) => spec[2])
  const valueImage = definition.valueToken[2]
  const themedSymbols = createThemedSymbolSet({
    id: `${definition.id}-symbols-v1`,
    serverSymbolSetId: definition.serverSymbolSetId ??
      (specialRound ? CLASSIC_SLOT_SYMBOL_SET_ID : `${definition.serverGameId}-symbols`),
    symbols: {
      ...Object.fromEntries(BASE_SYMBOL_IDS.map((id) => [id, {
        label: definition.symbolSpecs[id][0],
        image: baseImages[id],
        responsiveImages: definition.responsiveImages?.[baseImages[id]],
      }])),
      ...Object.fromEntries(collectionSymbols.map((id, index) => [id, {
        label: definition.collectionLabels[index][0],
        image: collectionImages[index],
        responsiveImages: definition.responsiveImages?.[collectionImages[index]],
      }])),
    } as Parameters<typeof createThemedSymbolSet>[0]['symbols'],
    ...(definition.additionalSymbolSpecs?.['8'] ? {
      additionalSymbols: {
        '8': {
          label: definition.additionalSymbolSpecs['8'][0],
          image: definition.additionalSymbolSpecs['8'][2],
          responsiveImages: definition.responsiveImages?.[definition.additionalSymbolSpecs['8'][2]],
        },
      },
    } : {}),
    valueToken: {
      label: definition.valueToken[0],
      image: valueImage,
      responsiveImages: definition.responsiveImages?.[valueImage],
    },
    energyEarnLabel: `+1 ${definition.energyLabel.toLowerCase()}`,
    freeRequiredLabel: `${specialRound?.freeGames.requiredSymbols ?? 3}+`,
    freeAwardLabel: `${specialRound?.freeGames.awardedSpins ?? 5} free games`,
    collectionRequiredCount: specialRound?.collectionTarget ?? 40,
    collectorFirstValue: `${definition.actorName.toLowerCase()} gathers value tokens`,
    collectorSecondValue: `double ${definition.awardLabel.toLowerCase()}`,
    collectionAwardLabels: Object.fromEntries(collectionSymbols.map((id, index) => [
      id,
      `${specialRound?.collectionAwardedSpins ?? 10} ${definition.collectionLabels[index][0].toLowerCase()} spins`,
    ])) as Parameters<typeof createThemedSymbolSet>[0]['collectionAwardLabels'],
  })
  const symbols = definition.symbolGuide
    ? {
        ...themedSymbols,
        guideEntries: definition.symbolGuide.entries,
        guideNote: definition.symbolGuide.note,
      }
    : themedSymbols
  const features: SlotFeatureSet = {
    energy: usesEnergy ? { label: definition.energyLabel, symbol: 'BOLT' } : undefined,
    collections: usesCollections ? {
      ariaLabel: definition.collectionAriaLabel,
      itemLabel: definition.itemLabel,
      presentation: definition.presentation,
      entries: collectionSymbols.map((symbol, index) => ({
        id: ['sync', 'rows', 'paw', 'rand'][index],
        label: definition.collectionLabels[index][0],
        shortLabel: definition.collectionLabels[index][0].split(' ')[0],
        symbol,
        requiredCount: specialRound?.collectionTarget ?? 40,
        ...(specialRound?.collectionRewardDescription
          ? {
              rewardDescription: specialRound.collectionRewardDescription,
              progressDescription: `Land matching ${definition.itemLabel} anywhere on the reels.`,
            }
          : {}),
      })),
    } : undefined,
    moneyGrab: usesDirectValueTokens ? {
      actorName: definition.actorName,
      awardLabel: definition.awardLabel,
      collectorSymbol: 'PAW',
      valueSymbolPrefix: 'RAND_',
    } : undefined,
    specialRound: specialRound?.feature,
  }
  const help: SlotHelpDefinition = {
    paylineCount: definition.paylinePatternIds.length,
    paylinePatternIds: definition.paylinePatternIds,
    freeGames: specialRound?.freeGames ?? { requiredSymbols: 3, awardedSpins: 5 },
    ...(definition.fiveMatchPayoutNote
      ? { fiveMatchPayoutNote: definition.fiveMatchPayoutNote }
      : {}),
    ...(definition.fourMatchPayoutNote
      ? { fourMatchPayoutNote: definition.fourMatchPayoutNote }
      : {}),
    extraSections: definition.helpSections ?? [
      {
        badge: 'GRAB',
        title: definition.awardLabel,
        body: `${definition.actorName} gathers every ${definition.valueToken[0].toLowerCase()} in the window. Two collectors double the haul. Three ${definition.symbolSpecs.BANANA[0].toLowerCase()} symbols in a row, column, or diagonal pay 3× the wager.`,
      },
      {
        badge: 'SET',
        title: definition.collectionAriaLabel,
        body: !usesCollections
          ? specialRound?.earnHelp ?? 'Land the marked scatter symbols to begin the special round.'
          : `Collect ${specialRound?.collectionTarget ?? 40} ${definition.itemLabel} on any track to unlock ${specialRound?.collectionAwardedSpins ?? 10} themed free spins.${usesEnergy ? ` ${definition.energyLabel} improves collection odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest track.` : ''}`,
      },
    ],
  }
  const cabinet: SlotCabinetTheme = {
    id: `${definition.id}-cabinet-v1`,
    chrome: 'simple',
    accessibleName: `${definition.title} themed slot machine`,
    eyebrow: 'Fortune Forge presents',
    title: definition.title,
    subtitle: definition.subtitle,
    titleArtwork: artwork.title,
    paylineTrace: definition.paylineTrace,
    celebrationEffect: definition.celebrationEffect,
    emblemImage: artwork.emblem,
    accentImage: artwork.accent,
    backdropImage: artwork.backdrop,
    visualsBackdropImage: artwork.backdrop,
    pageBackdropImage: artwork.backdrop,
    backdropImageSet: artwork.backdropImageSet,
    palette: {
      shellTop: colors.primary,
      shellBottom: colors.deep,
      panel: colors.ground,
      trim: colors.rim,
      trimBright: colors.glow,
      accent: colors.secondary,
      glow: colors.glow,
      text: colors.text,
    },
  }
  const experience: SlotExperienceSet = withEngineGuide({
    id: `${definition.id}-experience-v1`,
    cabinet,
    features,
    help,
    outcomeNarrative: definition.outcomeNarrative,
    shellBackdrop: 'theme',
    symbols,
    mascot: null,
    sounds: definition.sounds ?? specialRound?.sounds ?? NO_SLOT_SOUNDS,
    rules: createSlotRulesSet(
      definition.serverGameId,
      definition.initialReels ? { initialReels: definition.initialReels } : undefined,
    ),
  }, definition.guidePolicy)

  return defineSlotGame({
    id: definition.id,
    routes: {
      play: `/slots/${definition.id}`,
      demo: `/slots/${definition.id}/demo`,
    },
    catalog: createShowcaseSlotCatalog(definition),
    experience,
  })
}

export function createShowcaseSlotCatalog(
  definition: ShowcaseSlotGameDefinition,
): SlotGameCatalogDefinition {
  const [, , reelSymbolImage] = definition.symbolSpecs['7']
  return {
    id: definition.id,
    title: definition.title,
    shortTitle: definition.title,
    description: definition.description,
    image: reelSymbolImage,
    imagePresentation: 'contain',
    slotDivBackgroundImage: definition.artwork.backdrop,
  }
}
