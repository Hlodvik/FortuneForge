import highNoonEmblem from '../../../assets/slots/games/high-noon-fortune/high-noon-emblem.svg'
import westernTown from '../../../assets/slots/games/high-noon-fortune/western-town.svg'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const HIGH_NOON_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'high-noon-fortune',
  title: 'High Noon Fortune',
  shortTitle: 'High Noon',
  description: 'Ride into a sunset frontier town, complete four badge trails, and lasso every gold nugget on the reels.',
  image: highNoonEmblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: westernTown,
}
