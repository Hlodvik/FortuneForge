import pokerRoom from '../../../assets/slots/games/royal-draw/optimized/royal-draw-poker-salon-v2.webp'
import royalFlush from '../../../assets/slots/games/royal-draw/optimized/royal-draw-royal-flush-v2.webp'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const ROYAL_DRAW_CATALOG: SlotGameCatalogDefinition = {
  id: 'royal-draw',
  title: 'Royal Draw',
  shortTitle: 'Royal Draw',
  description: 'Take a seat at the high-stakes table, complete all four suits, and let the dealer tray sweep the jackpot chips.',
  image: royalFlush,
  imagePresentation: 'contain',
  slotDivBackgroundImage: pokerRoom,
}
