import {
  getPracticeSession,
  joinPracticeQueue,
  postPracticeCommand,
  practiceSessionId,
  type PracticeBotSkill,
  type PracticeQueue,
  type PracticeSeat,
} from '../shared/practiceBots'
import type { SolitaireCommand, SolitaireGame } from './solitaireTypes'

const basePath = '/api/cards/solitaire/bot-practice'
const sessionId = practiceSessionId('fortune-forge.solitaire-bot-practice-session')

export type SolitaireBotPracticeMatch = Readonly<{
  matchId: string
  version: number
  startedAtUtc: string
  deadlineAtUtc: string
  remainingMilliseconds: number
  game: SolitaireGame
  seats: readonly PracticeSeat[]
}>

export type SolitaireBotPracticeStanding = Readonly<{
  rank: number
  player: PracticeSeat
  score: number
  moves: number
  status: string
}>

export type SolitaireBotPracticeResult = Readonly<{
  matchId: string
  startedAtUtc: string
  completedAtUtc: string
  standings: readonly SolitaireBotPracticeStanding[]
}>

export type SolitaireBotPracticeResponse = Readonly<{
  contractVersion: 'cards.bot.v2'
  kind: 'queued' | 'match' | 'result'
  queue: PracticeQueue | null
  match: SolitaireBotPracticeMatch | null
  result: SolitaireBotPracticeResult | null
}>

export function getSolitaireBotPractice(signal?: AbortSignal) {
  return getPracticeSession<SolitaireBotPracticeResponse>(basePath, sessionId, signal)
}

export function joinSolitaireBotPractice(
  playerCount: number,
  skill: PracticeBotSkill,
  idempotencyKey: string,
) {
  return joinPracticeQueue<SolitaireBotPracticeResponse>(
    basePath, sessionId, playerCount, skill, idempotencyKey,
  )
}

export function commandSolitaireBotPractice(
  matchId: string,
  expectedVersion: number,
  command: SolitaireCommand,
  idempotencyKey: string,
) {
  return postPracticeCommand<SolitaireBotPracticeResponse>(
    basePath,
    sessionId,
    matchId,
    { type: command.type, expectedVersion, arguments: commandArguments(command) },
    idempotencyKey,
  )
}


function commandArguments(command: SolitaireCommand): Readonly<Record<string, string>> | undefined {
  if (command.type === 'draw') return undefined
  if (command.type === 'flip') return { column: String(command.column) }
  if (command.type !== 'move') return undefined
  return {
    fromZone: command.from.zone,
    fromIndex: String(command.from.index),
    startIndex: String(command.startIndex),
    toZone: command.to.zone,
    toIndex: String(command.to.index),
  }
}
