import type { GridPosition, PaylinePayout } from '../types/slots'

export const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
export const natural = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
export const positive = (value: unknown): value is number => natural(value) && value > 0
export const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0
export function grid<T = string>(value: unknown, columns: number, rows: number, cell: (value: unknown) => boolean): value is T[][] {
  return Array.isArray(value) && value.length === columns && value.every(column => Array.isArray(column) && column.length === rows && column.every(cell))
}
export function position(value: unknown, reels: readonly (readonly unknown[])[]): value is GridPosition {
  return record(value) && natural(value.reel) && natural(value.row) && value.reel < reels.length && value.row < reels[value.reel].length
}
/** Checks display coordinates and totals, never authorizes a wager or calculates its award. */
export function paylines(value: unknown, reels: readonly (readonly unknown[])[], total: number): value is PaylinePayout[] {
  return Array.isArray(value) && value.every(line => record(line) && natural(line.paylineId) && natural(line.amountPoints) &&
    Array.isArray(line.matches) && line.matches.every(paid => record(paid) && natural(paid.amountPoints) && record(paid.match) &&
      Array.isArray(paid.match.positions) && paid.match.positions.every(cell => position(cell, reels)))) &&
    value.reduce((sum, line) => sum + line.amountPoints, 0) === total
}
