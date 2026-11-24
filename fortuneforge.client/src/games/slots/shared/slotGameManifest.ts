// Edited: 2026-11-18T22:54:10-06:00 | Computer: Vinnabok
import type { SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'

import type { SlotGameCatalogDefinition } from '../catalogTypes'
export type { SlotGameCatalogDefinition } from '../catalogTypes'

export type SlotGameManifest = {
  id: string
  routes: {
    play: string | null
    demo: string | null
  }
  catalog: SlotGameCatalogDefinition
  experience: SlotExperienceSet
}

export type PackagedSlotGameManifestEnvelope = Omit<SlotGameManifest, 'experience'> & {
  experience: unknown
}

/**
 * Package clients and the legacy host currently publish structurally identical
 * experience contracts from separate declaration roots. Keep that transitional
 * type assertion at this single boundary while still checking the manifest ID,
 * routes and catalogue envelope.
 */
export function adoptPackagedSlotGameManifest(
  manifest: PackagedSlotGameManifestEnvelope,
): SlotGameManifest {
  return manifest as SlotGameManifest
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
