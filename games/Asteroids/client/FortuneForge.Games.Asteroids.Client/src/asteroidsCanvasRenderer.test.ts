import { describe, expect, it } from 'vitest'
import { renderAsteroids } from './asteroidsCanvasRenderer'
import type { Asteroid, AsteroidsAlienShip, AsteroidsGameState } from './contracts'

describe('renderAsteroids', () => {
  it('selects the asteroid sprite cell supplied by the game state', () => {
    const context = testContext()
    renderAsteroids(context.value, gameWith({ id: 7, spriteVariant: 6, size: 'medium', radius: 23 }), { asteroid: readyImage(3038) })

    expect(context.drawImageCalls).toHaveLength(1)
    expect(context.drawImageCalls[0].slice(1, 5)).toEqual([2604, 0, 434, 434])
  })

  it('keeps the primitive asteroid fallback when an atlas is unavailable', () => {
    const context = testContext()
    renderAsteroids(context.value, gameWith({ id: 1, size: 'large', radius: 38 }), { asteroid: { complete: false, naturalWidth: 0 } as HTMLImageElement })

    expect(context.drawImageCalls).toHaveLength(0)
    expect(context.strokeCalls).toBeGreaterThan(0)
  })

  it('uses the top laser row and bottom impact row from their shared atlas', () => {
    const context = testContext()
    const game = { ...gameWith({ id: 1, size: 'small', radius: 13 }), bullets: [{ id: 3, x: 400, y: 300, velocityX: 10, velocityY: 0, remainingTicks: 20 }] }
    renderAsteroids(context.value, game, { laserImpact: readyImage(2172) }, [{ x: 400, y: 300, startedTick: 0, kind: 'destroyed' }])

    expect(context.drawImageCalls.map(call => call.slice(1, 5))).toEqual([[543, 0, 181, 181], [0, 181, 181, 181]])
  })

  it('draws the dedicated asteroid explosion and the matching power-up cell', () => {
    const context = testContext()
    const game = { ...gameWith({ id: 1, size: 'small', radius: 13 }), powerUps: [{ id: 3, x: 400, y: 300, velocityX: 0, velocityY: 0, remainingTicks: 20, type: 'extra-life' as const }] }
    renderAsteroids(context.value, game, { explosion: readyImage(2172), powerUp: readyImage(2172) }, [{ x: 400, y: 300, startedTick: 0, kind: 'destroyed' }])

    expect(context.drawImageCalls.map(call => call.slice(1, 5))).toEqual([[1448, 0, 724, 724], [0, 0, 362, 724]])
  })

  it('uses one stable thrust frame rather than alternating the ship body', () => {
    const context = testContext()
    const game = { ...gameWith({ id: 1, size: 'small', radius: 13 }), asteroids: [], tick: 17, ship: { x: 400, y: 300, velocityX: 0, velocityY: -4, angle: 0, invulnerabilityTicks: 0, thrustTicks: 2 } }

    renderAsteroids(context.value, game, { ship: readyImage(1086) })

    expect(context.drawImageCalls).toHaveLength(1)
    expect(context.drawImageCalls[0].slice(1, 5)).toEqual([272, 0, 272, 362])
  })

  it.each([
    ['scout', 0, 2],
    ['hunter', 1086, -3],
  ] as const)('selects and faces the %s alien sprite cell', (type, sourceX, velocityX) => {
    const context = testContext()
    const game = {
      ...gameWith({ id: 1, size: 'small', radius: 13 }),
      asteroids: [],
      alienShip: alienShip({ type, velocityX }),
    }

    renderAsteroids(context.value, game, { alienShip: readyImage(2172) })

    expect(context.drawImageCalls).toHaveLength(1)
    expect(context.drawImageCalls[0].slice(1, 5)).toEqual([sourceX, 0, 1086, 724])
    expect(context.scaleCalls).toContainEqual([velocityX < 0 ? -1 : 1, 1])
  })

  it('draws distinct layered plasma for enemy bullets without another atlas', () => {
    const context = testContext()
    const game = {
      ...gameWith({ id: 1, size: 'small', radius: 13 }),
      asteroids: [],
      enemyBullets: [{ id: 30, x: 320, y: 240, velocityX: -5, velocityY: 1, remainingTicks: 60 }],
    }

    renderAsteroids(context.value, game)

    expect(context.fillRectCalls).toContainEqual([-12, -3, 13, 6])
    expect(context.fillRectCalls).toContainEqual([0, -1, 5, 2])
  })

  it('draws an edge-straddling alien on both sides of the toroidal field', () => {
    const context = testContext()
    const game = {
      ...gameWith({ id: 1, size: 'small', radius: 13 }),
      asteroids: [],
      alienShip: alienShip({ x: 3 }),
    }

    renderAsteroids(context.value, game, { alienShip: readyImage(2172) })

    expect(context.drawImageCalls).toHaveLength(2)
    expect(context.translateCalls).toContainEqual([3, 180])
    expect(context.translateCalls).toContainEqual([803, 180])
  })

  it('renders alien impacts as pixel shards without reusing the asteroid explosion art', () => {
    const context = testContext()
    const game = { ...gameWith({ id: 1, size: 'small', radius: 13 }), asteroids: [], tick: 4 }

    renderAsteroids(context.value, game, { explosion: readyImage(2172) }, [{ x: 400, y: 300, startedTick: 0, kind: 'alien-destroyed' }])

    expect(context.drawImageCalls).toHaveLength(0)
    expect(context.fillRectCalls.length).toBeGreaterThan(8)
  })
})

