import type { SlotGameCatalogEntry } from '../catalogTypes'
import jadeDragonTitle from '../../../assets/catalogue/slots/jade-dragon-title-v1.webp'
export const EMPTY_SLOTS_GAME_CATALOG: SlotGameCatalogEntry = {
  id: 'empty-slots-game', title: 'Jade Dragon: The Golden Pearl', shortTitle: 'Jade Dragon',
  description: 'Enter a vibrant jade sanctuary with a fierce dragon, luminous pearls, free games, and expanding wild features.',
  image: jadeDragonTitle, imagePresentation: 'cover',
  playHref: '/slots/empty-slots-game', demoHref: '/slots/empty-slots-game', serverGameIds: ['empty-slots-v1'],
}
