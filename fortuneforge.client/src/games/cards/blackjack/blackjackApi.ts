import type { CardRank, CardSuit, PlayingCardModel } from '../shared/cards'

let volatileDemoSessionId: string | null = null

export type BlackjackAction = 'hit' | 'stand' | 'double'

export type BlackjackStatus = {
  available: boolean
  minimumWager: number
  maximumWager: number
  wagerIncrement: number
  dealerRule: string
  blackjackPayout: string
  doubleAllowed: boolean
  splitAllowed: boolean
  insuranceAllowed: boolean
}

export type BlackjackCard = {
  rank: string | null
  suit: CardSuit | null
  hidden: boolean
}

export type BlackjackHand = {
  cards: BlackjackCard[]
  score: number | null
  soft: boolean
  blackjack: boolean
  bust: boolean
}

export type BlackjackGame = {
  gameId: string
  status: 'active' | 'completed'
  outcome: string | null
  message: string
  wager: number
  totalWager: number
  payout: number
  balance: number | null
  player: BlackjackHand
  dealer: BlackjackHand
  canHit: boolean
  canStand: boolean
  canDouble: boolean
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

type BlackjackProblem = {
  error?: string
  detail?: string
  code?: string
  available?: number
  required?: number
}

export class BlackjackRequestError extends Error {
  readonly status: number
  readonly code?: string
  readonly available?: number
  readonly required?: number

  constructor(message: string, status: number, problem: BlackjackProblem | null) {
    super(message)
    this.name = 'BlackjackRequestError'
    this.status = status
    this.code = problem?.code
    this.available = problem?.available
    this.required = problem?.required
  }
}

export async function requestBlackjackStatus(
  _demoMode: boolean,
  signal?: AbortSignal,
): Promise<BlackjackStatus> {
  const response = await fetch('/api/cards/blackjack/demo/status', {
    method: 'GET',
    credentials: 'omit',
    cache: 'no-store',
    signal,
  })
  return readResponse(response, isBlackjackStatus, 'Blackjack status')
}

export async function startBlackjackGame(
  wager: number,
  idempotencyKey: string,
  demoMode: boolean,
): Promise<BlackjackGame> {
  return gameRequest(
    '/games',
    {
      method: 'POST',
      headers: requestHeaders(idempotencyKey),
      body: JSON.stringify({ wager }),
    },
    demoMode,
  )
}

export async function getBlackjackGame(
  gameId: string,
  demoMode: boolean,
): Promise<BlackjackGame> {
  return gameRequest(
    `/games/${encodeURIComponent(gameId)}`,
    { method: 'GET', headers: requestHeaders(null), cache: 'no-store' },
    demoMode,
  )
}

export async function actOnBlackjackGame(
  game: BlackjackGame,
  action: BlackjackAction,
  idempotencyKey: string,
  demoMode: boolean,
): Promise<BlackjackGame> {
  return gameRequest(
    `/games/${encodeURIComponent(game.gameId)}/actions`,
    {
      method: 'POST',
      headers: requestHeaders(idempotencyKey),
      body: JSON.stringify({ action, expectedVersion: game.version }),
    },
    demoMode,
  )
}

export function toPlayingCard(card: BlackjackCard, index: number): PlayingCardModel {
  return {
    id: `${card.suit ?? 'hidden'}-${card.rank ?? 'hidden'}-${index}`,
    suit: card.suit ?? 'spades',
    rank: rankValue(card.rank),
  }
}

export function createBlackjackRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `blackjack_${Date.now()}_${Math.random().toString(36).slice(2, 14)}`
}

function gameRequest(
  suffix: string,
  init: RequestInit,
  demoMode: boolean,
): Promise<BlackjackGame> {
  const path = `/api/cards/blackjack/demo${suffix}`
  void demoMode
  return fetch(path, { ...init, credentials: 'omit' })
    .then((response) => readResponse(response, isBlackjackGame, 'Blackjack game'))
}

