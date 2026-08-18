import dawnLake from '../../../assets/slots/games/reel-riches/dawn-lake.svg'
import reelRichesEmblem from '../../../assets/slots/games/reel-riches/reel-riches-emblem.svg'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const REEL_RICHES_CATALOG: SlotGameCatalogDefinition = {
  id: 'reel-riches',
  title: 'Reel Riches',
  shortTitle: 'Reel Riches',
  description: 'Cast onto a sunrise lake, complete four tackle collections, and sweep pearl prizes into the fishing net.',
  image: reelRichesEmblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: dawnLake,
}
