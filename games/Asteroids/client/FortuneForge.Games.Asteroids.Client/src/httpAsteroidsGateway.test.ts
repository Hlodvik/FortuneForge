import { afterEach, describe, expect, it, vi } from 'vitest'
import { HttpAsteroidsGateway } from './httpAsteroidsGateway'

describe('HttpAsteroidsGateway enemy contracts', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('accepts one inspectable alien ship and its enemy projectiles', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(gameResponse())))

    const game = await new HttpAsteroidsGateway().startGame(42)

    expect(game.alienShip).toMatchObject({ id: 20, type: 'hunter', hitPoints: 4, courseChangeTicks: 31 })
    expect(game.enemyBullets).toEqual([
      { id: 21, x: 210, y: 180, velocityX: -4.5, velocityY: 1.25, remainingTicks: 80 },
    ])
  })

  it.each([
    ['an unknown alien type', { type: 'marauder' }],
    ['a negative course-change counter', { courseChangeTicks: -1 }],
    ['an expired enemy projectile', null],
  ])('rejects %s', async (_label, alienPatch) => {
    const game = gameResponse()
    const malformed = alienPatch === null
      ? { ...game, enemyBullets: [{ ...game.enemyBullets[0], remainingTicks: 0 }] }
      : { ...game, alienShip: { ...game.alienShip, ...alienPatch } }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(malformed)))

    await expect(new HttpAsteroidsGateway().startGame()).rejects.toThrow('invalid response')
  })
})

function gameResponse() {
  return {
    gameId: 'd5818a98-4ae6-49b0-a58b-244530ee7548',
    width: 800,
    height: 600,
    ship: { x: 400, y: 300, velocityX: 0, velocityY: 0, angle: 0, invulnerabilityTicks: 0, thrustTicks: 0 },
    asteroids: [],
    bullets: [],
    alienShip: {
      id: 20,
      x: 640,
      y: 180,
      velocityX: -3,
      velocityY: 0.5,
      radius: 16,
      type: 'hunter',
      hitPoints: 4,
      fireCooldownTicks: 22,
      courseChangeTicks: 31,
      remainingTicks: 280,
    },
    enemyBullets: [{ id: 21, x: 210, y: 180, velocityX: -4.5, velocityY: 1.25, remainingTicks: 80 }],
    powerUps: [],
    score: 300,
    bestScore: 1200,
    lives: 3,
    wave: 2,
    tick: 450,
    phase: 'playing',
    lastEvent: 'ticked',
    scoreGained: 0,
    rapidFireTicks: 0,
    message: '',
  }
}

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}
