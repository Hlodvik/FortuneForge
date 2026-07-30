import type { SlotSymbolId } from '../types/slots'

export type SlotCollectionDefinition = {
  id: string
  label: string
  shortLabel: string
  symbol: SlotSymbolId
  requiredCount: number
}

export type SlotCollectionFeature = {
  ariaLabel: string
  entries: readonly SlotCollectionDefinition[]
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

export type SlotFeatureSet = {
  collections?: SlotCollectionFeature
  energy?: SlotEnergyFeature
  moneyGrab?: SlotMoneyGrabFeature
}

export type SlotHelpSection = {
  badge: string
  title: string
  body: string
}

export type SlotHelpDefinition = {
  paylineCount: number
  freeGames?: {
    awardedSpins: number
    requiredSymbols: number
  }
  extraSections?: readonly SlotHelpSection[]
}

export const NO_SLOT_FEATURES: SlotFeatureSet = {}
