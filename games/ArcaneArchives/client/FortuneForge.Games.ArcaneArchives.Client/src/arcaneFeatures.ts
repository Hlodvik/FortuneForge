import type { RefObject } from 'react'
import type { GridPosition, SlotPotionSoundEvent, SlotSymbolId } from '@fortuneforge/slot-client-core'

export const arcaneWandPaylineId = 903

export type ArcaneWandCollection = {
  wand: GridPosition
  cells: (GridPosition & { symbolId: SlotSymbolId; multiplier: number; amountPoints: number; potionBonusPoints?: number })[]
  totalPoints: number
}

export type SlotTomeState = {
  selectedPage: string
  activePage: string | null
  boostedSpinsRemaining: number
  grimoireReady: boolean
  ingredients: Readonly<Record<string, number>>
  pages: readonly Readonly<{ id: string; label: string; ingredients: readonly string[] }>[]
}

export type SlotPotionPreview = Readonly<{
  ingredientId: SlotSymbolId
  name: string
  effect: string
  potency: number
}>

export type SlotPotionBottle = SlotPotionPreview & Readonly<{
  id: number
  lockedWagerPoints?: number
  variantId?: string
}>

export type SlotPotionState = Readonly<{
  capacity: number
  majority: number
  pot: readonly SlotSymbolId[]
  bottles: readonly SlotPotionBottle[]
  active: Readonly<{ bottle: SlotPotionBottle; spinsRemaining: number }> | null
  preview: SlotPotionPreview | null
}>

export type SlotTomePanelProps = {
  state: SlotTomeState
  potions: SlotPotionState | null
  awarded: Readonly<Record<string, number>>
  cabinetRef: RefObject<HTMLDivElement | null>
  settledSpin: Readonly<{ key: number; reels: readonly (readonly SlotSymbolId[])[] }> | null
  reducedMotion: boolean
  disabled: boolean
  error: string | null
  pointValueInCents: number
  onOpen: () => void
  onChangePage: (page: string, activate: boolean) => void
  onUsePotion: (id: number) => void
  confirmedPotionUse?: number | null
  onPotionCue?: (event: SlotPotionSoundEvent) => void
}

export type SlotPotionWagerReadoutProps = Readonly<{
  wager: number
  selectedWager: number
  lockedPotionId: number | null
  confirmedPotionUse: number | null
  reducedMotion: boolean
}>
