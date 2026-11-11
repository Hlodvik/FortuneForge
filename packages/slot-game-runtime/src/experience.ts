import type {
  GridPosition,
  LightningCollection,
  SlotCabinetTheme,
  SlotSoundSet,
  SlotSymbolId,
  SlotSymbolSet,
} from '@fortuneforge/slot-client-core'
import type { ComponentType } from 'react'
import type { SlotFeatureSet, SlotHelpDefinition } from './features.js'
import type { MascotSet } from './mascot.js'
import type { SlotOutcomeNarrative } from './outcomeNarratives.js'

export type SlotRulesSet = {
  gameId: string
  initialReels: readonly (readonly SlotSymbolId[])[]
  pointValueInCents: number
  wagerOptions: readonly number[]
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

export type SlotExperiencePresentation = {
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
