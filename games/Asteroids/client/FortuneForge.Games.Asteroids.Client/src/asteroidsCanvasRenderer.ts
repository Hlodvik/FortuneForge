import type { Asteroid, AsteroidsAlienShip, AsteroidsBullet, AsteroidsEnemyBullet, AsteroidsGameState, AsteroidsPowerUp } from './contracts'

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
const alienShipCellWidth = 1086
const alienShipCellHeight = 724

export type AsteroidsSpriteAtlases = Readonly<{
  ship?: HTMLImageElement
  asteroid?: HTMLImageElement
  laserImpact?: HTMLImageElement
  explosion?: HTMLImageElement
  powerUp?: HTMLImageElement
  alienShip?: HTMLImageElement
}>

export type AsteroidsImpact = Readonly<{
  x: number
  y: number
  startedTick: number
  kind: 'hit' | 'destroyed' | 'alien-hit' | 'alien-destroyed'
}>

export function renderAsteroids(context: CanvasRenderingContext2D, game: AsteroidsGameState, atlases: AsteroidsSpriteAtlases = {}, impacts: readonly AsteroidsImpact[] = []): void {
  context.save()
  context.imageSmoothingEnabled = false
  context.clearRect(0, 0, game.width, game.height)
  context.fillStyle = '#070c15'
  context.fillRect(0, 0, game.width, game.height)
  drawStars(context, game.width, game.height)
  for (const asteroid of game.asteroids) {
    const extent = asteroid.radius * 1.18
    drawWrapped(asteroid.x, asteroid.y, extent, extent, game.width, game.height,
      (x, y) => drawAsteroid(context, asteroid, game.tick, x, y, atlases.asteroid))
  }
  for (const powerUp of game.powerUps)
    drawWrapped(powerUp.x, powerUp.y, 24, 24, game.width, game.height,
      (x, y) => drawPowerUp(context, powerUp, x, y, atlases.powerUp))
  for (const bullet of game.bullets)
    drawWrapped(bullet.x, bullet.y, 18, 18, game.width, game.height,
      (x, y) => drawLaser(context, bullet, game.tick, x, y, atlases.laserImpact))
  for (const bullet of game.enemyBullets)
    drawWrapped(bullet.x, bullet.y, 12, 5, game.width, game.height,
      (x, y) => drawEnemyBullet(context, bullet, x, y))
  if (game.alienShip !== null) {
    const displayHeight = alienShipDisplayHeight(game.alienShip)
    const displayWidth = displayHeight * (alienShipCellWidth / alienShipCellHeight)
    drawWrapped(game.alienShip.x, game.alienShip.y, displayWidth / 2, displayHeight / 2, game.width, game.height,
      (x, y) => drawAlienShip(context, game.alienShip!, x, y, atlases.alienShip))
  }
  for (const impact of impacts) {
    const age = game.tick - impact.startedTick
    const isHit = impact.kind === 'hit' || impact.kind === 'alien-hit'
    const lifetime = isHit ? hitFlashLifetimeTicks : impactLifetimeTicks
    const extent = impact.kind === 'alien-destroyed' ? 58 : isHit ? 23 : 48
    if (age >= 0 && age < lifetime) {
      drawWrapped(impact.x, impact.y, extent, extent, game.width, game.height,
        (x, y) => drawImpact(context, impact, age, lifetime, x, y, atlases.explosion, atlases.laserImpact))
    }
  }
  drawWrapped(game.ship.x, game.ship.y, 27, 36, game.width, game.height,
    (x, y) => drawShip(context, game, x, y, atlases.ship))
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

function drawEnemyBullet(context: CanvasRenderingContext2D, bullet: AsteroidsEnemyBullet, x: number, y: number): void {
  const angle = Math.atan2(bullet.velocityY, bullet.velocityX)
  context.save()
  context.translate(x, y)
  context.rotate(angle)
  context.globalCompositeOperation = 'lighter'
  context.globalAlpha = 0.26
  context.fillStyle = '#ff2fa8'
  context.fillRect(-12, -3, 13, 6)
  context.globalAlpha = 0.72
  context.fillStyle = '#ff45c2'
  context.fillRect(-7, -2, 10, 4)
  context.globalAlpha = 1
  context.fillStyle = '#efff9a'
  context.fillRect(0, -1, 5, 2)
  context.restore()
}

function drawAlienShip(context: CanvasRenderingContext2D, alien: AsteroidsAlienShip, x: number, y: number, atlas?: HTMLImageElement): void {
  const frame = alien.type === 'scout' ? 0 : 1
  const displayHeight = alienShipDisplayHeight(alien)
  const displayWidth = displayHeight * (alienShipCellWidth / alienShipCellHeight)
  const facing = alien.velocityX < 0 ? -1 : 1

  context.save()
  context.translate(x, y)
  context.scale(facing, 1)
  if (isReady(atlas)) {
    context.drawImage(
      atlas,
      frame * alienShipCellWidth,
      0,
      alienShipCellWidth,
      alienShipCellHeight,
      -displayWidth / 2,
      -displayHeight / 2,
      displayWidth,
      displayHeight)
    context.restore()
    return
  }

  const fallbackScale = displayHeight / 52
  context.scale(fallbackScale, fallbackScale)
  context.lineWidth = 1.5 / fallbackScale
  if (alien.type === 'scout') drawScoutFallback(context)
  else drawHunterFallback(context)
  context.restore()
}

function drawScoutFallback(context: CanvasRenderingContext2D): void {
  context.fillStyle = '#103b48'
  context.strokeStyle = '#66f4ee'
  context.beginPath()
  context.moveTo(-36, 2)
  context.lineTo(-23, -8)
  context.lineTo(23, -8)
  context.lineTo(36, 2)
  context.lineTo(21, 10)
  context.lineTo(-21, 10)
  context.closePath()
  context.fill()
  context.stroke()
  context.fillStyle = '#31d9e1'
  context.beginPath()
  context.arc(0, -8, 12, Math.PI, Math.PI * 2)
  context.closePath()
  context.fill()
  context.stroke()
  context.fillStyle = '#a9ff3f'
  for (const lightX of [-24, -8, 8, 24]) context.fillRect(lightX - 2, 3, 4, 3)
}

function drawHunterFallback(context: CanvasRenderingContext2D): void {
  context.fillStyle = '#281947'
  context.strokeStyle = '#ff5fd4'
  context.beginPath()
  context.moveTo(37, 0)
  context.lineTo(8, -7)
  context.lineTo(-16, -22)
  context.lineTo(-10, -5)
  context.lineTo(-34, -10)
  context.lineTo(-25, 0)
  context.lineTo(-34, 10)
  context.lineTo(-10, 5)
  context.lineTo(-16, 22)
  context.lineTo(8, 7)
  context.closePath()
  context.fill()
  context.stroke()
  context.fillStyle = '#ff39b8'
  context.fillRect(-18, -2, 38, 4)
  context.fillStyle = '#9aff36'
  context.fillRect(8, -3, 9, 6)
}

function alienShipDisplayHeight(alien: AsteroidsAlienShip): number {
  return alien.radius * (alien.type === 'scout' ? 2.6 : 2.75)
}

function drawImpact(context: CanvasRenderingContext2D, impact: AsteroidsImpact, age: number, lifetime: number, x: number, y: number, explosion?: HTMLImageElement, fallbackAtlas?: HTMLImageElement): void {
  if (impact.kind === 'alien-hit' || impact.kind === 'alien-destroyed') {
    drawAlienImpact(context, impact.kind, age, lifetime, x, y)
    return
  }
  const frame = Math.min(laserFrames - 1, Math.floor((age / lifetime) * laserFrames))
  const displaySize = impact.kind === 'hit' ? 46 : 96
  context.save()
  context.translate(x, y)
  context.rotate(((impact.x + impact.y) * 0.01) % (Math.PI * 2))
  if (isReady(explosion)) context.drawImage(explosion, frame * explosionFrameWidth, 0, explosionFrameWidth, explosionFrameHeight, -displaySize / 2, -displaySize / 2, displaySize, displaySize)
  else if (isReady(fallbackAtlas)) context.drawImage(fallbackAtlas, frame * laserFrameSize, laserFrameSize, laserFrameSize, laserFrameSize, -34, -34, 68, 68)
  context.restore()
}

function drawAlienImpact(context: CanvasRenderingContext2D, kind: 'alien-hit' | 'alien-destroyed', age: number, lifetime: number, x: number, y: number): void {
  const progress = Math.min(1, age / Math.max(1, lifetime - 1))
  const fade = 1 - progress
  const reach = kind === 'alien-hit' ? 12 + progress * 10 : 18 + progress * 36
  const shardCount = kind === 'alien-hit' ? 6 : 12

  context.save()
  context.translate(x, y)
  context.globalCompositeOperation = 'lighter'
  context.globalAlpha = fade
  for (let index = 0; index < shardCount; index++) {
    const angle = (index / shardCount) * Math.PI * 2 + ((x + y) * 0.007)
    const distance = reach * (.38 + ((index * 7) % 11) / 16)
    const size = kind === 'alien-hit' ? 3 : 3 + ((index * 5) % 4)
    context.fillStyle = index % 3 === 0 ? '#efff86' : index % 2 === 0 ? '#63f8ee' : '#ff4fc3'
    context.fillRect(
      Math.round(Math.cos(angle) * distance - size / 2),
      Math.round(Math.sin(angle) * distance - size / 2),
      size,
      size)
  }
  context.globalAlpha = fade * .88
  context.fillStyle = '#ffffff'
  const coreSize = kind === 'alien-hit' ? 8 : Math.max(4, 18 - progress * 12)
  context.fillRect(-coreSize / 2, -coreSize / 2, coreSize, coreSize)
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

function drawWrapped(
  x: number,
  y: number,
  extentX: number,
  extentY: number,
  width: number,
  height: number,
  draw: (wrappedX: number, wrappedY: number) => void,
): void {
  const xs = [x]
  const ys = [y]
  if (x < extentX) xs.push(x + width)
  if (x > width - extentX) xs.push(x - width)
  if (y < extentY) ys.push(y + height)
  if (y > height - extentY) ys.push(y - height)
  for (const wrappedX of xs)
    for (const wrappedY of ys)
      draw(wrappedX, wrappedY)
}

function isReady(image: HTMLImageElement | undefined): image is HTMLImageElement { return image !== undefined && image.complete && image.naturalWidth > 0 }
