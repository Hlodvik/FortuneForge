import { AsteroidsControl, advanceAsteroidsFrame, foldPaidSeedHex, startAsteroidsSimulation, type AsteroidsSimulationState } from './asteroidsSimulation'

export const maximumReplaySteps = 3_600
export const maximumReplayCommands = 512
export const asteroidsReplayRulesVersion = 2
export type AsteroidsReplayCommand = Readonly<{ step: number; input: number }>
export type AsteroidsReplayPayload = Readonly<{ rulesVersion: number; totalSteps: number; commands: readonly AsteroidsReplayCommand[] }>
export type AsteroidsReplayEndReason = 'game-over' | 'time-up'
export type AsteroidsReplayDisplayResult = Readonly<{ score: number; wave: number; lives: number; reason: AsteroidsReplayEndReason }>
export type AsteroidsReplayCompletion = Readonly<{ replay: AsteroidsReplayPayload; display: AsteroidsReplayDisplayResult }>
export type AsteroidsReplaySessionStatus = 'running' | 'finished' | 'failed'
export type AsteroidsHeldControl = 'left' | 'right' | 'thrust' | 'fire'
export type AsteroidsReplaySessionView = Readonly<{ state: AsteroidsSimulationState; status: AsteroidsReplaySessionStatus; remainingSteps: number; error: string | null; firedThisFrame: boolean }>

/** Pure fixed-step recorder. It never accepts a score or any identity beyond the server-issued seed. */
export class AsteroidsReplaySession {
  private readonly commands: AsteroidsReplayCommand[] = []
  private readonly held = new Set<AsteroidsHeldControl>()
  private activeMask: number = AsteroidsControl.None
  private lastTurn: 'left' | 'right' = 'left'
  private completion: AsteroidsReplayCompletion | null = null
  private delivered = false
  private current: AsteroidsSimulationState
  private currentStatus: AsteroidsReplaySessionStatus = 'running'
  private failure: string | null = null
  private lastFrameFired = false

  constructor(readonly runId: string, seedHex: string) {
    if (runId.trim().length === 0) throw new Error('Asteroids run id is required.')
    this.current = startAsteroidsSimulation(foldPaidSeedHex(seedHex))
  }

  get view(): AsteroidsReplaySessionView {
    return { state: this.current, status: this.currentStatus, remainingSteps: Math.max(0, maximumReplaySteps - this.current.tick), error: this.failure, firedThisFrame: this.lastFrameFired }
  }

  setHeld(control: AsteroidsHeldControl, pressed: boolean): void {
    if (this.currentStatus !== 'running') return
    this.lastFrameFired = false
    if (pressed) {
      const wasHeld = this.held.has(control)
      this.held.add(control)
      if (!wasHeld && (control === 'left' || control === 'right')) this.lastTurn = control
    } else this.held.delete(control)
  }

  clearHeld(): void {
    if (this.currentStatus === 'running') {
      this.held.clear()
      this.lastFrameFired = false
    }
  }

  advanceFrame(): AsteroidsReplaySessionView {
    if (this.currentStatus !== 'running') return this.view
    const mask = this.currentMask()
    this.lastFrameFired = false
    if (mask !== this.activeMask) {
      if (this.commands.length === maximumReplayCommands) {
        this.currentStatus = 'failed'
        this.failure = 'Replay transition limit reached. This run cannot be submitted.'
        this.held.clear()
        return this.view
      }
      this.commands.push({ step: this.current.tick, input: mask })
      this.activeMask = mask
    }
    this.lastFrameFired = (mask & AsteroidsControl.Fire) !== 0 && this.current.fireCooldownTicks === 0
    this.current = advanceAsteroidsFrame(this.current, mask)
    if (this.current.phase === 'game-over') this.finish('game-over')
    else if (this.current.tick === maximumReplaySteps) this.finish('time-up')
    return this.view
  }

  takeCompletion(): AsteroidsReplayCompletion | null {
    if (this.delivered || this.completion === null) return null
    this.delivered = true
    return this.completion
  }

  private currentMask(): number {
    let mask = this.held.has('thrust') ? AsteroidsControl.Thrust : AsteroidsControl.None
    if (this.held.has('fire')) mask |= AsteroidsControl.Fire
    const left = this.held.has('left'), right = this.held.has('right')
    if (left && right) mask |= this.lastTurn === 'left' ? AsteroidsControl.TurnLeft : AsteroidsControl.TurnRight
    else if (left) mask |= AsteroidsControl.TurnLeft
    else if (right) mask |= AsteroidsControl.TurnRight
    return mask
  }

  private finish(reason: AsteroidsReplayEndReason): void {
    this.currentStatus = 'finished'
    this.held.clear()
    this.completion = {
      replay: { rulesVersion: asteroidsReplayRulesVersion, totalSteps: this.current.tick, commands: this.commands.map(command => ({ ...command })) },
      display: { score: this.current.score, wave: this.current.wave, lives: this.current.lives, reason },
    }
  }
}
