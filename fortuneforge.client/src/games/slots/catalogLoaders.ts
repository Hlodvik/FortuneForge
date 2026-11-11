import { SLOT_GAME_CATALOG, type SlotGameCatalogEntry } from './catalog'

export async function loadSlotGameCatalogById(id: string): Promise<SlotGameCatalogEntry | null> {
  return SLOT_GAME_CATALOG.find((game) => game.id === id) ?? null
}
