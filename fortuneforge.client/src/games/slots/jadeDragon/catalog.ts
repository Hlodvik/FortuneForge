import type { SlotGameCatalogEntry } from '../catalogTypes'
import jadeDragonTitle from '../../../assets/catalogue/slots/jade-dragon-title-v1.webp'

export const JADE_DRAGON_CATALOG: SlotGameCatalogEntry = {
  // Retain the production analytics key; the public route and title are Jade Dragon.
  id: 'empty-slots-game',
  title: 'Jade Dragon: The Golden Pearl',
  shortTitle: 'Jade Dragon',
  description: 'Enter a vibrant jade sanctuary with a fierce dragon, luminous pearls, free games, and expanding wild features.',
  image: jadeDragonTitle,
  imagePresentation: 'cover',
  playHref: '/slots/jade-dragon',
  demoHref: '/slots/jade-dragon',
  serverGameIds: ['empty-slots-v1'],
}
