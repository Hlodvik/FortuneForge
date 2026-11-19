// Edited: 2026-11-18T16:27:28-06:00 | Computer: Vinnabok
import type { SlotGameCatalogDefinition } from '@fortuneforge/slot-game-runtime/manifest'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_CATALOG: SlotGameCatalogDefinition = {
  id: 'arcane-archives',
  title: 'Arcane Archives',
  shortTitle: 'Arcane Archives',
  description: 'Match golden glyphs in a moonlit library filled with constellations and alchemical ingredients.',
  image: art.owl,
  imagePresentation: 'contain',
  slotDivBackgroundImage: art.backdrop,
}
