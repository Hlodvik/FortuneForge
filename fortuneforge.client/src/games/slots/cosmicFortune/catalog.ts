import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'

export const COSMIC_FORTUNE_CATALOG: SlotGameCatalogDefinition = {
  id: 'cosmic-fortune',
  title: 'Cosmic Fortune',
  shortTitle: 'Cosmic Fortune',
  description: 'Launch through the lucky stars, assemble four planetary orbits, and sweep up dark-matter prizes.',
  image: art.emblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: art.backdrop,
}
