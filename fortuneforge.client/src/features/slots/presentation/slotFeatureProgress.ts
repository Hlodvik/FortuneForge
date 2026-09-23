import type { SlotCollectionFeature } from '../config/slotFeatures'
import type { SlotSealCollection } from '../types/slots'

export type SlotFeatureProgress = {
  current: number
  label: string
  percent: number
  rewardDescription: string | null
  target: number
}

export function getSlotFeatureProgress(
  collections: SlotCollectionFeature | undefined,
  collectionStates: readonly SlotSealCollection[],
): SlotFeatureProgress | null {
  if (!collections || collections.entries.length === 0) return null

  const candidates = collections.entries.map((definition) => {
    const state = collectionStates.find((candidate) => candidate.sealId === definition.id)
    const target = Math.max(1, state?.requiredCount ?? definition.requiredCount)
    const current = Math.min(target, Math.max(0, state?.count ?? 0))
    return {
      current,
      label: definition.label,
      percent: Math.round(current / target * 100),
      rewardDescription: definition.rewardDescription ?? null,
      target,
    }
  })

  return candidates.reduce((leading, candidate) => {
    if (candidate.percent !== leading.percent) {
      return candidate.percent > leading.percent ? candidate : leading
    }
    return candidate.current > leading.current ? candidate : leading
  })
}
