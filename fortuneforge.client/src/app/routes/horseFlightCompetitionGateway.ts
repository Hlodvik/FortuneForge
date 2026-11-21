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
  private readonly readBalance: () => number
  private readonly onPaidMutation?: () => void | Promise<void>
  private recoveryScope: string | undefined

  constructor(
    gateway: HorseFlightCompetitionGateway,
    mode: HorseFlightPlayMode,
    balance: number | (() => number),
    onPaidMutation?: () => void | Promise<void>,
  ) {
    this.gateway = gateway
    this.mode = mode
    this.readBalance = typeof balance === 'function' ? balance : () => balance
    this.onPaidMutation = onPaidMutation
  }

  async getStatus(signal?: AbortSignal): Promise<HorseFlightStatus> {
    const window = await this.currentRecoveryWindow(signal)
    this.recoveryScope = window.scope
    return { available: window.available, tickMilliseconds, balance: this.readBalance(), mode: this.mode.kind,
      recoveryMode: this.mode.kind === 'free' ? 'free' : `paid:${this.mode.period}`, recoveryScope: this.recoveryScope }
  }

  async start(options: HorseFlightRequestOptions = {}): Promise<HorseFlightStart> {
    const idempotencyKey = options.idempotencyKey ?? createHorseFlightCompetitionKey()
    let scope: string
    if (options.admission !== undefined || options.recoverOnly) {
      this.assertFrozenRecoveryMode(options)
      scope = options.recoveryScope ?? this.recoveryScope ?? ''
      if (options.admission !== undefined && (this.mode.kind !== 'paid' || scope !== this.paidRecoveryScope(this.mode.period,
        options.admission.expectedWindowStartsAtUtc, options.admission.expectedWindowEndsAtUtc)))
        throw new Error('The saved Horse Flight admission does not match its original window.')
    } else scope = await this.assertRecoveryScope(options, true)
    let run: { runId: string; seed: number; rulesVersion?: 1 | 2 | 3 | 4 }, acceptedScope = scope
    if (this.mode.kind === 'paid') {
      const terms = options.admission ? { ...options.admission, ...(options.recoverOnly ? { recoverOnly: true } : {}) }
        : options.recoverOnly ? { recoverOnly: true } : undefined
      const accepted = terms === undefined
        ? await this.gateway.startHorseFlightAttempt(this.mode.period, idempotencyKey, options.signal)
        : await this.gateway.startHorseFlightAttempt(this.mode.period, idempotencyKey, options.signal, terms)
      void this.onPaidMutation?.()
      acceptedScope = this.paidRecoveryScope(accepted.period, accepted.startsAtUtc, accepted.endsAtUtc)
      const issuedRules = 'rulesVersion' in accepted ? accepted.rulesVersion : undefined
      if (issuedRules !== undefined && issuedRules !== 1 && issuedRules !== 2 && issuedRules !== 3 && issuedRules !== 4)
        throw new Error('The issued Horse Flight rules version is unavailable.')
      run = { ...accepted, ...(issuedRules === undefined ? {} : { rulesVersion: issuedRules }) }
    } else run = await this.gateway.startFreeHorseFlightRun(idempotencyKey, options.signal)
    // Record the actual accepted window if admission crossed its boundary.
    // Retries retain their immutable admission terms; legacy starts use recover-only lookup.
    return { runId: run.runId, seed: run.seed, tickMilliseconds, recoveryScope: acceptedScope,
      ...(run.rulesVersion === undefined ? {} : { rulesVersion: run.rulesVersion }) }
  }

  async complete(
    runId: string,
    totalTicks: number,
    jumpTicks: readonly number[],
    options: HorseFlightRequestOptions = {},
    rightClickTicks: readonly number[] = [],
  ): Promise<HorseFlightResult> {
    this.assertFrozenRecoveryMode(options)
    const expectedPrefix = this.mode.kind === 'paid' ? /^horse_flight_[0-9a-f]{64}$/ : /^horse_flight_free_[0-9a-f]{64}$/
    if (!expectedPrefix.test(runId)) throw new Error('The Horse Flight run does not match the selected mode.')
    const replay: HorseFlightReplayPayload = { totalTicks, jumpTicks, rightClickTicks }
    const completion = this.mode.kind === 'paid'
      ? await this.gateway.completeHorseFlightReplay(this.mode.period, runId, replay, options.signal)
      : await this.gateway.completeFreeHorseFlightReplay(runId, replay, options.signal)
    if (this.mode.kind === 'paid') void this.onPaidMutation?.()
    return {
      runId: completion.runId,
      balance: this.readBalance(),
      score: completion.score,
      phase: completion.phase,
      totalTicks,
      jumpTicks,
      rightClickTicks,
    }
  }

  private assertFrozenRecoveryMode(options: HorseFlightRequestOptions): void {
    const expected = options.recoveryScope ?? this.recoveryScope
    if (expected === undefined) return
    try {
      const parts: unknown = JSON.parse(expected)
      if (this.mode.kind === 'free' && expected === JSON.stringify(['free'])) return
      if (this.mode.kind === 'paid' && Array.isArray(parts) && parts.length === 3 && parts[0] === `paid:${this.mode.period}` &&
        typeof parts[1] === 'string' && typeof parts[2] === 'string' &&
        expected === this.paidRecoveryScope(this.mode.period, parts[1], parts[2])) return
    } catch { /* Malformed scopes cannot identify a recorded flight. */ }
    throw new Error('The saved Horse Flight request does not match the selected mode.')
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
