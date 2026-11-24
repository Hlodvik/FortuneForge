import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'
import { PHARAOHS_ASCENT_VISUALS as art } from './visuals'

export const PHARAOHS_ASCENT_CATALOG: SlotGameCatalogDefinition = {
  id: 'pharaohs-ascent',
  title: 'Pharaoh’s Ascent', shortTitle: 'Pharaoh’s Ascent',
  description: 'Explore an ancient pyramid beyond the reels. Collect relics, bank treasure, and unlock the sarcophagus.',
  image: art.title, imagePresentation: 'cover', slotDivBackgroundImage: art.backdrop,
}
