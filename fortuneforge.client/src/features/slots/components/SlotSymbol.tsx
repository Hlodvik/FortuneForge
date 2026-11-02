import type { CSSProperties } from 'react'
import { getSlotSymbolDefinition, getSlotSymbolImageSrcSet, type SlotSymbolSet } from '../config/symbolSets'
import type { SlotSymbolId } from '../types/slots'
import { ResponsiveSlotImage } from './ResponsiveSlotImage'

type SlotSymbolProps = {
  animated?: boolean
  beingGrabbed?: boolean
  highlighted?: boolean
  highlightOrder?: number
  reelIndex?: number
  rowIndex?: number
  symbol: SlotSymbolId
  symbolSet: SlotSymbolSet
  valueLabel?: string
}

export function SlotSymbol({
  animated = true,
  beingGrabbed = false,
  highlighted = false,
  highlightOrder = 0,
  reelIndex,
  rowIndex,
  symbol,
  symbolSet,
  valueLabel,
}: SlotSymbolProps) {
  // The reel supplies its symbol set, keeping this renderer theme-agnostic.
  const definition = getSlotSymbolDefinition(symbolSet, symbol)
  const image = animated && definition.animatedImage ? definition.animatedImage : definition.image
  const responsiveImages = image === definition.image ? definition.responsiveImages : undefined
  const className = [
    'slot-symbol',
    `slot-symbol--${symbol.toLowerCase()}`,
    beingGrabbed ? 'slot-symbol--being-grabbed' : '',
    highlighted ? 'slot-symbol--winner' : '',
  ].filter(Boolean).join(' ')
  const style = highlighted
    ? ({ '--win-delay': `${Math.min(420, Math.max(0, highlightOrder) * 70)}ms` } as CSSProperties)
    : undefined

  return (
    <span
      className={className}
      aria-label={`${definition.label}${highlighted ? ', winning symbol' : ''}`}
      data-reel-index={reelIndex}
      data-row-index={rowIndex}
      data-symbol={symbol}
      style={style}
    >
      <div
        className={`slot-symbol__artwork${responsiveImages ? ' slot-symbol__artwork--responsive' : ''}`}
        data-value-label={valueLabel ?? definition.valueLabel}
      >
        <ResponsiveSlotImage
          className="slot-symbol__image"
          src={image}
          imageSrcSet={getSlotSymbolImageSrcSet(responsiveImages)}
          alt=""
          aria-hidden="true"
        />
      </div>
    </span>
  )
}
