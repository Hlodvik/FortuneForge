import type { SlotSymbolId } from '../types/slots'
import type { SpinAnimation as SharedSpinAnimation } from '@fortuneforge/slot-client-core/animation/slotAnimation'
export type SpinAnimation = SharedSpinAnimation<SlotSymbolId>
export type { ReelMotionState } from '@fortuneforge/slot-client-core/animation/slotAnimation'
export { startSpinAnimation, prepareSpinForStopping, waitForReelStopOffset, requestQuickStop,
  stopSpinAnimationWithCadence, stopReelAnimation, finishSpinAnimation, cancelSpinAnimation,
  reelLandingSettleDurationMs } from '@fortuneforge/slot-client-core/animation/slotAnimation'
