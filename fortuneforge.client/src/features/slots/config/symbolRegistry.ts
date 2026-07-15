import playingCardSymbol from '../../../assets/slots/playing-card-symbol.png'
import type { SlotSymbolId } from '../types/slots'

export type SlotSymbolDefinition = {
  id: SlotSymbolId
  label: string
  displayText: string
  image: string
}

export const symbolRegistry: Record<SlotSymbolId, SlotSymbolDefinition> = {
  '2': { id: '2', label: 'Two', displayText: '2', image: playingCardSymbol },
  '3': { id: '3', label: 'Three', displayText: '3', image: playingCardSymbol },
  '4': { id: '4', label: 'Four', displayText: '4', image: playingCardSymbol },
  '5': { id: '5', label: 'Five', displayText: '5', image: playingCardSymbol },
  '6': { id: '6', label: 'Six', displayText: '6', image: playingCardSymbol },
  '7': { id: '7', label: 'Seven', displayText: '7', image: playingCardSymbol },
  ACE: { id: 'ACE', label: 'Ace wild', displayText: 'A', image: playingCardSymbol },
}
