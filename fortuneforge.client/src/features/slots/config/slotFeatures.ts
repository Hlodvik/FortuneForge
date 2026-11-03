import type { ComponentType, Ref } from 'react'
import type { SlotSymbolId } from '../types/slots'

export type SlotCollectionDefinition = {
  id: string
  label: string
  /** Compact cabinet copy; the full label remains available to assistive tech and help surfaces. */
  displayLabel?: string
  shortLabel: string
  symbol: SlotSymbolId
  requiredCount: number
  /** Plain-language outcome shown when players inspect this collection. */
  rewardDescription?: string
  progressDescription?: string
  containerImage?: string
}

export type SlotCollectionPresentation =
  | 'seal-pile'
  | 'juice-glass'
  | 'gem-hoard'
  | 'vault-access'
  | 'divine-offering'
  | 'tackle-creel'
  | 'frontier-trail'
  | 'chip-stack'
  | 'spellbook-shelf'
  | 'star-orbit'
  | 'fossil-dig'
  | 'celestial-orbit'

export type SlotCollectionFeature = {
  ariaLabel: string
  entries: readonly SlotCollectionDefinition[]
  itemLabel?: string
  containerImage?: string
  presentation?: SlotCollectionPresentation
  completionDialog?: boolean
  completionTitle?: string
  completionDetail?: string
}

export type SlotEnergyAward = {
  demoState: Record<string, number>
  valueCents: number
  payoutCents: number
  completed: boolean
}

export type SlotEnergyAwardMeterProps = {
  balance: number
  capacity: number
  meterRef: Ref<HTMLDivElement>
  award: SlotEnergyAward | null
  completing: boolean
}

export type SlotEnergyFeature = {
  label: string
  symbol: SlotSymbolId
  completionAward?: {
    decode: (response: object) => SlotEnergyAward | null
    Meter: ComponentType<SlotEnergyAwardMeterProps>
    durationMs: number
  }
  milestones?: readonly number[]
}

export type SlotMoneyGrabFeature = {
  actorName: string
  awardLabel: string
  collectorSymbol: SlotSymbolId
  presentation?: 'separate-award'
} & (
  | { valueSymbolPrefix: string; valueSymbols?: never }
  | { valueSymbolPrefix?: never; valueSymbols: readonly SlotSymbolId[] }
)

export type SlotSpecialRoundEarnStyle =
  | 'altar'
  | 'buckets'
  | 'cards'
  | 'dig'
  | 'gates'
  | 'glass'
  | 'orbit'
  | 'shelf'
  | 'stack'
  | 'trail'

export type SlotSpecialRoundFeature = {
  id: string
  title: string
  earnLabel: string
  activeModes: Readonly<Record<string, string>>
  earnStyle?: SlotSpecialRoundEarnStyle
  earnHint?: string
}

export type SlotFeatureSet = {
  collections?: SlotCollectionFeature
  energy?: SlotEnergyFeature
  lightningCollection?: { collectorSymbol: SlotSymbolId }
  moneyGrab?: SlotMoneyGrabFeature
  specialRound?: SlotSpecialRoundFeature
}

export type SlotHelpSection = {
  badge: string
  title: string
  body: string
}

export type SlotHelpDefinition = {
  paylineCount: number
  paylinePatternIds?: readonly number[]
  fiveMatchPayoutNote?: string
  fourMatchPayoutNote?: string
  freeGames?: {
    awardedSpins: number
    requiredSymbols: number
    badge?: string
    title?: string
    symbolLabel?: string
    awardLabel?: string
  }
  extraSections?: readonly SlotHelpSection[]
}

export const NO_SLOT_FEATURES: SlotFeatureSet = {}

export function getSpecialRoundLabel(
  feature: SlotSpecialRoundFeature,
  mode: string | null,
): string {
  if (!mode) return feature.title
  const directLabel = feature.activeModes[mode]
  if (directLabel) return directLabel
  const modes = mode.split('-')
  const labels = modes.map((part) => feature.activeModes[part])
  return modes.length > 1 && labels.every(Boolean) ? labels.join(' · ') : feature.title
}
