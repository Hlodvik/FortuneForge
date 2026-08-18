import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'
import { DINO_DOMINION_VISUALS as art } from './visuals'

export const DINO_DOMINION_CATALOG: SlotGameCatalogDefinition = {
  id: 'dino-dominion',
  title: 'Dino Dominion',
  shortTitle: 'Dino Dominion',
  description: 'Dig through a prehistoric valley, uncover four fossil beds, and send rare amber to the museum.',
  image: art.emblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: art.backdrop,
}
