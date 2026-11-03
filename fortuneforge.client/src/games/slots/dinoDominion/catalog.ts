import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'
import { GOLD_FOUNDRY_VISUALS as art } from './visuals'

export const DINO_DOMINION_CATALOG: SlotGameCatalogDefinition = {
  id: 'dino-dominion',
  title: 'Gold Foundry',
  shortTitle: 'Gold Foundry',
  description: 'Match metal ingots on paylines, collect them with a reel crucible, and fill the crucible above the reels for a payout based on your paid wagers.',
  image: art.crucible,
  imagePresentation: 'contain',
  slotDivBackgroundImage: art.backdrop,
}
