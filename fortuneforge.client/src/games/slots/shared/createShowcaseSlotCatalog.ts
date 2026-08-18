import type { ShowcaseSlotGameDefinition } from './createShowcaseSlotGame'
import type { SlotGameCatalogDefinition } from './slotGameManifest'
import { createSlotBackdropSvg, createSlotIconSvg } from './themedSvg'

export function createShowcaseSlotCatalog(
  definition: ShowcaseSlotGameDefinition,
): SlotGameCatalogDefinition {
  return {
    id: definition.id,
    title: definition.title,
    shortTitle: definition.title,
    description: definition.description,
    image: createSlotIconSvg({
      label: `${definition.title} emblem`,
      glyph: definition.motif,
      background: definition.colors.primary,
      backgroundDeep: definition.colors.deep,
      rim: definition.colors.rim,
      glow: definition.colors.glow,
    }),
    imagePresentation: 'contain',
    slotDivBackgroundImage: createSlotBackdropSvg({
      label: `${definition.title} themed landscape`,
      motif: definition.motif,
      skyTop: definition.colors.skyTop,
      skyBottom: definition.colors.skyBottom,
      horizon: definition.colors.horizon,
      accent: definition.colors.glow,
      ground: definition.colors.ground,
    }),
  }
}
