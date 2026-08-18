import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'
import { DEFAULT_SLOT_SOUNDS } from '../../../features/slots/config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'
import type {
  SlotCollectionPresentation,
  SlotFeatureSet,
  SlotHelpDefinition,
} from '../../../features/slots/config/slotFeatures'
import { defineSlotGame, type SlotGameManifest } from './slotGameManifest'
import { createThemedSymbolSet } from './themedSymbolSet'
import { createSlotBackdropSvg, createSlotIconSvg } from './themedSvg'

const BASE_SYMBOL_IDS = [
  '2', '3', '4', '5', '6', '7', 'ACE', 'FREE', 'POWER', 'BOLT', 'BANANA', 'PAW',
] as const

type BaseSymbolId = (typeof BASE_SYMBOL_IDS)[number]
type SymbolSpec = readonly [label: string, glyph: string]

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
  valueToken: SymbolSpec
  collectionLabels: readonly [SymbolSpec, SymbolSpec, SymbolSpec, SymbolSpec]
  symbolSpecs: Readonly<Record<BaseSymbolId, SymbolSpec>>
  motif: string
  accentGlyph: string
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
  const { colors } = definition
  const palettes = [
    [colors.primary, colors.deep, colors.rim, colors.glow],
    [colors.secondary, colors.deep, colors.rim, colors.primary],
    [colors.glow, colors.deep, colors.secondary, colors.rim],
    [colors.rim, colors.deep, colors.glow, colors.secondary],
  ] as const
  const icon = (label: string, glyph: string, variant: number) => {
    const [background, backgroundDeep, rim, glow] = palettes[variant % palettes.length]
    return createSlotIconSvg({ label, glyph, background, backgroundDeep, rim, glow })
  }
  const backdrop = createSlotBackdropSvg({
    label: `${definition.title} themed landscape`,
    motif: definition.motif,
    skyTop: colors.skyTop,
    skyBottom: colors.skyBottom,
    horizon: colors.horizon,
    accent: colors.glow,
    ground: colors.ground,
  })
  const baseImages = Object.fromEntries(BASE_SYMBOL_IDS.map((id, index) => {
    const [label, glyph] = definition.symbolSpecs[id]
    return [id, icon(label, glyph, index)]
  })) as Record<BaseSymbolId, string>
  const collectionImages = definition.collectionLabels.map(([label, glyph], index) =>
    icon(label, glyph, index + 1))
  const valueImage = icon(definition.valueToken[0], definition.valueToken[1], 3)
  const symbols = createThemedSymbolSet({
    id: `${definition.id}-symbols-v1`,
    serverSymbolSetId: `${definition.serverGameId}-symbols`,
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
      [id, `10 ${definition.collectionLabels[index][0].toLowerCase()} spins`],
    )) as Parameters<typeof createThemedSymbolSet>[0]['collectionAwardLabels'],
  })
  const features: SlotFeatureSet = {
    energy: { label: definition.energyLabel, symbol: 'BOLT' },
    collections: {
      ariaLabel: definition.collectionAriaLabel,
      itemLabel: definition.itemLabel,
      presentation: definition.presentation,
      entries: collectionSymbols.map((symbol, index) => ({
        id: ['sync', 'rows', 'paw', 'rand'][index],
        label: definition.collectionLabels[index][0],
        shortLabel: definition.collectionLabels[index][0].split(' ')[0],
        symbol,
        requiredCount: 40,
      })),
    },
    moneyGrab: {
      actorName: definition.actorName,
      awardLabel: definition.awardLabel,
      collectorSymbol: 'PAW',
      valueSymbolPrefix: 'RAND_',
    },
  }
  const help: SlotHelpDefinition = {
    paylineCount: definition.paylinePatternIds.length,
    paylinePatternIds: definition.paylinePatternIds,
    freeGames: { requiredSymbols: 3, awardedSpins: 5 },
    extraSections: [
      {
        badge: 'GRAB',
        title: definition.awardLabel,
        body: `${definition.actorName} gathers every ${definition.valueToken[0].toLowerCase()} in the window. Two collectors double the haul. Three ${definition.symbolSpecs.BANANA[0].toLowerCase()} symbols in a row, column, or diagonal pay 3× the wager.`,
      },
      {
        badge: 'SET',
        title: definition.collectionAriaLabel,
        body: `Collect 40 ${definition.itemLabel} on any track to unlock ten themed free spins. ${definition.energyLabel} improves collection odds at each quarter meter; a full meter boosts the payout by 1.5× and completes the nearest track.`,
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
    emblemImage: icon(`${definition.title} emblem`, definition.motif, 0),
    accentImage: icon(`${definition.title} accent`, definition.accentGlyph, 1),
    backdropImage: backdrop,
    visualsBackdropImage: backdrop,
    pageBackdropImage: backdrop,
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
    shellBackdrop: 'theme',
    symbols,
    mascot: null,
    sounds: DEFAULT_SLOT_SOUNDS,
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
      image: cabinet.emblemImage,
      imagePresentation: 'contain',
      slotDivBackgroundImage: backdrop,
    },
    experience,
  })
}
