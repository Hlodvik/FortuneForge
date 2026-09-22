import type { SlotCabinetTheme, SlotCelebrationEffect } from '../../../features/slots/config/cabinetThemes'
import type { SlotOutcomeNarrative } from '../../../features/slots/config/outcomeNarratives'
import { DEFAULT_SLOT_SOUNDS, type SlotSoundSet } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type {
  SlotCollectionPresentation,
  SlotFeatureSet,
  SlotHelpDefinition,
  SlotSpecialRoundFeature,
} from '../../../features/slots/config/slotFeatures'
import { defineSlotGame, type SlotGameManifest } from './slotGameManifest'
import { createThemedSymbolSet } from './themedSymbolSet'

const BASE_SYMBOL_IDS = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BOLT', 'BANANA', 'PAW',
] as const

type BaseSymbolId = (typeof BASE_SYMBOL_IDS)[number]
type SymbolSpec = readonly [label: string, glyph: string, image: string]

type ShowcaseSpecialRound = {
  collectionTarget: number
  collectionAwardedSpins: number
  freeGames: NonNullable<SlotHelpDefinition['freeGames']>
  feature: SlotSpecialRoundFeature
  sounds: SlotSoundSet
  usesCollections?: boolean
  usesEnergy?: boolean
  usesDirectValueTokens?: boolean
  earnHelp?: string
}

export type ShowcaseSlotGameDefinition = {
  id: string
  title: string
  subtitle: string
  description: string
  serverGameId: string
  paylinePatternIds: readonly number[]
  presentation: SlotCollectionPresentation
  collectionAriaLabel: string
  itemLabel: string
  energyLabel: string
  actorName: string
  awardLabel: string
  outcomeNarrative: SlotOutcomeNarrative
  celebrationEffect: SlotCelebrationEffect
  valueToken: SymbolSpec
  collectionLabels: readonly [SymbolSpec, SymbolSpec, SymbolSpec, SymbolSpec]
  symbolSpecs: Readonly<Record<BaseSymbolId, SymbolSpec>>
  artwork: {
    emblem: string
    accent: string
    backdrop: string
  }
  motif: string
  accentGlyph: string
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
  const baseImages = Object.fromEntries(BASE_SYMBOL_IDS.map((id) => {
    return [id, definition.symbolSpecs[id][2]]
  })) as Record<BaseSymbolId, string>
  const collectionImages = definition.collectionLabels.map((spec) => spec[2])
  const valueImage = definition.valueToken[2]
  const symbols = createThemedSymbolSet({
    id: `${definition.id}-symbols-v1`,
    serverSymbolSetId: specialRound ? 'wukong-treasures-v3' : `${definition.serverGameId}-symbols`,
    symbols: {
      ...Object.fromEntries(BASE_SYMBOL_IDS.map((id) => [id, {
        label: definition.symbolSpecs[id][0],
        image: baseImages[id],
      }])),
      ...Object.fromEntries(collectionSymbols.map((id, index) => [id, {
        label: definition.collectionLabels[index][0],
        image: collectionImages[index],
      }])),
    } as Parameters<typeof createThemedSymbolSet>[0]['symbols'],
    valueToken: { label: definition.valueToken[0], image: valueImage },
    energyEarnLabel: `+1 ${definition.energyLabel.toLowerCase()}`,
    collectorFirstValue: `${definition.actorName.toLowerCase()} gathers value tokens`,
    collectorSecondValue: `double ${definition.awardLabel.toLowerCase()}`,
    collectionAwardLabels: Object.fromEntries(collectionSymbols.map((id, index) =>
      [id, `${specialRound?.collectionAwardedSpins ?? 10} ${definition.collectionLabels[index][0].toLowerCase()} spins`],
    )) as Parameters<typeof createThemedSymbolSet>[0]['collectionAwardLabels'],
  })
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
    extraSections: [
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
    celebrationEffect: definition.celebrationEffect,
    emblemImage: artwork.emblem,
    accentImage: artwork.accent,
    backdropImage: artwork.backdrop,
    visualsBackdropImage: artwork.backdrop,
    pageBackdropImage: artwork.backdrop,
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
  const experience: SlotExperienceSet = {
    id: `${definition.id}-experience-v1`,
    cabinet,
    features,
    help,
    outcomeNarrative: definition.outcomeNarrative,
    shellBackdrop: 'theme',
    symbols,
    mascot: null,
    sounds: specialRound?.sounds ?? DEFAULT_SLOT_SOUNDS,
    rules: createSlotRulesSet(definition.serverGameId),
  }

  return defineSlotGame({
    id: definition.id,
    routes: {
      play: `/slots/${definition.id}`,
      demo: `/slots/${definition.id}/demo`,
    },
    catalog: {
      id: definition.id,
      title: definition.title,
      shortTitle: definition.title,
      description: definition.description,
      image: baseImages['7'],
      imagePresentation: 'contain',
      slotDivBackgroundImage: artwork.backdrop,
    },
    experience,
  })
}
