import olympusEmblem from '../../../assets/slots/games/gods-of-olympus/optimized/olympus-emblem.png'
import olympusTerrace from '../../../assets/slots/games/gods-of-olympus/optimized/olympus-terrace.png'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const GODS_OF_OLYMPUS_CATALOG: SlotGameCatalogDefinition = {
  id: 'gods-of-olympus',
  title: 'Gods of Olympus',
  shortTitle: 'Gods of Olympus',
  description: 'Match eight or more symbols on Mount Olympus. When they clear, fresh symbols drop in for another chance to win.',
  image: olympusEmblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: olympusTerrace,
}
