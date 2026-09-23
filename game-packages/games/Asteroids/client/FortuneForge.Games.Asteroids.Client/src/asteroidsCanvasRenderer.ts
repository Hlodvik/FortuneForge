import type { Asteroid, AsteroidsBullet, AsteroidsGameState, AsteroidsPowerUp } from './contracts'

const asteroidCellSize = 434
const shipFrameWidth = 272
const shipFrameHeight = 362
const laserFrameSize = 181
const asteroidVariants = 7
const laserFrames = 6
const impactLifetimeTicks = 18
const hitFlashLifetimeTicks = 7
const explosionFrameWidth = 362
const explosionFrameHeight = 724
const powerUpCellSize = 724

export type AsteroidsSpriteAtlases = Readonly<{
  ship?: HTMLImageElement
  asteroid?: HTMLImageElement
  laserImpact?: HTMLImageElement
  explosion?: HTMLImageElement
  powerUp?: HTMLImageElement
}>

export type AsteroidsImpact = Readonly<{ x: number; y: number; startedTick: number; kind: 'hit' | 'destroyed' }>

export function renderAsteroids(context: CanvasRenderingContext2D, game: AsteroidsGameState, atlases: AsteroidsSpriteAtlases = {}, impacts: readonly AsteroidsImpact[] = []): void {
  context.save()
  context.imageSmoothingEnabled = false
  context.clearRect(0, 0, game.width, game.height)
  context.fillStyle = '#070c15'
  context.fillRect(0, 0, game.width, game.height)
  drawStars(context, game.width, game.height)
  for (const asteroid of game.asteroids) {
    drawAsteroid(context, asteroid, game.tick, asteroid.x, asteroid.y, atlases.asteroid)
    if (asteroid.kind === 'hunter') drawHunterCue(context, asteroid)
  }
  for (const bullet of game.bullets)
    drawLaser(context, bullet, game.tick, bullet.x, bullet.y, atlases.laserImpact)
  for (const powerUp of game.powerUps)
    drawPowerUp(context, powerUp, powerUp.x, powerUp.y, atlases.powerUp)
  for (const impact of impacts) {
    const age = game.tick - impact.startedTick
    const lifetime = impact.kind === 'hit' ? hitFlashLifetimeTicks : impactLifetimeTicks
    if (age >= 0 && age < lifetime)
      drawImpact(context, impact, age, lifetime, impact.x, impact.y, atlases.explosion, atlases.laserImpact)
  }
  drawShip(context, game, game.ship.x, game.ship.y, atlases.ship)
  context.restore()
}

function drawStars(context: CanvasRenderingContext2D, width: number, height: number): void {
  context.fillStyle = '#a7b4ce'
  for (let index = 0; index < 70; index++) {
    const x = (index * 149.37) % width
    const y = (index * 83.91) % height
    context.globalAlpha = index % 4 === 0 ? 0.72 : 0.3
    context.fillRect(x, y, 1, 1)
  }
  context.globalAlpha = 1
}

function drawAsteroid(context: CanvasRenderingContext2D, asteroid: Asteroid, tick: number, x: number, y: number, atlas?: HTMLImageElement): void {
  if (isReady(atlas)) {
    const variant = asteroid.spriteVariant % asteroidVariants
    const displaySize = asteroid.radius * 2.35
    context.save()
    context.translate(x, y)
    context.rotate((asteroid.id * 0.618 + tick * 0.015) % (Math.PI * 2))
    context.drawImage(atlas, variant * asteroidCellSize, 0, asteroidCellSize, asteroidCellSize, -displaySize / 2, -displaySize / 2, displaySize, displaySize)
    context.restore()
    return
  }

  context.save()
  context.translate(x, y)
  context.fillStyle = '#596273'
  context.strokeStyle = '#aab4c4'
  context.lineWidth = 1.5
  context.beginPath()
  for (let index = 0; index < 9; index++) {
    const angle = (index / 9) * Math.PI * 2
    const wobble = 0.78 + (((asteroid.id * 17 + index * 11) % 23) / 100)
    const px = Math.cos(angle) * asteroid.radius * wobble
    const py = Math.sin(angle) * asteroid.radius * wobble
    if (index === 0) context.moveTo(px, py)
    else context.lineTo(px, py)
  }
  context.closePath()
  context.fill()
  context.stroke()
  context.fillStyle = '#252d3a'
  for (let index = 0; index < 3; index++) {
    const angle = (asteroid.id * 0.37) + (index * 2.1)
    const distance = asteroid.radius * (0.22 + (index * 0.13))
    context.beginPath()
    context.arc(Math.cos(angle) * distance, Math.sin(angle) * distance, asteroid.radius * (0.1 + (index * 0.035)), 0, Math.PI * 2)
    context.fill()
  }
  context.restore()
}

