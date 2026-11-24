import { CROOKED_ROAD_DEFINITION } from './definition'

export const CROOKED_ROAD_CATALOG = {
  id: CROOKED_ROAD_DEFINITION.id,
  title: CROOKED_ROAD_DEFINITION.title,
  shortTitle: CROOKED_ROAD_DEFINITION.title,
  description: CROOKED_ROAD_DEFINITION.description,
  image: CROOKED_ROAD_DEFINITION.symbolSpecs['7'][2],
  imagePresentation: 'contain' as const,
  slotDivBackgroundImage: CROOKED_ROAD_DEFINITION.artwork.backdrop,
}
