import type { CSSProperties } from 'react'
import { getSlotSymbolDefinition, type SlotSymbolSet } from '../config/symbolSets'
import type { SlotSymbolId } from '../types/slots'

type SlotSymbolProps = {
  animated?: boolean
  beingGrabbed?: boolean
  highlighted?: boolean
  highlightOrder?: number
  reelIndex?: number
  rowIndex?: number
  symbol: SlotSymbolId
  symbolSet: SlotSymbolSet
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
}: SlotSymbolProps) {
  // The reel supplies its symbol set, keeping this renderer theme-agnostic.
  const definition = getSlotSymbolDefinition(symbolSet, symbol)
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
        className="slot-symbol__artwork"
        data-value-label={definition.valueLabel}
      >
        <img
          className="slot-symbol__image"
          src={animated && definition.animatedImage ? definition.animatedImage : definition.image}
          alt=""
          aria-hidden="true"
        />
      </div>
    </span>
  )
}
