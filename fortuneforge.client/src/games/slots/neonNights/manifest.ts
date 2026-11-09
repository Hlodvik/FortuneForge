import { NEON_NIGHTS_SLOT_GAME as neon } from '@fortuneforge/games-neon-nights/manifest'
import type { SlotGameManifest } from '../shared/slotGameManifest'

// Compatibility for registry/contract consumers. Runtime page dispatch uses the game package.
export const NEON_NIGHTS_SLOT_GAME = neon as unknown as SlotGameManifest
