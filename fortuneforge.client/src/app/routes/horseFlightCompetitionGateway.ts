import type {
  HorseFlightGateway,
  HorseFlightReplayPayload,
  HorseFlightRequestOptions,
  HorseFlightResult,
  HorseFlightStart,
  HorseFlightStatus,
} from '@fortuneforge/games-horse-flight'
import type {
  ArcadeCompetitionPaidPeriod,
  HorseFlightCompetitionGateway,
} from '../../games/arcade/arcadeCompetitionApi'

export type HorseFlightPlayMode = Readonly<{ kind: 'free' }> | Readonly<{
  kind: 'paid'
  period: ArcadeCompetitionPaidPeriod
}>

const tickMilliseconds = 20

export class HorseFlightCompetitionGatewayAdapter implements HorseFlightGateway {
  private readonly gateway: HorseFlightCompetitionGateway
  private readonly mode: HorseFlightPlayMode
  private readonly balance: number
  private readonly onPaidMutation?: () => void | Promise<void>

  constructor(
    gateway: HorseFlightCompetitionGateway,
    mode: HorseFlightPlayMode,
    balance: number,
    onPaidMutation?: () => void | Promise<void>,
  ) {
    this.gateway = gateway
    this.mode = mode
    this.balance = balance
    this.onPaidMutation = onPaidMutation
  }

  async getStatus(): Promise<HorseFlightStatus> {
    return { available: true, tickMilliseconds, balance: this.balance, mode: this.mode.kind }
  }

  async start(options: HorseFlightRequestOptions = {}): Promise<HorseFlightStart> {
    const idempotencyKey = options.idempotencyKey ?? createHorseFlightCompetitionKey()
    const run = this.mode.kind === 'paid'
      ? await this.gateway.startHorseFlightAttempt(this.mode.period, idempotencyKey, options.signal)
      : await this.gateway.startFreeHorseFlightRun(idempotencyKey, options.signal)
    if (this.mode.kind === 'paid') void this.onPaidMutation?.()
    return { runId: run.runId, seed: run.seed, tickMilliseconds }
  }

  async complete(
    runId: string,
    totalTicks: number,
    jumpTicks: readonly number[],
    options: HorseFlightRequestOptions = {},
    rightClickTicks: readonly number[] = [],
  ): Promise<HorseFlightResult> {
    const replay: HorseFlightReplayPayload = { totalTicks, jumpTicks, rightClickTicks }
    const completion = this.mode.kind === 'paid'
      ? await this.gateway.completeHorseFlightReplay(this.mode.period, runId, replay, options.signal)
      : await this.gateway.completeFreeHorseFlightReplay(runId, replay, options.signal)
    if (this.mode.kind === 'paid') void this.onPaidMutation?.()
    return {
      runId: completion.runId,
      balance: this.balance,
      score: completion.score,
      phase: completion.phase,
      totalTicks,
      jumpTicks,
      rightClickTicks,
    }
  }
}

export function createHorseFlightCompetitionKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `horse-flight-${random}`
}
