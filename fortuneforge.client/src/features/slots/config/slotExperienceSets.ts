import {
  RAINBOW_REALM_MASCOT,
  WUKONG_MASCOT,
  type MascotSet,
} from '../../../components/WukongCompanion.config'
import type { SlotSymbolId } from '../types/slots'
import {
  RAINBOW_REALM_CABINET_THEME,
  WUKONG_CABINET_THEME,
  type SlotCabinetTheme,
} from './cabinetThemes'
import { DEFAULT_SLOT_SOUNDS, type SlotSoundSet } from './soundSets'
import {
  RAINBOW_REALM_SYMBOLS,
  WUKONG_SYMBOLS,
  type SlotSymbolSet,
} from './symbolSets'

export type SlotRulesSet = {
  gameId: string
  initialReels: readonly (readonly SlotSymbolId[])[]
  wagerOptions: readonly number[]
  autoSpinDelayMs: number
  autoSpinSpeedMultiplier: number
  specialBoostCost: number
  energyMeterCapacity: number
}

export type SlotExperienceSet = {
  id: string
  cabinet: SlotCabinetTheme
  symbols: SlotSymbolSet
  mascot: MascotSet | null
  sounds: SlotSoundSet
  rules: SlotRulesSet
}

const DEFAULT_SLOT_RULES_SET: SlotRulesSet = {
  gameId: 'classic-demo-v1',
  initialReels: [
    ['2', '3', '4', 'BOLT'],
    ['3', '4', '5', '6'],
    ['4', '5', '6', '7'],
    ['5', '6', '7', 'ACE'],
    ['FREE', '7', 'ACE', 'POWER'],
  ],
  wagerOptions: [50, 100, 250, 500],
  autoSpinDelayMs: 1_000,
  autoSpinSpeedMultiplier: 3,
  specialBoostCost: 8,
  energyMeterCapacity: 100,
}

const createSlotRulesSet = (gameId: string): SlotRulesSet => ({
  ...DEFAULT_SLOT_RULES_SET,
  gameId,
})

// Re-export the named building blocks next to their composed experience.
export {
  DEFAULT_SLOT_SOUNDS,
  RAINBOW_REALM_CABINET_THEME,
  RAINBOW_REALM_MASCOT,
  RAINBOW_REALM_SYMBOLS,
  WUKONG_CABINET_THEME,
  WUKONG_MASCOT,
  WUKONG_SYMBOLS,
}

// SlotsPage consumes one composed set. Swapping a theme is now a configuration
// choice instead of a rewrite across page, reel, mascot, and audio modules.
export const DEFAULT_SLOT_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'fortune-forge-wukong-v1',
  cabinet: WUKONG_CABINET_THEME,
  symbols: WUKONG_SYMBOLS,
  mascot: null,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: DEFAULT_SLOT_RULES_SET,
}

export const RAINBOW_REALM_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'rainbow-realm-fruits-v1',
  cabinet: RAINBOW_REALM_CABINET_THEME,
  symbols: RAINBOW_REALM_SYMBOLS,
  mascot: RAINBOW_REALM_MASCOT,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('rainbow-realm-fruits-v1'),
}

export const SLOT_EXPERIENCE_SETS_BY_ROUTE: Readonly<Record<string, SlotExperienceSet>> = {
  '/slots/wukong': DEFAULT_SLOT_EXPERIENCE_SET,
  '/slots/rainbow-realm': RAINBOW_REALM_EXPERIENCE_SET,
}
