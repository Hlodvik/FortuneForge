import { MIDNIGHT_VAULT_SLOT_GAME } from '@fortuneforge/games-midnight-vault/manifest'
import type { SlotGameManifest } from '../shared/slotGameManifest'

/** Route-registry compatibility only. Gameplay is loaded by MidnightVaultHost. */
export const DRAGON_HOARD_SLOT_GAME = MIDNIGHT_VAULT_SLOT_GAME as unknown as SlotGameManifest
