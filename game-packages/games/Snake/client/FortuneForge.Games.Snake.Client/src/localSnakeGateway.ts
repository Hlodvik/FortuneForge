import type { SnakeDirection, SnakeEvent, SnakeGameState, SnakeGateway, SnakePhase, SnakePoint, SnakeStatus } from './contracts'

type SnakeSession = Readonly<{ state: SnakeGameState; randomState: number }>

const status: SnakeStatus = {
  available: true,
  width: 20,
  height: 20,
  foodScore: 10,
  tickMilliseconds: 115,
  mode: 'arcade',
}

/**
 * A deterministic browser gateway for the arcade cabinet. It mirrors the
 * server engine rules while keeping an arcade run responsive offline.
 */
export class LocalSnakeGateway implements SnakeGateway {
  private bestScore = 0
  private gameNumber = 0
  private readonly sessions = new Map<string, SnakeSession>()

  async getStatus(signal?: AbortSignal): Promise<SnakeStatus> {
    assertNotAborted(signal)
    return status
  }

  async startGame(seed?: number, signal?: AbortSignal): Promise<SnakeGameState> {
    assertNotAborted(signal)
    const gameId = `snake-${++this.gameNumber}`
    const randomState = normalizeSeed(seed ?? seededNow(this.gameNumber))
    const center: SnakePoint = { x: Math.floor(status.width / 2), y: Math.floor(status.height / 2) }
    const body: readonly SnakePoint[] = [center, { x: center.x - 1, y: center.y }, { x: center.x - 2, y: center.y }]
    const food = findFood(body, status.width, status.height, randomState)
    const state: SnakeGameState = {
      gameId,
      width: status.width,
      height: status.height,
      body,
      food: food.point,
      direction: 'right',
      score: 0,
      bestScore: this.bestScore,
      moves: 0,
      length: body.length,
      phase: 'playing',
      lastEvent: 'started',
      scoreGained: 0,
      message: 'Choose a direction to begin.',
    }
    return this.save(gameId, state, food.randomState)
  }

  async turn(gameId: string, direction: SnakeDirection, signal?: AbortSignal): Promise<SnakeGameState> {
    assertNotAborted(signal)
    const session = this.sessionFor(gameId)
    const current = session.state
    if (current.phase !== 'playing') throw new Error('This run has ended. Start a new game to play again.')
    if (direction === current.direction) return this.save(gameId, { ...current, lastEvent: 'no-op', scoreGained: 0, message: 'Keep going.' }, session.randomState)
    if (isOpposite(current.direction, direction)) return this.save(gameId, { ...current, lastEvent: 'no-op', scoreGained: 0, message: 'The snake cannot reverse into itself.' }, session.randomState)
    return this.save(gameId, { ...current, direction, lastEvent: 'turned', scoreGained: 0, message: `Heading ${direction}.` }, session.randomState)
  }

  async tick(gameId: string, signal?: AbortSignal): Promise<SnakeGameState> {
    assertNotAborted(signal)
    const session = this.sessionFor(gameId)
    const current = session.state
    if (current.phase !== 'playing') throw new Error('This run has ended. Start a new game to play again.')

    const nextHead = step(current.body[0]!, current.direction)
    const eating = current.food !== null && samePoint(nextHead, current.food)
    const collisionBody = eating ? current.body : current.body.slice(0, -1)
    if (!inside(nextHead, current.width, current.height) || collisionBody.some(point => samePoint(point, nextHead))) {
      return this.save(gameId, { ...current, moves: current.moves + 1, phase: 'lost', lastEvent: 'lost', scoreGained: 0, message: 'The snake crashed. Start a new run to try again.' }, session.randomState)
    }

    const body = [nextHead, ...current.body]
    if (!eating) body.pop()
    const scoreGained = eating ? status.foodScore : 0
    const score = current.score + scoreGained
    this.bestScore = Math.max(this.bestScore, score)
    const food = eating ? findFood(body, current.width, current.height, session.randomState) : { point: current.food, randomState: session.randomState }
    const phase: SnakePhase = food.point === null ? 'won' : 'playing'
    const lastEvent: SnakeEvent = phase === 'won' ? 'won' : eating ? 'ate-food' : 'moved'
    const message = phase === 'won' ? 'The snake filled the board. You win!' : eating ? `Fruit collected. +${scoreGained} points.` : 'Keep moving.'
    return this.save(gameId, { ...current, body, food: food.point, score, bestScore: this.bestScore, moves: current.moves + 1, length: body.length, phase, lastEvent, scoreGained, message }, food.randomState)
  }

  async reset(gameId: string, seed?: number, signal?: AbortSignal): Promise<SnakeGameState> {
    assertNotAborted(signal)
    this.sessionFor(gameId)
    const state = await this.startGame(seed, signal)
    this.sessions.delete(gameId)
    return state
  }

  private sessionFor(gameId: string): SnakeSession {
    const session = this.sessions.get(gameId)
    if (!session) throw new Error('This Snake run could not be found. Start a new game.')
    return session
  }

  private save(gameId: string, state: SnakeGameState, randomState: number): SnakeGameState {
    const snapshot = { ...state, body: state.body.map(point => ({ ...point })) }
    this.sessions.set(gameId, { state: snapshot, randomState })
    return snapshot
  }
}

function assertNotAborted(signal?: AbortSignal) {
  if (signal?.aborted) throw new DOMException('The Snake request was cancelled.', 'AbortError')
}

function seededNow(gameNumber: number): number { return ((Date.now() >>> 0) ^ Math.imul(gameNumber, 0x9e3779b9)) >>> 0 }
function normalizeSeed(seed: number): number { const normalized = Math.trunc(seed) >>> 0; return normalized === 0 ? 0x9e3779b9 : normalized }
function nextRandom(value: number): number { let next = normalizeSeed(value); next ^= next << 13; next ^= next >>> 17; next ^= next << 5; return next >>> 0 }

function findFood(body: readonly SnakePoint[], width: number, height: number, randomState: number): Readonly<{ point: SnakePoint | null; randomState: number }> {
  const occupied = new Set(body.map(pointKey))
  const empty: SnakePoint[] = []
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) if (!occupied.has(`${x}:${y}`)) empty.push({ x, y })
  if (empty.length === 0) return { point: null, randomState }
  const nextState = nextRandom(randomState)
  return { point: empty[nextState % empty.length]!, randomState: nextState }
}

function step(point: SnakePoint, direction: SnakeDirection): SnakePoint {
  return direction === 'up' ? { x: point.x, y: point.y - 1 } : direction === 'right' ? { x: point.x + 1, y: point.y } : direction === 'down' ? { x: point.x, y: point.y + 1 } : { x: point.x - 1, y: point.y }
}

function inside(point: SnakePoint, width: number, height: number): boolean { return point.x >= 0 && point.x < width && point.y >= 0 && point.y < height }
function samePoint(left: SnakePoint, right: SnakePoint): boolean { return left.x === right.x && left.y === right.y }
function pointKey(point: SnakePoint): string { return `${point.x}:${point.y}` }
function isOpposite(first: SnakeDirection, second: SnakeDirection): boolean { return (first === 'up' && second === 'down') || (first === 'down' && second === 'up') || (first === 'left' && second === 'right') || (first === 'right' && second === 'left') }
