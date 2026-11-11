import image from '../../../assets/catalogue/slots/gold-foundry-title-v1.webp'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

// Lobby-only metadata and card art: opening the lobby never loads the game package.
export const GOLD_FOUNDRY_CATALOG: SlotGameCatalogDefinition = {
  id: 'gold-foundry',
  title: 'Gold Foundry',
  shortTitle: 'Gold Foundry',
  description: 'Match metal ingots on paylines, collect them with a reel crucible, and fill the crucible above the reels for a payout based on your paid wagers.',
  image,
  imagePresentation: 'contain',
}
