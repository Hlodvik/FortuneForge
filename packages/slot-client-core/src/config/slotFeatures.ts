export type SlotHelpSection = { badge: string; title: string; body: string }
export type SlotHelpDefinition = {
  paylineCount: number
  paylinePatternIds?: readonly number[]
  fiveMatchPayoutNote?: string
  fourMatchPayoutNote?: string
  freeGames?: { awardedSpins: number; requiredSymbols: number; badge?: string; title?: string; symbolLabel?: string; awardLabel?: string }
  extraSections?: readonly SlotHelpSection[]
}
export type SlotSpecialRoundFeature = {
  id: string
  title: string
  earnLabel: string
  activeModes: Readonly<Record<string, string>>
  earnStyle?: string
  earnHint?: string
}
