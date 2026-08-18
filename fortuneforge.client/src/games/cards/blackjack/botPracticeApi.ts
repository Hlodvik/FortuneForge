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

const basePath = '/api/cards/blackjack/bot-practice'
const sessionId = practiceSessionId('fortune-forge.blackjack-bot-practice-session')

export type BlackjackPracticeCard = Readonly<{
  rank: string | null
  suit: CardSuit | null
  hidden: boolean
}>

export type BlackjackPracticeHand = Readonly<{
  cards: readonly BlackjackPracticeCard[]
  score: number | null
  soft: boolean
  blackjack: boolean
  bust: boolean
}>

export type BlackjackPracticeSeat = Readonly<{
  player: PracticeSeat
  hand: BlackjackPracticeHand
  outcome: string | null
  virtualWagerUnits: number
}>

export type BlackjackPracticeEvent = Readonly<{
  version: number
  type: string
  actorSeatId: string
  actorDisplayName: string
  occurredAtUtc: string
  publicData: Readonly<Record<string, string>>
}>

export type BlackjackPracticeTable = Readonly<{
  matchId: string
  status: 'active' | 'completed'
  version: number
  activeSeat: number
  dealer: BlackjackPracticeHand
  seats: readonly BlackjackPracticeSeat[]
  events: readonly BlackjackPracticeEvent[]
  legalActions: readonly ('hit' | 'stand' | 'double')[]
  startedAtUtc: string
  updatedAtUtc: string
}>

export type BlackjackBotPracticeResponse = Readonly<{
  contractVersion: 'cards.bot.v2'
  kind: 'queue' | 'match'
  queue: PracticeQueue | null
  table: BlackjackPracticeTable | null
}>

export function getBlackjackBotPractice(signal?: AbortSignal) {
  return getPracticeSession<BlackjackBotPracticeResponse>(basePath, sessionId, signal)
}

export function joinBlackjackBotPractice(
  playerCount: number,
  skill: PracticeBotSkill,
  idempotencyKey: string,
) {
  return joinPracticeQueue<BlackjackBotPracticeResponse>(
    basePath, sessionId, playerCount, skill, idempotencyKey,
  )
}

export function commandBlackjackBotPractice(
  matchId: string,
  expectedVersion: number,
  action: 'hit' | 'stand' | 'double',
  idempotencyKey: string,
) {
  return postPracticeCommand<BlackjackBotPracticeResponse>(
    basePath,
    sessionId,
    matchId,
    { type: action, expectedVersion },
    idempotencyKey,
  )
}
