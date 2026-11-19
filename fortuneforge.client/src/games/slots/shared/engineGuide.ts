// Edited: 2026-11-18T19:29:04-06:00 | Computer: Vinnabok
import { withEngineGuide as applyEngineGuide } from '@fortuneforge/slot-game-runtime/engine-guide'
import type { SlotExperienceSet } from '../../../features/slots/config/slotExperienceSets'

export {
  formatCalibratedMultiplier, getSlotEngineGuideContract, SLOT_ENGINE_GUIDE_CONTRACTS, type SlotEngineGuideContract,
} from '@fortuneforge/slot-game-runtime/engine-guide'

const gameOwnedGuides = new Set(['arcane-archives-v1', 'rainbow-realm-fruits-v1', 'dragon-hoard-v1', 'neon-nights-v1'])

export function withEngineGuide(experience: SlotExperienceSet): SlotExperienceSet {
  const { gameId } = experience.rules
  const pirates = gameId === 'pirates-fortune-v1'
  return applyEngineGuide(experience, {
    preserveExistingGuide: gameOwnedGuides.has(gameId),
    preserveCollectionGuideEntries: pirates,
    preserveCollectionHelpSections: pirates,
    preserveCollectionProgressCopy: pirates || gameId === 'wukong-journey-v1',
  })
}
