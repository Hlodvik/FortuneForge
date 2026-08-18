import captainCrest from '../../../assets/slots/games/pirates-fortune/captain-crest.png'
import piratesCove from '../../../assets/slots/games/pirates-fortune/pirates-cove.png'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const PIRATES_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'pirates-fortune',
  title: "Pirates' Fortune",
  shortTitle: "Pirates' Fortune",
  description: 'Sail into a moonlit cove, collect four treasure gems, and let the Jolly Roger plunder every doubloon in sight.',
  image: captainCrest,
  imagePresentation: 'contain',
  slotDivBackgroundImage: piratesCove,
}
