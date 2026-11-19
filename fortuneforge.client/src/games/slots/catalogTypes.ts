// Edited: 2026-11-18T22:54:10-06:00 | Computer: Vinnabok
export type SlotGameCatalogDefinition = {
  id: string
  title: string
  shortTitle: string
  description: string
  image: string
  imagePresentation: 'contain' | 'cover'
  /** Keeps edge-to-edge emblems clear of the catalogue-card frame. */
  imageScale?: 'standard' | 'compact'
  slotDivBackgroundImage?: string
}

export type SlotGameCatalogEntry = SlotGameCatalogDefinition & {
  playHref: string | null
  demoHref: string
  serverGameIds: readonly string[]
}
