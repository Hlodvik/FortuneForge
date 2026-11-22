import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'
import { FIFTH_SUN_TITLE, FIFTH_SUN_DESCRIPTION } from './definition'
import { FIFTH_SUN_SYMBOL_IMAGES as artwork } from './symbols'

export const FIFTH_SUN_CATALOG: SlotGameCatalogDefinition = {
  id: 'robot-revolution', title: FIFTH_SUN_TITLE, shortTitle: FIFTH_SUN_TITLE,
  description: FIFTH_SUN_DESCRIPTION, image: artwork.SUNSTONE,
  imagePresentation: 'contain', slotDivBackgroundImage: artwork.BACKDROP,
}
export const SUNSTONE_ECLIPSE_CATALOG = FIFTH_SUN_CATALOG
export const ROBOT_REVOLUTION_CATALOG = FIFTH_SUN_CATALOG
