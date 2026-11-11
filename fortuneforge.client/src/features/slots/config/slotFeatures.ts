// Compatibility barrel for host components. Game packages and the host share
// this one neutral contract instead of maintaining structurally similar React
// component types in separate compilation units.
export {
  getSpecialRoundLabel,
  NO_SLOT_FEATURES,
} from '@fortuneforge/slot-game-runtime'

export type {
  SlotCollectionDefinition,
  SlotCollectionFeature,
  SlotCollectionPresentation,
  SlotEnergyAward,
  SlotEnergyAwardMeterProps,
  SlotEnergyFeature,
  SlotFeatureSet,
  SlotHelpDefinition,
  SlotHelpSection,
  SlotMoneyGrabFeature,
  SlotSpecialRoundEarnStyle,
  SlotSpecialRoundFeature,
} from '@fortuneforge/slot-game-runtime'
