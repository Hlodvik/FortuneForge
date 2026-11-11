import { GOLD_FOUNDRY_SLOT_GAME as goldFoundry } from '@fortuneforge/games-gold-foundry/manifest'
import type { SlotGameManifest } from '../shared/slotGameManifest'

// Compatibility for registry/contract consumers. Runtime page dispatch uses the game package.
export const GOLD_FOUNDRY_SLOT_GAME = goldFoundry as unknown as SlotGameManifest
