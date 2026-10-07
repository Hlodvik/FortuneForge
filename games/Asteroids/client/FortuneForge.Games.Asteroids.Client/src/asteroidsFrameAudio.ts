import {
  playAsteroidsSound,
  setAsteroidsAudioScene,
  type AsteroidsAlienSoundType,
  type AsteroidsSound,
  type AsteroidsSoundOptions,
} from './asteroidsAudio'
import { wasAlienDestroyed } from './asteroidsAlienLifecycle'
import type { AsteroidSize, AsteroidsAlienShip, AsteroidsGameState, AsteroidsPowerUp } from './contracts'

export type AsteroidsFrameAudioCue = Readonly<{ sound: AsteroidsSound; options?: AsteroidsSoundOptions }>
export type AsteroidsFrameAudioOptions = Readonly<{ playerFired?: boolean }>

type EnemyBulletSnapshot = Readonly<{ id: number; x: number; y: number }>
type AudioGameState = AsteroidsGameState & Readonly<{
  alienShip?: AsteroidsAlienShip | null
  enemyBullets?: readonly EnemyBulletSnapshot[]
}>

/**
 * Derives semantic cues from snapshots instead of trusting lastEvent, which can
 * be overwritten when several things happen in one simulation frame.
 */
export function asteroidsFrameAudioCues(previous: AsteroidsGameState | null, current: AsteroidsGameState, options: AsteroidsFrameAudioOptions = {}): readonly AsteroidsFrameAudioCue[] {
  if (previous === null) return current.tick === 0 && current.lastEvent === 'started' ? [{ sound: 'mission-start' }] : []
  if (previous.gameId !== current.gameId || (current.tick === 0 && current.lastEvent === 'started' && previous.tick !== 0)) return [{ sound: 'mission-start' }]
  if (current.tick < previous.tick) return []
  if (options.playerFired === true && current.tick === previous.tick) return []

  const cues: AsteroidsFrameAudioCue[] = []
  if (options.playerFired === true) {
    cues.push({ sound: 'player-shot', options: { pan: panFor(current.ship.x, current.width), rapidFire: current.rapidFireTicks > 0 } })
  } else {
    const previousBullets = new Set(previous.bullets.map(bullet => bullet.id))
    for (const bullet of current.bullets.filter(bullet => !previousBullets.has(bullet.id)).slice(0, 3)) {
      cues.push({ sound: 'player-shot', options: { pan: panFor(bullet.x, current.width), rapidFire: current.rapidFireTicks > 0 } })
    }
  }

  // Control actions in the server-backed surface can update entities without
  // advancing the simulation tick. Processing additions above fixes fire audio;
  // removals are ignored here so an out-of-order same-tick response cannot fake
  // a collision or destruction.
  if (current.tick === previous.tick) return cues

  const previousAsteroids = new Map(previous.asteroids.map(asteroid => [asteroid.id, asteroid]))
  const currentAsteroids = new Set(current.asteroids.map(asteroid => asteroid.id))
  for (const asteroid of current.asteroids) {
    const before = previousAsteroids.get(asteroid.id)
    if (before !== undefined && asteroid.hitPoints < before.hitPoints) {
      cues.push({ sound: 'asteroid-hit', options: { pan: panFor(asteroid.x, current.width), size: asteroid.size } })
    }
  }
  for (const asteroid of previous.asteroids.filter(asteroid => !currentAsteroids.has(asteroid.id)).slice(0, 4)) {
    cues.push({ sound: 'asteroid-destroyed', options: { pan: panFor(asteroid.x, current.width), size: asteroid.size } })
  }

  const previousPowerUps = new Map(previous.powerUps.map(powerUp => [powerUp.id, powerUp]))
  const currentPowerUps = new Set(current.powerUps.map(powerUp => powerUp.id))
  for (const powerUp of current.powerUps.filter(powerUp => !previousPowerUps.has(powerUp.id)).slice(0, 2)) {
    cues.push({ sound: 'power-up-spawned', options: { pan: panFor(powerUp.x, current.width), powerUp: powerUp.type } })
  }
  const removedPowerUps = previous.powerUps.filter(powerUp => !currentPowerUps.has(powerUp.id))
  for (const powerUp of collectedPowerUps(removedPowerUps, current).slice(0, 2)) {
    cues.push({ sound: 'power-up-collected', options: { powerUp: powerUp.type } })
  }

  const beforeWithEnemies = previous as AudioGameState
  const currentWithEnemies = current as AudioGameState
  const beforeAlien = beforeWithEnemies.alienShip ?? null
  const alien = currentWithEnemies.alienShip ?? null
  if (alien !== null && (beforeAlien === null || beforeAlien.id !== alien.id)) {
    cues.push({ sound: 'alien-entered', options: { alienType: alien.type, pan: panFor(alien.x, current.width) } })
  } else if (alien !== null && beforeAlien !== null && alien.id === beforeAlien.id && alien.hitPoints < beforeAlien.hitPoints) {
    cues.push({ sound: 'alien-hit', options: { alienType: alien.type, pan: panFor(alien.x, current.width) } })
  }
  if (beforeAlien !== null && (alien === null || alien.id !== beforeAlien.id) && wasAlienDestroyed(beforeAlien, current)) {
    cues.push({ sound: 'alien-destroyed', options: { alienType: beforeAlien.type, pan: panFor(beforeAlien.x, current.width) } })
  }

  const priorEnemyBullets = new Set((beforeWithEnemies.enemyBullets ?? []).map(bullet => bullet.id))
  for (const bullet of (currentWithEnemies.enemyBullets ?? []).filter(bullet => !priorEnemyBullets.has(bullet.id)).slice(0, 2)) {
    cues.push({ sound: 'alien-fired', options: { alienType: alien?.type ?? beforeAlien?.type ?? 'scout', pan: panFor(bullet.x, current.width) } })
  }

  const terminal = current.phase === 'game-over' || String(current.lastEvent) === 'game-over'
  if (terminal && previous.phase !== 'game-over') cues.push({ sound: 'game-over' })
  else if (current.lives < previous.lives || String(current.lastEvent) === 'damaged') cues.push({ sound: 'ship-damaged' })
  if (current.wave > previous.wave) cues.push({ sound: 'wave-cleared' })
  return cues
}

