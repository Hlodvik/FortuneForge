import type { SnakeDirection, SnakeGameState, SnakeGateway } from './contracts'
import { SnakeGatewayError } from './httpSnakeGateway'

export const snakeSpeeds = { relaxed: 170, classic: 115, turbo: 78 } as const
export type SnakeSpeed = keyof typeof snakeSpeeds
export type SnakeRunPhase = 'loading' | 'ready' | 'playing' | 'paused' | 'won' | 'lost' | 'error'
export type SnakeRunSnapshot = Readonly<{
  game: SnakeGameState | null; phase: SnakeRunPhase; busy: boolean; error: string | null
  speed: SnakeSpeed; boardSize: number; pendingDirection: SnakeDirection | null
}>

/** Serializes cabinet intent around the existing gateway; never simulates game rules. */
export class SnakeRunController {
  private snapshot: SnakeRunSnapshot = { game: null, phase: 'loading', busy: true, error: null, speed: 'classic', boardSize: 20, pendingDirection: null }
  private readonly listeners = new Set<() => void>()
  private active = false
  private lease = 0
  private movementGeneration = 0
  private timer: ReturnType<typeof setTimeout> | null = null
  private pending: Promise<void> | null = null
  private request: AbortController | null = null
  private turns: SnakeDirection[] = []
  private plannedDirection: SnakeDirection = 'right'

  constructor(private readonly gateway: SnakeGateway, private readonly canRun = () => typeof document === 'undefined' || (!document.hidden && document.hasFocus())) {}
  readonly getSnapshot = () => this.snapshot
  readonly subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener) } }

  activate() { this.active = true; this.lease++; this.prepare(false) }
  dispose() { this.active = false; this.lease++; this.movementGeneration++; this.stopTimer(); this.request?.abort(); this.turns = []; this.pending = null }

  play() {
    if (!this.active || this.snapshot.busy || this.snapshot.phase !== 'ready') return
    this.publish({ phase: this.canRun() ? 'playing' : 'paused', error: null }); this.schedule()
  }
  pause() {
    if (this.snapshot.phase !== 'playing') return
    this.movementGeneration++; this.stopTimer(); this.turns = []; this.publish({ phase: 'paused', pendingDirection: null })
  }
  resume() {
    if (!this.active || this.snapshot.phase !== 'paused' || this.snapshot.busy || !this.canRun()) return
    this.publish({ phase: 'playing' }); this.schedule()
  }
  togglePause() { if (this.snapshot.phase === 'playing') this.pause(); else if (this.snapshot.phase === 'paused') this.resume() }

  turn(direction: SnakeDirection) {
    const { game, phase } = this.snapshot
    if (!this.active || !game || (phase !== 'ready' && phase !== 'playing') || !this.canRun()) return
    const heading = this.turns.at(-1) ?? this.plannedDirection
    if (opposite(heading, direction)) return
    if (direction !== heading && this.turns.length < 2) {
      this.turns.push(direction); this.publish({ pendingDirection: this.turns[0] ?? null })
    }
    if (phase === 'ready') this.play()
  }

  newGame(play = true, speed = this.snapshot.speed, boardSize = this.snapshot.boardSize) {
    if (!this.active || this.snapshot.busy) return
    this.publish({ speed, boardSize }); this.prepare(play)
  }
  retry() { if (!this.active || this.snapshot.busy) return; this.prepare(false) }
  configure(speed: SnakeSpeed, boardSize: number) {
    if (boardSize !== this.snapshot.boardSize) this.newGame(true, speed, boardSize)
    else { this.pause(); this.publish({ speed }) }
  }

  private prepare(play: boolean) {
    const lease = this.lease, prior = this.pending
    this.movementGeneration++; this.stopTimer(); this.turns = []
    this.publish({ phase: 'loading', busy: true, error: null, pendingDirection: null })
    const task = (async () => {
      await prior
      if (!this.current(lease)) return
      const controller = new AbortController(); this.request = controller
      try {
        const game = this.snapshot.game, options = { width: this.snapshot.boardSize, height: this.snapshot.boardSize }
        let next: SnakeGameState
        if (game) next = await this.gateway.reset(game.gameId, undefined, controller.signal, options)
        else {
          const status = await this.gateway.getStatus(controller.signal)
          if (!this.current(lease)) return
          if (!status.available) throw new SnakeGatewayError('Snake is unavailable right now.', 'unavailable', 503)
          next = await this.gateway.startGame(undefined, controller.signal, options)
        }
        if (!this.current(lease)) return
        this.plannedDirection = next.direction
        this.publish({ game: next, phase: play ? (this.canRun() ? 'playing' : 'paused') : 'ready', busy: false, boardSize: next.width, error: null })
      } catch (reason) { if (this.current(lease)) this.fail(reason, true) }
      finally { if (this.request === controller) this.request = null }
    })()
    this.track(task, lease)
  }

  private schedule() {
    if (!this.active || this.snapshot.phase !== 'playing' || this.timer !== null || this.pending) return
    this.timer = setTimeout(() => { this.timer = null; this.step() }, snakeSpeeds[this.snapshot.speed])
  }
  private step() {
    if (!this.active || this.snapshot.phase !== 'playing' || this.pending || !this.snapshot.game) return
    if (!this.canRun()) { this.pause(); return }
    const lease = this.lease, movementGeneration = this.movementGeneration, controller = new AbortController(); this.request = controller
    const task = (async () => {
      try {
        let game = this.snapshot.game!
        const direction = this.turns.shift()
        if (direction) {
          this.plannedDirection = direction
          game = await this.gateway.turn(game.gameId, direction, controller.signal)
          if (!this.current(lease)) return
          this.publish({ game, pendingDirection: this.turns[0] ?? null })
        }
        if (this.snapshot.phase !== 'playing' || movementGeneration !== this.movementGeneration) return
        if (!this.canRun()) { this.pause(); return }
        const next = await this.gateway.tick(game.gameId, controller.signal)
        if (!this.current(lease)) return
        this.plannedDirection = next.direction
        this.publish({ game: next, phase: this.snapshot.busy || next.phase === 'playing' ? this.snapshot.phase : next.phase })
        if (next.phase !== 'playing') { this.turns = []; this.publish({ pendingDirection: null }) }
      } catch (reason) { if (this.current(lease) && !this.snapshot.busy) this.fail(reason, false) }
      finally { if (this.request === controller) this.request = null }
    })()
    this.track(task, lease)
  }
  private track(task: Promise<void>, lease: number) {
    this.pending = task
    void task.finally(() => { if (this.current(lease) && this.pending === task) { this.pending = null; this.schedule() } })
  }
  private fail(reason: unknown, opening: boolean) {
    this.stopTimer(); this.turns = []
    this.publish({ phase: 'error', busy: false, pendingDirection: null, error: reason instanceof SnakeGatewayError ? reason.message : opening ? 'Snake could not be prepared. Try again.' : 'This run was interrupted. Start a new run.' })
  }
  private current(lease: number) { return this.active && this.lease === lease }
  private stopTimer() { if (this.timer !== null) clearTimeout(this.timer); this.timer = null }
  private publish(patch: Partial<SnakeRunSnapshot>) { this.snapshot = { ...this.snapshot, ...patch }; this.listeners.forEach(listener => listener()) }
}

export function opposite(first: SnakeDirection, second: SnakeDirection) {
  return (first === 'up' && second === 'down') || (first === 'down' && second === 'up') || (first === 'left' && second === 'right') || (first === 'right' && second === 'left')
}
