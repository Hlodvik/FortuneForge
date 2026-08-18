import type { SlotGameCatalogDefinition } from '../shared/slotGameManifest'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_CATALOG: SlotGameCatalogDefinition = {
  id: 'arcane-archives',
  title: 'Arcane Archives',
  shortTitle: 'Arcane Archives',
  description: 'Open the forbidden stacks, build four rune shelves, and let an enchanted satchel gather raw mana.',
  image: art.emblem,
  imagePresentation: 'contain',
  slotDivBackgroundImage: art.backdrop,
}
