import type { AsteroidsAction, AsteroidSize } from './contracts'

export function actionLabel(action: AsteroidsAction): string { return action === 'rotate-left' ? 'Rotate left' : action === 'rotate-right' ? 'Rotate right' : action === 'thrust' ? 'Thrust' : action === 'fire' ? 'Fire' : 'Tick' }
export function sizeLabel(size: AsteroidSize): string { return size[0].toUpperCase() + size.slice(1) }
export function formatScore(score: number): string { return score.toLocaleString() }
