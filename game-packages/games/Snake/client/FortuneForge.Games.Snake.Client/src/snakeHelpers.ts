import type { SnakeDirection, SnakePoint } from './contracts'

export function pointKey(point: SnakePoint): string { return `${point.x}:${point.y}` }
export function directionLabel(direction: SnakeDirection): string { return direction[0].toUpperCase() + direction.slice(1) }
export function cellClass(isHead: boolean, isBody: boolean, isFood: boolean): string { return isHead ? 'snake-head' : isBody ? 'snake-body' : isFood ? 'snake-food' : 'snake-empty' }
