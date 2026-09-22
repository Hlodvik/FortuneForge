import type { SlotSymbolId } from '../types/slots'

export type SlotCollectionFillImages = readonly [string, ...string[]]

export type SlotCollectionDefinition = {
  id: string
  label: string
  shortLabel: string
  symbol: SlotSymbolId
  requiredCount: number
  /** Plain-language outcome shown when players inspect this collection. */
  rewardDescription?: string
  containerImage?: string
  containerFillImages?: SlotCollectionFillImages
}

export type SlotCollectionPresentation =
  | 'seal-pile'
  | 'juice-glass'
  | 'gem-hoard'
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
}

export type SlotEnergyFeature = {
  label: string
  symbol: SlotSymbolId
}

export type SlotMoneyGrabFeature = {
  actorName: string
  awardLabel: string
  collectorSymbol: SlotSymbolId
  valueSymbolPrefix: string
}

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
  showStatusPanel?: boolean
}

export type SlotFeatureSet = {
  collections?: SlotCollectionFeature
  energy?: SlotEnergyFeature
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
  freeGames?: {
    awardedSpins: number
    requiredSymbols: number
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
  return mode ? feature.activeModes[mode] ?? feature.title : feature.title
}
