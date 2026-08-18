import olympusEmblem from '../../../assets/slots/games/gods-of-olympus/olympus-emblem.png'
import olympusTerrace from '../../../assets/slots/games/gods-of-olympus/olympus-terrace.png'
import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'

export const GODS_OF_OLYMPUS_CATALOG: SlotGameCatalogDefinition = {
  id: 'gods-of-olympus',
  title: 'Gods of Olympus',
  shortTitle: 'Gods of Olympus',
  description: 'Climb Mount Olympus, collect four divine medallions, and let the Gauntlet of Zeus claim a shower of drachmas.',
  image: olympusEmblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: olympusTerrace,
}
