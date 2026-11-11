// Compatibility barrel for the host. The neutral runtime is the single source
// of truth so package manifests and host routes cannot drift structurally.
export { createSlotRulesSet } from '@fortuneforge/slot-game-runtime/experience'

export type {
  SlotExperiencePresentation,
  SlotExperienceSet,
  SlotPresentationFlyover,
  SlotRulesSet,
} from '@fortuneforge/slot-game-runtime/experience'
