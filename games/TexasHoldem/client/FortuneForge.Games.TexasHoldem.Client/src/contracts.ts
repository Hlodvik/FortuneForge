/** Presentation contracts mirror the existing credit.v2 transport; no wallet or engine lives here. */
export type HoldemAction = 'fold' | 'check' | 'call' | 'raise'
export type HoldemSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades'
export type HoldemCard = Readonly<{ rank?: string | null; suit?: HoldemSuit | null; hidden: boolean }>
export type HoldemTableRule = Readonly<{
  id: string
  name: string
  description: string
  /** Rule stakes and account balances use decimal rand credits, unlike table chip fields. */
  smallBlindCredits: number
  bigBlindCredits: number
  anteCredits: number
  maximumTableStackCredits: number
  minimumBuyInCredits?: number
}>
export type HoldemSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  /** Integer cents, not decimal account credits. */
  startingStack: number
  stack: number
  committed: number
  committedRound: number
  status: string
  lastAction?: string | null
  holeCards: readonly HoldemCard[]
  handName?: string | null
  isCurrentPlayer: boolean
}>
export type HoldemActionEntry = Readonly<{
  sequence: number
  handNumber: number
  street: string
  seatId: string
  displayName: string
  action: string
  amount: number
}>
export type HoldemTable = Readonly<{
  matchId: string
  status: string
  street: string
  handNumber: number
  dealerSeat: number
  activeSeat: number
  /** Pot, bets, raises and awards are integer cents. */
  pot: number
  currentBet: number
  minimumRaiseTo: number
  maximumRaiseTo: number
  shortAllInRaiseTo?: number | null
  communityCards: readonly HoldemCard[]
  seats: readonly HoldemSeat[]
  legalActions: readonly HoldemAction[]
  winningSeatIds: readonly string[]
  winningAmount: number
  startedAtUtc: string
  matchDeadlineAtUtc: string
  actionDeadlineAtUtc?: string | null
  remainingActionMilliseconds: number
  tableRule?: HoldemTableRule | null
  actionLog?: readonly HoldemActionEntry[] | null
}>
