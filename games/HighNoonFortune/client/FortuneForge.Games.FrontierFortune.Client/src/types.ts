import type { ReactNode } from 'react'

export type FrontierAccount = { balances: { slotsCredits: number } }
export type FrontierFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
export type FrontierFortuneGameProps = {
  account?: FrontierAccount
  authenticatedFetch: FrontierFetch
  renderShell: (props: { balance: number; onNavigate: () => void; children: ReactNode }) => ReactNode
  paymentAlerts?: ReactNode
  onSpinStateChange?: (busy: boolean) => void
  onAccountChange?: (balanceCents: number) => void
}
export type HandSymbol = 'J' | 'Q' | 'K' | 'A' | 'WILD'
export type HandCard = { symbol: HandSymbol | null; held: boolean }
export type HandCategory = 'pair' | 'two-pair' | 'trips' | 'full-house' | 'four' | 'five' | 'grand'
export type FrontierHand = {
  roundId: string; wagerPoints: number; dealsRemaining: number; phase: 'choose' | 'hold' | 'complete'
  hand: HandCard[]; awardPoints: number; category: HandCategory | null
}
export type FrontierAnte = { chips: number; requiredChips: number; wagerPointsTotal: number; fundedWagerPoints: number; ready: boolean }
export type FrontierCommand = {
  operationId: string; kind: 'spin' | 'deal-in' | 'deal' | 'confirm'
  wagerPoints?: number | null; roundId?: string | null; expectedRevision?: number | null
  held?: boolean[] | null; selectedRows?: (number | null)[] | null
}
export type FrontierWin = { line: number; symbol: string; count: number; awardPoints: number; cells: { column: number; row: number }[] }
export type FrontierState = {
  gameId: 'high-noon-fortune-v1'; engineVersion: 'frontier-fortune-engine-v2'
  balanceCents: number; revision: number; reels: string[][]; lastWinPoints: number; wins: FrontierWin[]
  ante: FrontierAnte; handRound: FrontierHand | null; pendingOperation: FrontierCommand | null
  wagering: { pointValueInCents: number; minWagerPoints: number; maxWagerPoints: number; stepPoints: number }
}
