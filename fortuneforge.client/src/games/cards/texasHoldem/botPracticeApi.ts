import type { CardSuit } from '../shared/cards'
import {
  getPracticeSession,
  joinPracticeQueue,
  postPracticeCommand,
  practiceSessionId,
  type PracticeBotSkill,
  type PracticeQueue,
  type PracticeSeat,
} from '../shared/practiceBots'

const basePath = '/api/cards/texas-holdem/bot-practice'
const sessionId = practiceSessionId('fortune-forge.holdem-bot-practice-session')

export type HoldemPracticeCard = Readonly<{
  rank: string | null
  suit: CardSuit | null
  hidden: boolean
}>

export type HoldemPracticeSeat = Readonly<{
  player: PracticeSeat
  holeCards: readonly HoldemPracticeCard[]
  stack: number
  committed: number
  status: string
  handName: string | null
  payout: number
}>

export type HoldemPracticeEvent = Readonly<{
  version: number
  type: string
  actorSeatId: string
  actorDisplayName: string
  occurredAtUtc: string
  publicData: Readonly<Record<string, string>>
}>

export type HoldemPracticeTable = Readonly<{
  matchId: string
  status: 'active' | 'completed'
  street: string
  version: number
  dealerSeat: number
  activeSeat: number
  pot: number
  currentBet: number
  minimumRaiseTo: number
  communityCards: readonly HoldemPracticeCard[]
  seats: readonly HoldemPracticeSeat[]
  events: readonly HoldemPracticeEvent[]
  legalActions: readonly ('fold' | 'check' | 'call' | 'raise')[]
  startedAtUtc: string
  updatedAtUtc: string
}>

export type HoldemBotPracticeResponse = Readonly<{
  contractVersion: 'cards.bot.v2'
  kind: 'queue' | 'match'
  queue: PracticeQueue | null
  table: HoldemPracticeTable | null
}>

export function getHoldemBotPractice(signal?: AbortSignal) {
  return getPracticeSession<HoldemBotPracticeResponse>(basePath, sessionId, signal)
}

export function joinHoldemBotPractice(
  playerCount: number,
  skill: PracticeBotSkill,
  idempotencyKey: string,
) {
  return joinPracticeQueue<HoldemBotPracticeResponse>(
    basePath, sessionId, playerCount, skill, idempotencyKey,
  )
}

export function commandHoldemBotPractice(
  matchId: string,
  expectedVersion: number,
  action: 'fold' | 'check' | 'call' | 'raise',
  idempotencyKey: string,
  raiseTo?: number,
) {
  return postPracticeCommand<HoldemBotPracticeResponse>(
    basePath,
    sessionId,
    matchId,
    {
      type: action,
      expectedVersion,
      arguments: raiseTo === undefined ? undefined : { raiseTo: String(raiseTo) },
    },
    idempotencyKey,
  )
}