function drawHunterCue(context: CanvasRenderingContext2D, asteroid: Asteroid): void {
  const heading = Math.atan2(asteroid.velocityY, asteroid.velocityX)
  context.save()
  context.translate(asteroid.x, asteroid.y)
  context.strokeStyle = '#ff6688'
  context.lineWidth = 3
  context.beginPath()
  context.arc(0, 0, asteroid.radius + 7, 0, Math.PI * 2)
  context.stroke()
  context.rotate(heading)
  context.beginPath()
  context.moveTo(asteroid.radius + 13, 0)
  context.lineTo(asteroid.radius + 6, -6)
  context.moveTo(asteroid.radius + 13, 0)
  context.lineTo(asteroid.radius + 6, 6)
  context.stroke()
  context.restore()
}

function drawLaser(context: CanvasRenderingContext2D, bullet: AsteroidsBullet, tick: number, x: number, y: number, atlas?: HTMLImageElement): void {
  if (isReady(atlas)) {
    const frame = (bullet.id + tick) % laserFrames
    const angle = Math.atan2(bullet.velocityY, bullet.velocityX)
    context.save()
    context.translate(x, y)
    context.rotate(angle)
    context.drawImage(atlas, frame * laserFrameSize, 0, laserFrameSize, laserFrameSize, -18, -18, 36, 36)
    context.restore()
    return
  }

  context.fillStyle = '#f3df9c'
  context.beginPath()
  context.arc(x, y, 2, 0, Math.PI * 2)
  context.fill()
}

function drawImpact(context: CanvasRenderingContext2D, impact: AsteroidsImpact, age: number, lifetime: number, x: number, y: number, explosion?: HTMLImageElement, fallbackAtlas?: HTMLImageElement): void {
  const frame = Math.min(laserFrames - 1, Math.floor((age / lifetime) * laserFrames))
  const displaySize = impact.kind === 'hit' ? 46 : 96
  context.save()
  context.translate(x, y)
  context.rotate(((impact.x + impact.y) * 0.01) % (Math.PI * 2))
  if (isReady(explosion)) context.drawImage(explosion, frame * explosionFrameWidth, 0, explosionFrameWidth, explosionFrameHeight, -displaySize / 2, -displaySize / 2, displaySize, displaySize)
  else if (isReady(fallbackAtlas)) context.drawImage(fallbackAtlas, frame * laserFrameSize, laserFrameSize, laserFrameSize, laserFrameSize, -34, -34, 68, 68)
  context.restore()
}

function drawPowerUp(context: CanvasRenderingContext2D, powerUp: AsteroidsPowerUp, x: number, y: number, atlas?: HTMLImageElement): void {
  if (isReady(atlas)) {
    const frame = powerUp.type === 'shield' ? 0 : powerUp.type === 'rapid-fire' ? 1 : 2
    context.drawImage(atlas, frame * powerUpCellSize, 0, powerUpCellSize, powerUpCellSize, x - 24, y - 24, 48, 48)
    return
  }

  context.fillStyle = powerUp.type === 'shield' ? '#6ce8ff' : powerUp.type === 'rapid-fire' ? '#ffaf43' : '#8af57a'
  context.beginPath()
  context.arc(x, y, 10, 0, Math.PI * 2)
  context.fill()
}

function drawShip(context: CanvasRenderingContext2D, game: AsteroidsGameState, x: number, y: number, atlas?: HTMLImageElement): void {
  if (isReady(atlas)) {
    const frame = game.ship.invulnerabilityTicks > 0 ? 3 : game.ship.thrustTicks > 0 ? 1 : 0
    context.save()
    context.translate(x, y)
    context.rotate(game.ship.angle)
    context.drawImage(atlas, frame * shipFrameWidth, 0, shipFrameWidth, shipFrameHeight, -27, -36, 54, 72)
    context.restore()
    return
  }

  context.save()
  context.translate(x, y)
  context.rotate(game.ship.angle)
  context.strokeStyle = game.ship.invulnerabilityTicks > 0 ? '#f2d677' : '#eff5ff'
  context.lineWidth = 2
  context.beginPath()
  context.moveTo(18, 0)
  context.lineTo(-12, -10)
  context.lineTo(-7, 0)
  context.lineTo(-12, 10)
  context.closePath()
  context.stroke()
  if (game.ship.velocityX !== 0 || game.ship.velocityY !== 0) {
    context.strokeStyle = '#e88456'
    context.beginPath()
    context.moveTo(-10, -4)
    context.lineTo(-18, 0)
    context.lineTo(-10, 4)
    context.stroke()
  }
  context.restore()
}

function isReady(image: HTMLImageElement | undefined): image is HTMLImageElement { return image !== undefined && image.complete && image.naturalWidth > 0 }
