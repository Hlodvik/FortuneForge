import { symbolRegistry } from '../config/symbolRegistry'
import type { SlotSymbolId } from '../types/slots'

type SlotSymbolProps = {
  symbol: SlotSymbolId
}

export function SlotSymbol({ symbol }: SlotSymbolProps) {
  const definition = symbolRegistry[symbol]

  return (
    <span className={`slot-symbol slot-symbol--${symbol.toLowerCase()}`} aria-label={definition.label}>
      <img className="slot-symbol__card" src={definition.image} alt="" aria-hidden="true" />
      <span className="slot-symbol__rank">{definition.displayText}</span>
    </span>
  )
}
