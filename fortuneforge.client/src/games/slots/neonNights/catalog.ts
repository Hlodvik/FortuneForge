// Edited: 2026-11-18T22:54:10-06:00 | Computer: Vinnabok
import image from '../../../assets/catalogue/slots/neon-nights-title-v1.webp'
import type { SlotGameCatalogDefinition } from '../catalogTypes'

// Lobby-only metadata and card art: opening the lobby never loads the game package.
export const NEON_NIGHTS_CATALOG: SlotGameCatalogDefinition = {
  id: 'neon-nights', title: 'Neon Nights', shortTitle: 'Neon Nights',
  description: 'Race through an electric city of arcade lights, synth beats, and glowing midnight prizes.',
  image, imagePresentation: 'contain',
}
