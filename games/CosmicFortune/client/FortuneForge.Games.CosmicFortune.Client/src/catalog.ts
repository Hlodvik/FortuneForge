import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'

export const COSMIC_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'cosmic-fortune',
  title: 'Pharaoh’s Ascent', shortTitle: 'Pharaoh’s Ascent',
  description: 'Explore an ancient pyramid beyond the reels. Collect relics, bank treasure, and unlock the sarcophagus.',
  image: art.title, imagePresentation: 'cover', slotDivBackgroundImage: art.backdrop,
}
