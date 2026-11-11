import dawnLake from './assets/art/optimized/reel-riches-lake-v2.webp'
import blueMarlin from './assets/art/optimized/reel-riches-blue-marlin-v2.webp'
import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'

export const REEL_RICHES_CATALOG: SlotGameCatalogDefinition = {
  id: 'reel-riches',
  title: 'Reel Riches',
  shortTitle: 'Reel Riches',
  description: 'Cast onto a sunrise lake, complete four tackle collections, and sweep pearl prizes into the fishing net.',
  image: blueMarlin,
  imagePresentation: 'contain',
  slotDivBackgroundImage: dawnLake,
}
