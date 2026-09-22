import type { AsteroidsReplayPayload } from '@fortuneforge/games-asteroids'
import type { FlappyReplayPayload } from '@fortuneforge/games-flappy'

export type ArcadeCompetitionPeriod = 'daily' | 'weekly' | 'all-time'
export type ArcadeCompetitionPaidPeriod = 'daily' | 'weekly'

export type ArcadeCompetitionPlacement = Readonly<{
  position: number
  playerId: string
  score: number
}>

export type ArcadeCompetitionRefund = Readonly<{
  playerId: string
  amountCents: number
}>

export type ArcadeCompetitionWindow = Readonly<{
  gameId: string
  period: 'daily' | 'weekly'
  startsAtUtc: string
  endsAtUtc: string
  entriesOpen: boolean
  isCompleted: boolean
  totalUniquePlayers: number
  leaderboard: readonly ArcadeCompetitionPlacement[]
  solePlayerRefund: ArcadeCompetitionRefund | null
  visibleJackpotCents: number
}>

export type ArcadeCompetitionAllTime = Readonly<{
  gameId: string
  period: 'all-time'
  leaderboard: readonly ArcadeCompetitionPlacement[]
}>

export type ArcadeCompetitionSnapshot = ArcadeCompetitionWindow | ArcadeCompetitionAllTime

export type ArcadeCompetitionGateway = Readonly<{
  getLeaderboard: (
    gameId: string,
    period: ArcadeCompetitionPeriod,
    at?: string,
    signal?: AbortSignal,
  ) => Promise<ArcadeCompetitionSnapshot>
}>

export type AsteroidsPaidAttempt = Readonly<{
  attemptId: string
  gameId: 'asteroids'
  period: ArcadeCompetitionPaidPeriod
  startsAtUtc: string
  endsAtUtc: string
  entryFeeCents: number
  wasReplay: boolean
  runId: string
  seedHex: string
}>

export type AsteroidsReplayCompletion = Readonly<{
  runId: string
  score: number
  terminal: 'completed' | 'gameover'
  wasReplay: boolean
}>

export type AsteroidsFreeRun = Readonly<{
  runId: string
  seedHex: string
  startedAtUtc: string
  wasReplay: boolean
}>

export type AsteroidsPaidCompetitionGateway = Readonly<{
  startAsteroidsAttempt: (period: ArcadeCompetitionPaidPeriod, idempotencyKey: string, signal?: AbortSignal) => Promise<AsteroidsPaidAttempt>
  completeAsteroidsReplay: (period: ArcadeCompetitionPaidPeriod, runId: string, replay: AsteroidsReplayPayload, signal?: AbortSignal) => Promise<AsteroidsReplayCompletion>
}>

export type AsteroidsFreeRunGateway = Readonly<{
  startFreeAsteroidsRun: (idempotencyKey: string, signal?: AbortSignal) => Promise<AsteroidsFreeRun>
  completeFreeAsteroidsReplay: (runId: string, replay: AsteroidsReplayPayload, signal?: AbortSignal) => Promise<AsteroidsReplayCompletion>
}>

export type FlappyFreeRun = Readonly<{
  runId: string
  seed: number
  startedAtUtc: string
  wasReplay: boolean
}>

export type FlappyReplayCompletion = Readonly<{
  runId: string
  score: number
  terminal: 'obstacle-collision' | 'ground-collision' | 'ceiling-collision'
  wasReplay: boolean
}>

export type FlappyFreeRunGateway = Readonly<{
  startFreeFlappyRun: (idempotencyKey: string, signal?: AbortSignal) => Promise<FlappyFreeRun>
  completeFreeFlappyReplay: (runId: string, replay: FlappyReplayPayload, signal?: AbortSignal) => Promise<FlappyReplayCompletion>
}>

type Fetcher = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

type CompetitionProblem = Readonly<{ code?: string }>

export class ArcadeCompetitionRequestError extends Error {
  readonly status: number
  readonly code?: string

  constructor(status: number, code?: string) {
    super('Unable to complete the arcade competition request.')
    this.name = 'ArcadeCompetitionRequestError'
    this.status = status
    this.code = code
  }
}

/** A read-only HTTP boundary. Supply a fake gateway to the component in tests. */
export class HttpArcadeCompetitionGateway implements ArcadeCompetitionGateway, AsteroidsPaidCompetitionGateway, AsteroidsFreeRunGateway, FlappyFreeRunGateway {
  private readonly fetcher: Fetcher

