import stagecoach from './assets/art/optimized/high-noon-stagecoach-v2.webp'
import westernTown from './assets/art/optimized/high-noon-town-v2.webp'
import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'

export const HIGH_NOON_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'high-noon-fortune',
  title: 'High Noon Fortune',
  shortTitle: 'High Noon',
  description: 'Ride into a sunset frontier town, complete four badge trails, and lasso every gold nugget on the reels.',
  image: stagecoach,
  imagePresentation: 'contain',
  slotDivBackgroundImage: westernTown,
}
