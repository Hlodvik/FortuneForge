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
  private recoveryScope: string | undefined

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

  async getStatus(signal?: AbortSignal): Promise<HorseFlightStatus> {
    const window = await this.currentRecoveryWindow(signal)
    this.recoveryScope = window.scope
    return { available: window.available, tickMilliseconds, balance: this.balance, mode: this.mode.kind,
      recoveryMode: this.mode.kind === 'free' ? 'free' : `paid:${this.mode.period}`, recoveryScope: this.recoveryScope }
  }

  async start(options: HorseFlightRequestOptions = {}): Promise<HorseFlightStart> {
    const idempotencyKey = options.idempotencyKey ?? createHorseFlightCompetitionKey()
    const scope = await this.assertRecoveryScope(options, true)
    let run: { runId: string; seed: number }, acceptedScope = scope
    if (this.mode.kind === 'paid') {
      const accepted = await this.gateway.startHorseFlightAttempt(this.mode.period, idempotencyKey, options.signal)
      void this.onPaidMutation?.()
      acceptedScope = this.paidRecoveryScope(accepted.period, accepted.startsAtUtc, accepted.endsAtUtc)
      run = accepted
    } else run = await this.gateway.startFreeHorseFlightRun(idempotencyKey, options.signal)
    // Record the actual accepted window if admission crossed its boundary.
    // Retrying an unconfirmed request still requires the previously frozen one.
    return { runId: run.runId, seed: run.seed, tickMilliseconds, recoveryScope: acceptedScope }
  }

  async complete(
    runId: string,
    totalTicks: number,
    jumpTicks: readonly number[],
    options: HorseFlightRequestOptions = {},
    rightClickTicks: readonly number[] = [],
  ): Promise<HorseFlightResult> {
    await this.assertRecoveryScope(options)
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

  private async currentRecoveryWindow(signal?: AbortSignal): Promise<{ scope: string; available: boolean }> {
    if (this.mode.kind === 'free') return { scope: JSON.stringify(['free']), available: true }
    const window = await this.gateway.getLeaderboard('horse-flight', this.mode.period, undefined, signal)
    if (window.gameId !== 'horse-flight' || window.period !== this.mode.period ||
      !('startsAtUtc' in window)) {
      throw new Error('The selected Horse Flight competition window is unavailable.')
    }
    return { scope: this.paidRecoveryScope(window.period, window.startsAtUtc, window.endsAtUtc), available: window.entriesOpen && !window.isCompleted }
  }

  private paidRecoveryScope(period: ArcadeCompetitionPaidPeriod, startsAt: string, endsAt: string): string {
    if (this.mode.kind !== 'paid' || period !== this.mode.period ||
      !Number.isFinite(Date.parse(startsAt)) || !Number.isFinite(Date.parse(endsAt)) || Date.parse(startsAt) >= Date.parse(endsAt)) {
      throw new Error('The accepted Horse Flight competition window does not match the selected mode.')
    }
    return JSON.stringify([`paid:${period}`, new Date(startsAt).toISOString(), new Date(endsAt).toISOString()])
  }

  private async assertRecoveryScope(options: HorseFlightRequestOptions, admission = false): Promise<string> {
    const window = await this.currentRecoveryWindow(options.signal), current = window.scope
    if (admission && !window.available) throw new Error('The selected Horse Flight competition window is closed to new entries.')
    const expected = options.recoveryScope ?? this.recoveryScope
    if (expected && expected !== current) {
      throw new Error('The Horse Flight competition window changed. The previous request cannot be replayed in this window.')
    }
    this.recoveryScope = current
    return current
  }
}

export function createHorseFlightCompetitionKey(): string {
  const random = typeof crypto?.randomUUID === 'function'
    ? crypto.randomUUID().replaceAll('-', '')
    : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`
  return `horse-flight-${random}`
}