  constructor(fetcher: Fetcher = fetch) {
    this.fetcher = fetcher
  }

  async getLeaderboard(
    gameId: string,
    period: ArcadeCompetitionPeriod,
    at?: string,
    signal?: AbortSignal,
  ): Promise<ArcadeCompetitionSnapshot> {
    const params = at === undefined ? '' : `?at=${encodeURIComponent(at)}`
    const response = await this.fetcher(
      `/api/arcade-competitions/${encodeURIComponent(gameId)}/${period}${params}`,
      { method: 'GET', cache: 'no-store', signal },
    )
    const value = await response.json().catch(() => null) as unknown

    if (!response.ok) {
      const problem = isRecord(value) ? value as CompetitionProblem : null
      throw new ArcadeCompetitionRequestError(response.status, problem?.code)
    }
    if (!isArcadeCompetitionSnapshot(value)) {
      throw new Error('The server returned an invalid arcade competition leaderboard.')
    }
    return value
  }

  async startAsteroidsAttempt(
    period: ArcadeCompetitionPaidPeriod,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<AsteroidsPaidAttempt> {
    if (idempotencyKey.trim().length === 0) throw new Error('An idempotency key is required.')
    const response = await this.fetcher(`/api/arcade-competitions/asteroids/${period}/attempts`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      cache: 'no-store',
      signal,
    })
    const result = await this.readResponse(response, isAsteroidsPaidAttempt)
    if (result.period !== period) throw new Error('The server returned an invalid arcade competition response.')
    return result
  }

  async completeAsteroidsReplay(
    period: ArcadeCompetitionPaidPeriod,
    runId: string,
    replay: AsteroidsReplayPayload,
    signal?: AbortSignal,
  ): Promise<AsteroidsReplayCompletion> {
    const response = await this.fetcher(
      `/api/arcade-competitions/asteroids/${period}/runs/${encodeURIComponent(runId)}/replay`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(replay),
        cache: 'no-store',
        signal,
      },
    )
    const result = await this.readResponse(response, isAsteroidsReplayCompletion)
    if (result.runId !== runId) throw new Error('The server returned an invalid arcade competition response.')
    return result
  }

  async startFreeAsteroidsRun(idempotencyKey: string, signal?: AbortSignal): Promise<AsteroidsFreeRun> {
    if (idempotencyKey.trim().length === 0) throw new Error('An idempotency key is required.')
    const response = await this.fetcher('/api/arcade-competitions/asteroids/free/runs', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      cache: 'no-store',
      signal,
    })
    return this.readResponse(response, isAsteroidsFreeRun)
  }

  async completeFreeAsteroidsReplay(
    runId: string,
    replay: AsteroidsReplayPayload,
    signal?: AbortSignal,
  ): Promise<AsteroidsReplayCompletion> {
    const response = await this.fetcher(
      `/api/arcade-competitions/asteroids/free/runs/${encodeURIComponent(runId)}/replay`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(replay),
        cache: 'no-store',
        signal,
      },
    )
    const result = await this.readResponse(response, isAsteroidsReplayCompletion)
    if (result.runId !== runId) throw new Error('The server returned an invalid arcade competition response.')
    return result
  }

  async startFreeFlappyRun(idempotencyKey: string, signal?: AbortSignal): Promise<FlappyFreeRun> {
    if (idempotencyKey.trim().length === 0) throw new Error('An idempotency key is required.')
    const response = await this.fetcher('/api/arcade-competitions/flappy/free/runs', {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      cache: 'no-store',
      signal,
    })
    return this.readResponse(response, isFlappyFreeRun)
  }

  async completeFreeFlappyReplay(
    runId: string,
    replay: FlappyReplayPayload,
    signal?: AbortSignal,
  ): Promise<FlappyReplayCompletion> {
    const response = await this.fetcher(
      `/api/arcade-competitions/flappy/free/runs/${encodeURIComponent(runId)}/replay`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(replay),
        cache: 'no-store',
        signal,
      },
    )
    const result = await this.readResponse(response, isFlappyReplayCompletion)
    if (result.runId !== runId) throw new Error('The server returned an invalid arcade competition response.')
    return result
  }

  private async readResponse<T>(response: Response, validator: (value: unknown) => value is T): Promise<T> {
    const value = await response.json().catch(() => null) as unknown
    if (!response.ok) {
      const problem = isRecord(value) ? value as CompetitionProblem : null
      throw new ArcadeCompetitionRequestError(response.status, problem?.code)
    }
    if (!validator(value)) throw new Error('The server returned an invalid arcade competition response.')
    return value
  }
}

