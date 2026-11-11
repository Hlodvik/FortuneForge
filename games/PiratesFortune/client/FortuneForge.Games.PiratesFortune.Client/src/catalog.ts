import goldPirateCoin from './assets/art/optimized/responsive/192/gold-pirate-coin.webp'
import piratesCove from './assets/art/optimized/pirates-cove.png'
import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'

export const PIRATES_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'pirates-fortune',
  title: "Pirates' Fortune",
  shortTitle: "Pirates' Fortune",
  description: 'Sail into a moonlit cove, collect four treasure gems, and let the Doubloon Purse plunder every coin in sight.',
  image: goldPirateCoin,
  imagePresentation: 'contain',
  imageScale: 'compact',
  slotDivBackgroundImage: piratesCove,
}