function gameWith(asteroid: Pick<Asteroid, 'id' | 'size' | 'radius'> & Partial<Asteroid>): AsteroidsGameState {
  return {
    gameId: 'game-1',
    width: 800,
    height: 600,
    ship: { x: 400, y: 300, velocityX: 0, velocityY: 0, angle: 0, invulnerabilityTicks: 0, thrustTicks: 0 },
    asteroids: [{ x: 400, y: 300, velocityX: 0, velocityY: 0, hitPoints: 1, spriteVariant: 0, ...asteroid }],
    bullets: [],
    alienShip: null,
    enemyBullets: [],
    powerUps: [],
    score: 0,
    bestScore: 0,
    lives: 3,
    wave: 1,
    tick: 0,
    phase: 'playing',
    lastEvent: 'started',
    scoreGained: 0,
    rapidFireTicks: 0,
    message: 'Ready.',
  }
}

function alienShip(overrides: Partial<AsteroidsAlienShip> = {}): AsteroidsAlienShip {
  return {
    id: 20,
    x: 600,
    y: 180,
    velocityX: 2,
    velocityY: 0.5,
    radius: 20,
    type: 'scout',
    hitPoints: 2,
    fireCooldownTicks: 40,
    courseChangeTicks: 50,
    remainingTicks: 300,
    ...overrides,
  }
}

function readyImage(naturalWidth: number): HTMLImageElement { return { complete: true, naturalWidth } as HTMLImageElement }

function testContext(): { value: CanvasRenderingContext2D; drawImageCalls: unknown[][]; fillRectCalls: unknown[][]; scaleCalls: unknown[][]; translateCalls: unknown[][]; strokeCalls: number } {
  const drawImageCalls: unknown[][] = []
  const fillRectCalls: unknown[][] = []
  const scaleCalls: unknown[][] = []
  const translateCalls: unknown[][] = []
  let strokeCalls = 0
  const value = {
    save() {}, restore() {}, clearRect() {}, fillRect(...args: unknown[]) { fillRectCalls.push(args) }, beginPath() {}, arc() {}, fill() {}, closePath() {}, moveTo() {}, lineTo() {},
    translate(...args: unknown[]) { translateCalls.push(args) }, scale(...args: unknown[]) { scaleCalls.push(args) }, rotate() {},
    stroke() { strokeCalls++ },
    drawImage(...args: unknown[]) { drawImageCalls.push(args) },
  } as unknown as CanvasRenderingContext2D
  return { value, drawImageCalls, fillRectCalls, scaleCalls, translateCalls, get strokeCalls() { return strokeCalls } }
}