function isArcadeCompetitionSnapshot(value: unknown): value is ArcadeCompetitionSnapshot {
  if (!isRecord(value) || !isPeriod(value.period) || !isPlacements(value.leaderboard) || typeof value.gameId !== 'string') {
    return false
  }
  if (value.period === 'all-time') {
    return true
  }
  return typeof value.startsAtUtc === 'string'
    && typeof value.endsAtUtc === 'string'
    && typeof value.entriesOpen === 'boolean'
    && typeof value.isCompleted === 'boolean'
    && isNonNegativeInteger(value.totalUniquePlayers)
    && isNonNegativeInteger(value.visibleJackpotCents)
    && (value.solePlayerRefund === null || isRefund(value.solePlayerRefund))
}

function isPeriod(value: unknown): value is ArcadeCompetitionPeriod {
  return value === 'daily' || value === 'weekly' || value === 'all-time'
}

function isPaidPeriod(value: unknown): value is ArcadeCompetitionPaidPeriod {
  return value === 'daily' || value === 'weekly'
}

function isAsteroidsPaidAttempt(value: unknown): value is AsteroidsPaidAttempt {
  return isRecord(value)
    && value.gameId === 'asteroids'
    && isPaidPeriod(value.period)
    && isNonBlankString(value.attemptId)
    && isUtcTimestamp(value.startsAtUtc)
    && isUtcTimestamp(value.endsAtUtc)
    && isNonNegativeInteger(value.entryFeeCents)
    && typeof value.wasReplay === 'boolean'
    && isRunId(value.runId)
    && isSeedHex(value.seedHex)
}

function isAsteroidsReplayCompletion(value: unknown): value is AsteroidsReplayCompletion {
  return isRecord(value)
    && isRunId(value.runId)
    && isNonNegativeInteger(value.score)
    && (value.terminal === 'completed' || value.terminal === 'gameover')
    && typeof value.wasReplay === 'boolean'
}

function isAsteroidsFreeRun(value: unknown): value is AsteroidsFreeRun {
  return isRecord(value)
    && isRunId(value.runId)
    && isSeedHex(value.seedHex)
    && isUtcTimestamp(value.startedAtUtc)
    && typeof value.wasReplay === 'boolean'
}

function isFlappyFreeRun(value: unknown): value is FlappyFreeRun {
  return isRecord(value)
    && isRunId(value.runId)
    && isNonNegativeInteger(value.seed)
    && value.seed > 0
    && value.seed <= 0xffff_ffff
    && isUtcTimestamp(value.startedAtUtc)
    && typeof value.wasReplay === 'boolean'
}

function isFlappyReplayCompletion(value: unknown): value is FlappyReplayCompletion {
  return isRecord(value)
    && isRunId(value.runId)
    && isNonNegativeInteger(value.score)
    && (value.terminal === 'obstacle-collision' || value.terminal === 'ground-collision' || value.terminal === 'ceiling-collision')
    && typeof value.wasReplay === 'boolean'
}

function isPlacements(value: unknown): value is readonly ArcadeCompetitionPlacement[] {
  return Array.isArray(value) && value.every((placement) => isRecord(placement)
    && isNonNegativeInteger(placement.position)
    && typeof placement.playerId === 'string'
    && typeof placement.score === 'number'
    && Number.isFinite(placement.score))
}

function isRefund(value: unknown): value is ArcadeCompetitionRefund {
  return isRecord(value)
    && typeof value.playerId === 'string'
    && isNonNegativeInteger(value.amountCents)
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isUtcTimestamp(value: unknown): value is string {
  return typeof value === 'string'
    && /(?:Z|[+-]00:00)$/.test(value)
    && Number.isFinite(Date.parse(value))
}

function isRunId(value: unknown): value is string {
  return typeof value === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(value)
}

function isSeedHex(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{16}$/.test(value) && value !== '0000000000000000'
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object'
}
