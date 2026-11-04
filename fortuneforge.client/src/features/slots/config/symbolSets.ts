import type { SlotSymbolId } from '../types/slots'

export const CLASSIC_SLOT_SYMBOL_SET_ID = 'classic-symbols-v3'

/** Matches the responsive artwork contract already used by Rainbow Realm and Pirates. */
export type SlotResponsiveImages = Readonly<
  { 96: string; 192: string } & ({ 256: string; 320?: never } | { 256?: never; 320: string })
>

export type SlotSymbolDefinition = {
  id: SlotSymbolId
  label: string
  image: string
  /** Exact-sized artwork variants; omitted for existing single-source art. */
  responsiveImages?: SlotResponsiveImages
  animatedImage?: string
  valueLabel?: string
  wagerMultiplier?: number
}

export function getSlotSymbolImageSrcSet(images?: SlotResponsiveImages): string | undefined {
  if (!images) return undefined
  const maximumWidth = images[320] !== undefined ? 320 : 256
  return `${images[96]} 96w, ${images[192]} 192w, ${images[maximumWidth]} ${maximumWidth}w`
}

export type SlotSymbolGuideEntry = {
  symbol: SlotSymbolId
  compactInSidePanel?: boolean
  payoutScale?: number
  firstLabel: string
  firstValue: string
  secondLabel?: string
  secondValue?: string
  artworkValueLabel?: string
}

export type SlotSymbolSet = {
  id: string
  serverSymbolSetId?: string
  definitions: Readonly<Partial<Record<SlotSymbolId, SlotSymbolDefinition>>>
  guideEntries: readonly SlotSymbolGuideEntry[]
  guidePayoutScale?: number
  guideSubtitle?: string
  /** Opt-in reel filler identities for games with dormant compatibility metadata. */
  animationSymbolIds?: readonly SlotSymbolId[]
  /** Optional explanation for a game's displayed, calibrated multipliers. */
  guideNote?: string
}

export function getSlotAnimationSymbolIds(symbolSet: SlotSymbolSet): readonly SlotSymbolId[] {
  return symbolSet.animationSymbolIds ?? Object.values(symbolSet.definitions).flatMap(definition =>
    definition ? [definition.id] : [])
}

export function getSlotSymbolDefinition(
  symbolSet: SlotSymbolSet,
  symbol: SlotSymbolId,
): SlotSymbolDefinition {
  const definition = symbolSet.definitions[symbol]
  if (!definition) {
    throw new Error(`Slot symbol '${symbol}' is not defined by '${symbolSet.id}'.`)
  }
  return definition
}
