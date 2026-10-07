import { describe, expect, it } from 'vitest'
import type { Asteroid, AsteroidsGameState, AsteroidsPowerUp } from './contracts'
import { asteroidsAudioUrgency, asteroidsFrameAudioCues } from './asteroidsFrameAudio'

describe('Asteroids frame audio', () => {
  it('recognises a player shot even when the action response keeps the same tick', () => {
    const before = game()
    const after = game({
      bullets: [{ id: 41, x: 400, y: 280, velocityX: 0, velocityY: -10, remainingTicks: 80 }],
      lastEvent: 'fired',
    })

    expect(asteroidsFrameAudioCues(before, after)).toEqual([
      { sound: 'player-shot', options: { pan: 0, rapidFire: false } },
    ])
  })

  it('plays an accepted replay shot even when its bullet collides in the same frame', () => {
    const before = game()
    const after = game({ tick: 11, lastEvent: 'hit' })

    expect(asteroidsFrameAudioCues(before, after, { playerFired: true }).map(cue => cue.sound)).toEqual(['player-shot'])
    expect(asteroidsFrameAudioCues(after, after, { playerFired: true })).toEqual([])
  })

  it('does not infer collision sounds from a stale same-tick response', () => {
    const before = game({ asteroids: [asteroid(1, 'large', 8)], lives: 3 })
    const after = game({ asteroids: [], lives: 2, lastEvent: 'damaged' })

    expect(asteroidsFrameAudioCues(before, after)).toEqual([])
  })

  it('ignores a stale lower-tick response instead of treating it as a new mission', () => {
    expect(asteroidsFrameAudioCues(game({ tick: 11 }), game({ tick: 10 }))).toEqual([])
  })

  it('distinguishes asteroid damage from destruction', () => {
    const before = game({ asteroids: [asteroid(1, 'large', 8, 120), asteroid(2, 'tiny', 1, 680)] })
    const after = game({ tick: 11, asteroids: [asteroid(1, 'large', 7, 120)], lastEvent: 'hit' })

    expect(asteroidsFrameAudioCues(before, after).map(cue => cue.sound)).toEqual([
      'asteroid-hit',
      'asteroid-destroyed',
    ])
  })

  it('covers power-up spawn and collection, including collection across a field seam', () => {
    const spawned = powerUp(7, 'shield', 799, 300)
    const beforeSpawn = game()
    const afterSpawn = game({ tick: 11, powerUps: [spawned] })
    expect(asteroidsFrameAudioCues(beforeSpawn, afterSpawn).map(cue => cue.sound)).toEqual(['power-up-spawned'])

    const afterCollection = game({ tick: 12, ship: { ...beforeSpawn.ship, x: 2, y: 300 }, lastEvent: 'power-up-collected' })
    expect(asteroidsFrameAudioCues(afterSpawn, afterCollection)).toEqual([
      { sound: 'power-up-collected', options: { powerUp: 'shield' } },
    ])
  })

  it('derives the complete alien cue sequence from enemy snapshot changes', () => {
    const alien = { id: 70, x: 720, y: 120, velocityX: -2, velocityY: 0, radius: 17, type: 'hunter' as const, hitPoints: 3, fireCooldownTicks: 20, courseChangeTicks: 35, remainingTicks: 400 }
    const entered = game({ tick: 11, alienShip: alien })
    expect(asteroidsFrameAudioCues(game(), entered).map(cue => cue.sound)).toEqual(['alien-entered'])

    const fired = game({ tick: 12, alienShip: { ...alien, fireCooldownTicks: 42 }, enemyBullets: [{ id: 71, x: 710, y: 120, velocityX: -5, velocityY: 1, remainingTicks: 95 }] })
    expect(asteroidsFrameAudioCues(entered, fired).map(cue => cue.sound)).toEqual(['alien-fired'])

    const hit = game({ tick: 13, alienShip: { ...alien, hitPoints: 2, remainingTicks: 399 }, enemyBullets: fired.enemyBullets })
    expect(asteroidsFrameAudioCues(fired, hit).map(cue => cue.sound)).toEqual(['alien-hit'])

    const vulnerable = game({ ...hit, tick: 14, alienShip: { ...alien, hitPoints: 1, remainingTicks: 398 } })
    const destroyed = game({ tick: 15, alienShip: null, enemyBullets: fired.enemyBullets })
    expect(asteroidsFrameAudioCues(vulnerable, destroyed).map(cue => cue.sound)).toEqual(['alien-destroyed'])
  })

  it('does not play an explosion when an alien leaves naturally', () => {
    const exiting = game({ tick: 20, alienShip: { id: 70, x: 819, y: 120, velocityX: 2, velocityY: 0, radius: 20, type: 'scout', hitPoints: 2, fireCooldownTicks: 20, courseChangeTicks: 35, remainingTicks: 200 } })
    const gone = game({ tick: 21, alienShip: null })
    expect(asteroidsFrameAudioCues(exiting, gone)).toEqual([])

    const expiring = game({ tick: 30, alienShip: { ...exiting.alienShip!, x: 400, velocityX: -2, remainingTicks: 1 } })
    expect(asteroidsFrameAudioCues(expiring, game({ tick: 31, alienShip: null }))).toEqual([])
  })

  it('covers ship damage, game over, wave completion, and mission reset', () => {
    const before = game({ lives: 3, wave: 1 })
    const damagedAndCleared = game({ tick: 11, lives: 2, wave: 2, lastEvent: 'wave-cleared' })
    expect(asteroidsFrameAudioCues(before, damagedAndCleared).map(cue => cue.sound)).toEqual(['ship-damaged', 'wave-cleared'])

    const terminal = game({ tick: 12, lives: 0, wave: 2, phase: 'game-over', lastEvent: 'game-over' })
    expect(asteroidsFrameAudioCues(damagedAndCleared, terminal).map(cue => cue.sound)).toEqual(['game-over'])

    expect(asteroidsFrameAudioCues(terminal, game({ gameId: 'new-mission', tick: 0 }))).toEqual([{ sound: 'mission-start' }])
    expect(asteroidsFrameAudioCues(null, game({ tick: 0, lastEvent: 'started' }))).toEqual([{ sound: 'mission-start' }])
    expect(asteroidsFrameAudioCues(game({ tick: 0, lastEvent: 'started' }), game({ tick: 0, lastEvent: 'started' }))).toEqual([])
  })

  it('accelerates ambience as a wave approaches completion', () => {
    const fullWave = game({ asteroids: [
      asteroid(1, 'large', 8), asteroid(2, 'medium', 6), asteroid(3, 'small', 4), asteroid(4, 'tiny', 2), asteroid(5, 'huge', 10),
    ] })
    const nearlyClear = game({ asteroids: [asteroid(4, 'tiny', 1)] })

    expect(asteroidsAudioUrgency(nearlyClear)).toBeGreaterThan(asteroidsAudioUrgency(fullWave))
    expect(asteroidsAudioUrgency(nearlyClear)).toBeLessThanOrEqual(1)
  })
})

function game(overrides: Partial<AsteroidsGameState> = {}): AsteroidsGameState {
  return {
    gameId: 'mission',
    width: 800,
    height: 600,
    ship: { x: 400, y: 300, velocityX: 0, velocityY: 0, angle: -Math.PI / 2, invulnerabilityTicks: 0, thrustTicks: 0 },
    asteroids: [],
    bullets: [],
    alienShip: null,
    enemyBullets: [],
    powerUps: [],
    score: 0,
    bestScore: 0,
    lives: 3,
    wave: 1,
    tick: 10,
    phase: 'playing',
    lastEvent: 'ticked',
    scoreGained: 0,
    rapidFireTicks: 0,
    message: '',
    ...overrides,
  }
}

function asteroid(id: number, size: Asteroid['size'], hitPoints: number, x = 400): Asteroid {
  return { id, x, y: 180, velocityX: 0, velocityY: 0, radius: 20, size, hitPoints, spriteVariant: 0 }
}

function powerUp(id: number, type: AsteroidsPowerUp['type'], x: number, y: number): AsteroidsPowerUp {
  return { id, type, x, y, velocityX: 0, velocityY: 0, remainingTicks: 200 }
}
