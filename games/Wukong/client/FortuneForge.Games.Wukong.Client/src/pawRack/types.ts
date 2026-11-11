import type { SlotSymbolId } from '@fortuneforge/slot-client-core'

export type WukongPawGrant = Readonly<{ id: string; wagerPoints: number }>
export type WukongPawPlacement = Readonly<{
  id: string
  grantId: string
  column: number
  wagerPoints: number
}>
export type WukongPawPreviewPhase = 'ready' | 'placed' | 'spinning' | 'settled' | 'blocked'
export type WukongPawPreviewSymbol = SlotSymbolId | 'PAW_EARN_SCROLL'
export type WukongPawPreviewResult = Readonly<{
  spinId: string
  pawCount: number
  moneyGrabPoints: number
  totalPoints: number
}>
export type WukongPawEarnRole = Readonly<{
  symbolId: 'PAW_EARN_SCROLL'
  threshold: null
  rate: null
  wagerBinding: null
}>

/** Prototype transport only. No account balance, purchase or production grant writes. */
export type WukongPawPrototypeState = Readonly<{
  grants: readonly WukongPawGrant[]
  wagerPoints: number
  wagerOptions: readonly number[]
  pointValueInCents: number
  placement: WukongPawPlacement | null
  phase: WukongPawPreviewPhase
  reels: readonly (readonly WukongPawPreviewSymbol[])[]
  lastResult: WukongPawPreviewResult | null
  observedEarnScrolls: number
  earnRole: WukongPawEarnRole
  fixtureEarnSymbolInjection: boolean
}>

/** All outcome, banking and admission changes belong to the preview host. */
export type WukongPawRackPreviewProps = WukongPawPrototypeState & Readonly<{
  pending: boolean
  error: string | null
  onPlace: (grantId: string, column: number) => void
  onClear: () => void
  onWager: (wagerPoints: number) => void
  onSpin: () => void
  onNextPreview: () => void
  onResetFixture: () => void
  onFixtureEarnSymbolChange: (enabled: boolean) => void
}>
