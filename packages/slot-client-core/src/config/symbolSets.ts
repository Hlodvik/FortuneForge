import type { SlotSymbolId } from '../types/slots'

/** Exact-sized artwork variants for shared image rendering. */
export type SlotResponsiveImages = Readonly<
  { 96: string; 192: string } & ({ 256: string; 320?: never } | { 256?: never; 320: string })
>

export type SlotSymbolDefinition<TSymbol extends string = SlotSymbolId> = {
  id: TSymbol
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

export type SlotSymbolGuideEntry<TSymbol extends string = SlotSymbolId> = {
  symbol: TSymbol
  compactInSidePanel?: boolean
  payoutScale?: number
  firstLabel: string
  firstValue: string
  secondLabel?: string
  secondValue?: string
  artworkValueLabel?: string
}

export type SlotSymbolSet<TSymbol extends string = SlotSymbolId> = {
  id: string
  serverSymbolSetId?: string
  definitions: Readonly<Partial<Record<TSymbol, SlotSymbolDefinition<TSymbol>>>>
  guideEntries: readonly SlotSymbolGuideEntry<TSymbol>[]
  /** Symbols kept in consecutive guide rows, in the specified order. */
  guideSymbolGroups?: readonly (readonly TSymbol[])[]
  guidePayoutScale?: number
  guideSubtitle?: string
  /** Opt-in reel filler identities for games with dormant compatibility metadata. */
  animationSymbolIds?: readonly TSymbol[]
  /** Optional explanation for a game's displayed, calibrated multipliers. */
  guideNote?: string
}

export function getSlotAnimationSymbolIds<TSymbol extends string>(symbolSet: SlotSymbolSet<TSymbol>): readonly TSymbol[] {
  const definitions = Object.values(symbolSet.definitions) as (SlotSymbolDefinition<TSymbol> | undefined)[]
  return symbolSet.animationSymbolIds ?? definitions.flatMap(definition =>
    definition ? [definition.id] : [])
}

export function getSlotSymbolDefinition<TSymbol extends string>(
  symbolSet: SlotSymbolSet<TSymbol>,
  symbol: TSymbol,
): SlotSymbolDefinition<TSymbol> {
  const definition = symbolSet.definitions[symbol]
  if (!definition) {
    throw new Error(`Slot symbol '${symbol}' is not defined by '${symbolSet.id}'.`)
  }
  return definition
}
