import type { StandardCardSuit } from '@fortuneforge/client-presentation'

export const contractVersion = 'cards.three-card-poker.table.v1'

export type ThreeCardPokerAction = 'play' | 'fold'
export type ThreeCardPokerStatus = Readonly<{
  available: boolean
  minimumWager: number
  maximumWager: number
  wagerIncrement: number
  minimumStartOccupancy: number
  tableCapacity: number
  humanGraceSeconds: number
  actionDeadlineSeconds: number
  dealerRule: string
  pairPlusAllowed: boolean
  anteBonusPaytable: string
  pairPlusPaytable: string
  pairPlusPaysOnFold: boolean
}>
export type ThreeCardPokerCard = Readonly<{ rank?: string | null; suit?: StandardCardSuit | null; hidden: boolean }>
export type ThreeCardPokerHand = Readonly<{ cards: readonly ThreeCardPokerCard[]; rank: string | null }>
export type ThreeCardPokerSeat = Readonly<{
  seatId: string
  displayName: string
  seat: number
  status: string
  ante: number
  pairPlus: number
  playWager: number
  totalWager: number
  payout: number
  anteBonusPayout: number
  pairPlusPayout: number
  outcome?: string | null
  lastAction?: string | null
  hand: ThreeCardPokerHand
  isCurrentPlayer: boolean
}>
export type ThreeCardPokerTable = Readonly<{
  tableId: string
  phase: string
  round: number
  dealer: ThreeCardPokerHand
  seats: readonly ThreeCardPokerSeat[]
  activeSeat: number | null
  legalActions: readonly ThreeCardPokerAction[]
  createdAtUtc: string
  updatedAtUtc: string
  actionDeadlineAtUtc: string | null
  wagerDeadlineAtUtc: string | null
  transition: string | null
  nextTransitionAtUtc: string | null
  remainingActionMilliseconds: number
  remainingWagerMilliseconds: number
  remainingTransitionMilliseconds: number
}>
type SessionBase = Readonly<{ contractVersion: typeof contractVersion; version: number }>
export type ThreeCardPokerQueueSession = SessionBase & Readonly<{
  kind: 'queue'; ticketId: string; position: number; joinedAtUtc: string
  humanGraceEndsAtUtc: string; players: readonly ThreeCardPokerSeat[]
}>
export type ThreeCardPokerPlaySession = SessionBase & Readonly<{ kind: 'table'; table: ThreeCardPokerTable }>
export type ThreeCardPokerSession = (SessionBase & Readonly<{ kind: 'idle' }>) | ThreeCardPokerQueueSession | ThreeCardPokerPlaySession
export type ThreeCardPokerMutationResponse = Readonly<{ session: ThreeCardPokerSession; balanceCredits: number }>
export type PendingThreeCardPokerMutation = Readonly<{ fingerprint: string; idempotencyKey: string }>

export function validThreeCardPokerWager(value: number, status: ThreeCardPokerStatus, optional = false): boolean {
  if (optional && value === 0) return true
  if (!Number.isFinite(value) || value < status.minimumWager || value > status.maximumWager) return false
  const increments = (value - status.minimumWager) / status.wagerIncrement
  return Math.abs(increments - Math.round(increments)) < .000001
}
