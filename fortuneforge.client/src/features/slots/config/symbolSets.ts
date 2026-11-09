import type { SlotSymbolId } from '../types/slots'
import type {
  SlotSymbolDefinition as SharedSlotSymbolDefinition,
  SlotSymbolGuideEntry as SharedSlotSymbolGuideEntry,
  SlotSymbolSet as SharedSlotSymbolSet,
} from '@fortuneforge/slot-client-core/config/symbolSets'

export const CLASSIC_SLOT_SYMBOL_SET_ID = 'classic-symbols-v3'

export { getSlotSymbolImageSrcSet, getSlotAnimationSymbolIds,
  getSlotSymbolDefinition } from '@fortuneforge/slot-client-core/config/symbolSets'
export type { SlotResponsiveImages } from '@fortuneforge/slot-client-core/config/symbolSets'
export type SlotSymbolDefinition = SharedSlotSymbolDefinition<SlotSymbolId>
export type SlotSymbolGuideEntry = SharedSlotSymbolGuideEntry<SlotSymbolId>
export type SlotSymbolSet = SharedSlotSymbolSet<SlotSymbolId>
