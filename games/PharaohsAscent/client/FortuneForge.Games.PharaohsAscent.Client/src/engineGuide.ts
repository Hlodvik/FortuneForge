import type { SlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import document from '../../../guide-contract.json?raw'

/** Game-owned presentation snapshot of the authoritative engine contract. */
export const PHARAOH_ENGINE_GUIDE = Object.freeze(JSON.parse(document) as SlotEngineGuideContract)
