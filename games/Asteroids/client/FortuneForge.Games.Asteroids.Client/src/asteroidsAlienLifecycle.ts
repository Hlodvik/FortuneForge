import type { AsteroidsAlienShip } from './contracts'

type AlienRemovalField = Readonly<{ width: number }>

/**
 * An alien can disappear because it was destroyed, crossed the horizontal
 * boundary, or exhausted its visit timer. Only the first case should produce
 * an explosion or destruction cue.
 */
export function wasAlienDestroyed(alien: AsteroidsAlienShip, field: AlienRemovalField): boolean {
  if (alien.remainingTicks <= 1) return false
  const nextX = alien.x + alien.velocityX
  const exitedRight = alien.velocityX > 0 && nextX > field.width + alien.radius
  const exitedLeft = alien.velocityX < 0 && nextX < -alien.radius
  return !exitedRight && !exitedLeft
}
