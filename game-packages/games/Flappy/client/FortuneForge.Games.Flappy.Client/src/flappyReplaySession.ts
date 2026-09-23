import { advanceFlappyFrame, startFlappySimulation, type FlappySimulationState } from './flappySimulation'

export const maximumFlappyReplayTicks = 9_000
export const maximumFlappyReplayFlaps = 1_500
export type FlappyReplayPayload = Readonly<{ totalTicks: number; flapTicks: readonly number[] }>
export type FlappyReplaySessionStatus = 'running' | 'finished' | 'failed'
export type FlappyReplayCompletion = Readonly<{ replay: FlappyReplayPayload; display: Readonly<{ score: number; phase: Exclude<FlappySimulationState['phase'], 'playing'> }> }>
export type FlappyReplaySessionView = Readonly<{ state: FlappySimulationState; status: FlappyReplaySessionStatus; remainingTicks: number; error: string | null }>

/** Records only player flap instants; the server derives the outcome from the issued seed. */
export class FlappyReplaySession {
  private readonly flapTicks: number[] = []
  private current: FlappySimulationState
  private currentStatus: FlappyReplaySessionStatus = 'running'
  private completion: FlappyReplayCompletion | null = null
  private delivered = false
  private failure: string | null = null

  constructor(readonly runId: string, seed: number) {
    if (runId.trim().length === 0) throw new Error('Flappy run id is required.')
    this.current = startFlappySimulation(seed)
  }

  get view(): FlappyReplaySessionView { return { state: this.current, status: this.currentStatus, remainingTicks: Math.max(0, maximumFlappyReplayTicks - this.current.tick), error: this.failure } }

  advanceFrame(flap = false): FlappyReplaySessionView {
    if (this.currentStatus !== 'running') return this.view
    if (flap) {
      if (this.flapTicks.length === maximumFlappyReplayFlaps) return this.fail('Replay flap limit reached. This run cannot be submitted.')
      this.flapTicks.push(this.current.tick)
    }
    this.current = advanceFlappyFrame(this.current, flap)
    if (this.current.phase !== 'playing') this.finish()
    else if (this.current.tick === maximumFlappyReplayTicks) this.fail('This run reached the replay time limit.')
    return this.view
  }

  takeCompletion(): FlappyReplayCompletion | null {
    if (this.delivered || this.completion === null) return null
    this.delivered = true
    return this.completion
  }

  private finish(): void {
    this.currentStatus = 'finished'
    this.completion = { replay: { totalTicks: this.current.tick, flapTicks: [...this.flapTicks] }, display: { score: this.current.score, phase: this.current.phase as Exclude<FlappySimulationState['phase'], 'playing'> } }
  }

  private fail(message: string): FlappyReplaySessionView { this.currentStatus = 'failed'; this.failure = message; return this.view }
}
