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
  SlotPotionSoundEvent,
  SlotSoundSet,
  SlotSymbolId,
  SlotSymbolSet,
} from '@fortuneforge/slot-client-core'
import type { ComponentType, RefObject } from 'react'
import type { SlotFeatureSet, SlotHelpDefinition } from './features.js'
import type { MascotSet } from './mascot.js'
import type { SlotOutcomeNarrative } from './outcomeNarratives.js'

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
  tumble?: Readonly<{
    columns: number
    rows: number
    symbols: readonly SlotSymbolId[]
    initialReels: readonly (readonly SlotSymbolId[])[]
  }>
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

export type SlotTomeState = {
  selectedPage: string
  activePage: string | null
  boostedSpinsRemaining: number
  grimoireReady: boolean
  ingredients: Readonly<Record<string, number>>
  pages: readonly Readonly<{ id: string; label: string; ingredients: readonly string[] }>[]
}

export type SlotPotionPreview = Readonly<{
  ingredientId: SlotSymbolId
  name: string
  effect: string
  potency: number
}>

export type SlotPotionBottle = SlotPotionPreview & Readonly<{ id: number }>

export type SlotPotionState = Readonly<{
  capacity: number
  majority: number
  pot: readonly SlotSymbolId[]
  bottles: readonly SlotPotionBottle[]
  active: Readonly<{ bottle: SlotPotionBottle; spinsRemaining: number }> | null
  preview: SlotPotionPreview | null
}>

export type SlotTomePanelProps = {
  state: SlotTomeState
  potions: SlotPotionState | null
  awarded: Readonly<Record<string, number>>
  cabinetRef: RefObject<HTMLDivElement | null>
  settledSpin: Readonly<{
    key: number
    reels: readonly (readonly SlotSymbolId[])[]
  }> | null
  reducedMotion: boolean
  disabled: boolean
  error: string | null
  onOpen: () => void
  onChangePage: (page: string, activate: boolean) => void
  onUsePotion: (id: number) => void
  confirmedPotionUse?: number | null
  onPotionCue?: (event: SlotPotionSoundEvent) => void
}

export type SlotWinHelpContentProps = {
  onClose: () => void
}

export type SlotExperiencePresentation = {
  winHelp?: Readonly<{
    Content: ComponentType<SlotWinHelpContentProps>
    title: string
    eyebrow?: string
    theme?: string
    firstOpenStorageKey?: string
  }>
  TomePanel?: ComponentType<SlotTomePanelProps>
  tomeTriggerSymbol?: SlotSymbolId
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