async function readResponse<T>(
  response: Response,
  validator: (value: unknown) => value is T,
  label: string,
): Promise<T> {
  const value = await response.json().catch(() => null) as unknown
  if (!response.ok) {
    const problem = isRecord(value) ? value as BlackjackProblem : null
    throw new BlackjackRequestError(
      problem?.error ?? problem?.detail ?? `${label} request failed (${response.status}).`,
      response.status,
      problem,
    )
  }
  if (!validator(value)) {
    throw new Error(`The server returned an invalid ${label.toLowerCase()} response.`)
  }
  return value
}

function requestHeaders(idempotencyKey: string | null): Headers {
  const headers = new Headers({ 'Content-Type': 'application/json' })
  if (idempotencyKey !== null) headers.set('Idempotency-Key', idempotencyKey)
  headers.set('X-Demo-Session-Id', demoSessionId())
  return headers
}

function demoSessionId(): string {
  const storageKey = 'fortune-forge.blackjack-demo-session'
  try {
    const existing = window.localStorage.getItem(storageKey)
    if (existing && existing.length >= 16) return existing
    const created = createBlackjackRequestId()
    window.localStorage.setItem(storageKey, created)
    return created
  } catch {
    volatileDemoSessionId ??= createBlackjackRequestId()
    return volatileDemoSessionId
  }
}

function isBlackjackStatus(value: unknown): value is BlackjackStatus {
  return isRecord(value)
    && typeof value.available === 'boolean'
    && isPositiveNumber(value.minimumWager)
    && isPositiveNumber(value.maximumWager)
    && isPositiveNumber(value.wagerIncrement)
    && typeof value.dealerRule === 'string'
    && typeof value.blackjackPayout === 'string'
    && typeof value.doubleAllowed === 'boolean'
    && typeof value.splitAllowed === 'boolean'
    && typeof value.insuranceAllowed === 'boolean'
}

function isBlackjackGame(value: unknown): value is BlackjackGame {
  return isRecord(value)
    && typeof value.gameId === 'string'
    && (value.status === 'active' || value.status === 'completed')
    && (value.outcome === null || typeof value.outcome === 'string')
    && typeof value.message === 'string'
    && isNonNegativeNumber(value.wager)
    && isNonNegativeNumber(value.totalWager)
    && isNonNegativeNumber(value.payout)
    && (value.balance === null || isNonNegativeNumber(value.balance))
    && isBlackjackHand(value.player)
    && isBlackjackHand(value.dealer)
    && typeof value.canHit === 'boolean'
    && typeof value.canStand === 'boolean'
    && typeof value.canDouble === 'boolean'
    && Number.isInteger(value.version)
    && typeof value.createdAtUtc === 'string'
    && typeof value.updatedAtUtc === 'string'
}

function isBlackjackHand(value: unknown): value is BlackjackHand {
  return isRecord(value)
    && Array.isArray(value.cards)
    && value.cards.every(isBlackjackCard)
    && (value.score === null || Number.isInteger(value.score))
    && typeof value.soft === 'boolean'
    && typeof value.blackjack === 'boolean'
    && typeof value.bust === 'boolean'
}

function isBlackjackCard(value: unknown): value is BlackjackCard {
  return isRecord(value)
    && (value.rank === null || typeof value.rank === 'string')
    && (value.suit === null || isCardSuit(value.suit))
    && typeof value.hidden === 'boolean'
}

function isCardSuit(value: unknown): value is CardSuit {
  return value === 'clubs' || value === 'diamonds' || value === 'hearts' || value === 'spades'
}

function rankValue(rank: string | null): CardRank {
  if (rank === 'A') return 1
  if (rank === 'J') return 11
  if (rank === 'Q') return 12
  if (rank === 'K') return 13
  const parsed = Number(rank)
  return Number.isInteger(parsed) && parsed >= 2 && parsed <= 10
    ? parsed as CardRank
    : 1
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function isNonNegativeNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}
