import type { MascotSet } from '../../../games/slots/shared/mascot/mascotTypes'
import type { SlotSymbolId } from '../types/slots'
import type { SlotFeatureSet, SlotHelpDefinition } from './slotFeatures'
import type { SlotCabinetTheme } from './cabinetThemes'
import type { SlotSoundSet } from './soundSets'
import type { SlotSymbolSet } from './symbolSets'

export type SlotRulesSet = {
  gameId: string
  initialReels: readonly (readonly SlotSymbolId[])[]
  pointValueInCents: number
  wagerOptions: readonly number[]
  autoSpinDelayMs: number
  autoSpinSpeedMultiplier: number
  specialBoostCost: number
  energyMeterCapacity: number
}

export type SlotExperienceSet = {
  id: string
  cabinet: SlotCabinetTheme
  features: SlotFeatureSet
  help: SlotHelpDefinition
  shellBackdrop: 'default-clouds' | 'theme'
  symbols: SlotSymbolSet
  mascot: MascotSet | null
  sounds: SlotSoundSet
  rules: SlotRulesSet
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
