import type { SlotExperienceSet } from '../config/slotExperienceSets'

export type SlotGameCatalogDefinition = {
  id: string
  title: string
  shortTitle: string
  description: string
  image: string
  imagePresentation: 'contain' | 'cover'
  slotDivBackgroundImage?: string
}

export type SlotGameManifest = {
  id: string
  routes: {
    play: string | null
    demo: string
  }
  catalog: SlotGameCatalogDefinition
  experience: SlotExperienceSet
}

// Keep this helper deliberately small: manifests remain ordinary typed data
// that artists can copy, review, and preview without learning a framework.
export function defineSlotGame(manifest: SlotGameManifest): SlotGameManifest {
  return manifest
}

export function createSlotExperienceRouteMap(
  manifests: readonly SlotGameManifest[],
): Readonly<Record<string, SlotExperienceSet>> {
  const routes: Record<string, SlotExperienceSet> = {}
  for (const manifest of manifests) {
    for (const route of [manifest.routes.play, manifest.routes.demo]) {
      if (route === null) continue
      if (routes[route]) {
        throw new Error(`Duplicate slot route '${route}'.`)
      }
      routes[route] = manifest.experience
    }
  }
  return routes
}
