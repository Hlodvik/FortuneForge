import type { ShowcaseSlotGameDefinition } from './createShowcaseSlotGame'
import type { SlotGameCatalogDefinition } from './slotGameManifest'

export function createShowcaseSlotCatalog(
  definition: ShowcaseSlotGameDefinition,
): SlotGameCatalogDefinition {
  const [, , reelSymbolImage] = definition.symbolSpecs['7']

  return {
    id: definition.id,
    title: definition.title,
    shortTitle: definition.title,
    description: definition.description,
    image: reelSymbolImage,
    imagePresentation: 'contain',
    slotDivBackgroundImage: definition.artwork.backdrop,
  }
}
