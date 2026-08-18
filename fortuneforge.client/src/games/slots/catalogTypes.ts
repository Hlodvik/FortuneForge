import type { SlotGameCatalogDefinition } from './shared/slotGameManifest'

export type SlotGameCatalogEntry = SlotGameCatalogDefinition & {
  playHref: string | null
  demoHref: string
  serverGameIds: readonly string[]
}