export function playAsteroidsFrameAudio(previous: AsteroidsGameState | null, current: AsteroidsGameState, options: AsteroidsFrameAudioOptions = {}): void {
  for (const cue of asteroidsFrameAudioCues(previous, current, options)) playAsteroidsSound(cue.sound, cue.options)
  const state = current as AudioGameState
  const alien = state.alienShip ?? null
  setAsteroidsAudioScene({
    playing: current.phase === 'playing',
    urgency: asteroidsAudioUrgency(current),
    alien: alien === null ? null : { type: alien.type, pan: panFor(alien.x, current.width) },
  })
}

export function asteroidsAudioUrgency(game: AsteroidsGameState): number {
  const initialWork = initialWaveWork(game.wave)
  const remainingWork = game.asteroids.reduce((total, asteroid) => total + asteroidWork(asteroid.size, asteroid.hitPoints), 0)
  const progress = initialWork <= 0 ? 0 : clamp(1 - remainingWork / initialWork, 0, 1)
  return clamp(.08 + progress * .82 + Math.min(.1, Math.max(0, game.wave - 1) * .015), 0, 1)
}

function collectedPowerUps(removed: readonly AsteroidsPowerUp[], current: AsteroidsGameState): readonly AsteroidsPowerUp[] {
  if (String(current.lastEvent) === 'power-up-collected') return removed
  return removed.filter(powerUp => toroidalDistance(powerUp.x, powerUp.y, current.ship.x, current.ship.y, current.width, current.height) <= 34)
}

function initialWaveWork(wave: number): number {
  let work = 0
  const count = Math.min(18, 4 + wave)
  for (let index = 0; index < count; index += 1) work += maximumAsteroidWork(initialSize(wave, index))
  return work
}

function asteroidWork(size: AsteroidSize, hitPoints: number): number { return Math.max(0, hitPoints) + descendantWork(size) }
function maximumAsteroidWork(size: AsteroidSize): number { return maximumHitPoints(size) + descendantWork(size) }
function descendantWork(size: AsteroidSize): number {
  return size === 'huge' ? 104 : size === 'large' ? 44 : size === 'medium' ? 16 : size === 'small' ? 4 : 0
}
function maximumHitPoints(size: AsteroidSize): number {
  return size === 'huge' ? 10 : size === 'large' ? 8 : size === 'medium' ? 6 : size === 'small' ? 4 : 2
}
function initialSize(wave: number, index: number): AsteroidSize {
  const value = (wave + index) % 5
  return value === 0 ? 'huge' : value === 1 ? 'large' : value === 2 ? 'medium' : value === 3 ? 'small' : 'tiny'
}

function toroidalDistance(x1: number, y1: number, x2: number, y2: number, width: number, height: number): number {
  const horizontal = Math.abs(x1 - x2) % width
  const vertical = Math.abs(y1 - y2) % height
  const deltaX = Math.min(horizontal, width - horizontal)
  const deltaY = Math.min(vertical, height - vertical)
  return Math.hypot(deltaX, deltaY)
}

function panFor(x: number, width: number): number { return clamp(((x / Math.max(1, width)) * 2 - 1) * .7, -.7, .7) }
function clamp(value: number, minimum: number, maximum: number): number { return Math.min(maximum, Math.max(minimum, value)) }
