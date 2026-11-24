// Edited: 2026-11-18T19:58:40-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:41:52-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:24:49-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:11:03-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T17:59:23-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:43:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:29:57-06:00 | Computer: Vinnabok
import type {
  GridPosition,
  LightningCollection,
  SlotCabinetTheme,
  SlotSoundSet,
  SlotSymbolId,
  SlotSymbolSet,
} from '@fortuneforge/slot-client-core'
import type { ComponentType, RefObject } from 'react'
import type { SlotFeatureSet, SlotHelpDefinition } from './features.js'
import type { MascotSet } from './mascot.js'
import type { SlotOutcomeNarrative } from './outcomeNarratives.js'

export type SlotTumbleRules = Readonly<{
  columns: number
  rows: number
  symbols: readonly SlotSymbolId[]
  initialReels: readonly (readonly SlotSymbolId[])[]
  multiplierValues: Readonly<Partial<Record<SlotSymbolId, number>>>
  multiplierCap: number
}>

export type SlotRulesSet = {
  gameId: string
  initialReels: readonly (readonly SlotSymbolId[])[]
  pointValueInCents: number
  wagerOptions: readonly number[]
  defaultWagerRand?: number
  autoSpinDelayMs: number
  autoSpinSpeedMultiplier: number
  baseSpinSpeedMultiplier: number
  reelTravelRowCount: number
  specialBoostCost: number
  energyMeterCapacity: number
  tumble?: SlotTumbleRules
}

export type SlotPresentationFlyover = {
  id: number
  collectionId: string
  symbol: SlotSymbolId
  left: number
  top: number
  width: number
  height: number
  travelX: number
  travelY: number
  durationMs: number
  chestDropHeight: number
}

export type SlotWinHelpContentProps = {
  onClose: () => void
}

/** A game-owned paid feature carried between normally charged rounds. */
export type SlotPaidModeState = Readonly<{
  mode: string
  remaining: number
  wagerPoints: number
}>

/** Settled, game-owned feature data; the game validates and presents its contract. */
export type SlotSpinFeaturePanelProps = Readonly<{
  feature: unknown | null
  spinKey: number | null
  isSpinning: boolean
  reducedMotion: boolean
  pointValueInCents: number
  cabinetRef?: RefObject<HTMLDivElement | null>
  paidMode?: SlotPaidModeState | null
  onPresentationBusyChange?: (busy: boolean) => void
}>

/** Persistent game-owned interaction; the host owns transport and account lifecycle. */
export type SlotGameFeaturePanelProps = Readonly<{
  state: unknown | null
  feature: unknown | null
  spinKey: number | null
  isSpinning: boolean
  reducedMotion: boolean
  pointValueInCents: number
  actionPending: boolean
  disabled?: boolean
  error: string | null
  onAction: (action: unknown) => Promise<void>
  onOpen?: () => void
  onPresentationBusyChange?: (busy: boolean) => void
}>

export type SlotGameFeatureActions = Readonly<{
  path: string
}>

export type SlotExperiencePresentation = {
  paymentAlerts?: boolean
  outcomePresentation?: 'banner' | 'ambient'
  symbolValueGuide?: Readonly<{
    mode?: 'auto' | 'on-demand' | 'modal' | 'none'
    className?: string
  }>
  SpinFeaturePanel?: ComponentType<SlotSpinFeaturePanelProps>
  FeaturePanel?: ComponentType<SlotGameFeaturePanelProps>
  /** Synthetic awards are shown by the game's panel rather than reel paylines. */
  featurePaylineIds?: readonly number[]
  winHelp?: Readonly<{
    Content: ComponentType<SlotWinHelpContentProps>
    title: string
    eyebrow?: string
    theme?: string
    firstOpenStorageKey?: string
  }>
  winPresentation?: 'payline-popups'
  mainWordmark?: Readonly<{ src: string; width: number; height: number; className: string }>
  stageWordmark?: Readonly<{ src: string; width: number; height: number; className: string }>
  specialBackdropImage?: string
  specialIntroImage?: string
  LightningCollectionOverlay?: ComponentType<{
    collection: LightningCollection
    reducedMotion: boolean
  }>
  TumbleAwardBadge?: ComponentType<{
    positions: readonly GridPosition[]
    columns: number
    rows: number
    amount: string
  }>
  CollectionFlyover?: ComponentType<{
    flyover: SlotPresentationFlyover
    image: string
    imageSrcSet?: string
  }>
}

export type SlotExperienceSet = {
  id: string
  cabinet: SlotCabinetTheme
  features: SlotFeatureSet
  help: SlotHelpDefinition
  outcomeNarrative?: SlotOutcomeNarrative
  shellBackdrop: 'default-clouds' | 'theme'
  symbols: SlotSymbolSet
  mascot: MascotSet | null
  sounds: SlotSoundSet
  rules: SlotRulesSet
  presentation?: SlotExperiencePresentation
  featureActions?: SlotGameFeatureActions
}

const BASE_SLOT_RULES_SET: Omit<SlotRulesSet, 'gameId'> = {
  initialReels: [
    ['2', '3', '4', 'BOLT'],
    ['3', '4', '5', '6'],
    ['4', '5', '6', '7'],
    ['5', '6', '7', 'ACE'],
    ['FREE', '7', 'ACE', 'POWER'],
  ],
  pointValueInCents: 25,
  wagerOptions: Array.from({ length: 1_000 }, (_, index) => (index + 1) * 2),
  autoSpinDelayMs: 1_000,
  autoSpinSpeedMultiplier: 3,
  baseSpinSpeedMultiplier: 1,
  reelTravelRowCount: 8,
  specialBoostCost: 8,
  energyMeterCapacity: 100,
}

export const createSlotRulesSet = (
  gameId: string,
  overrides: Partial<Omit<SlotRulesSet, 'gameId'>> = {},
): SlotRulesSet => ({
  ...BASE_SLOT_RULES_SET,
  ...overrides,
  gameId,
})
