import pokerRoom from '../../../assets/slots/games/royal-draw/poker-room.svg'
import royalDrawEmblem from '../../../assets/slots/games/royal-draw/royal-draw-emblem.svg'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const ROYAL_DRAW_CATALOG: SlotGameCatalogDefinition = {
  id: 'royal-draw',
  title: 'Royal Draw',
  shortTitle: 'Royal Draw',
  description: 'Take a seat at the high-stakes table, complete all four suits, and let the dealer tray sweep the jackpot chips.',
  image: royalDrawEmblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: pokerRoom,
}
