import type { RouletteBet, RouletteBetKind, RouletteRound, RouletteSettlement, RouletteStatus } from './contracts'
import { pocketColor } from './rouletteHelpers'

export type RouletteBetRequest = Omit<RouletteBet, 'betIndex' | 'playerId'>
export type RouletteBetOption = Readonly<{ kind: RouletteBetKind; label: string; count: number; payout: string }>

export const betOptions: readonly RouletteBetOption[] = [
  { kind: 'straight', label: 'Straight-up', count: 1, payout: '35:1' },
  { kind: 'split', label: 'Split', count: 2, payout: '17:1' },
  { kind: 'street', label: 'Street', count: 3, payout: '11:1' },
  { kind: 'corner', label: 'Corner', count: 4, payout: '8:1' },
  { kind: 'six-line', label: 'Six-line', count: 6, payout: '5:1' },
  { kind: 'column', label: 'Column', count: 1, payout: '2:1' },
  { kind: 'dozen', label: 'Dozen', count: 1, payout: '2:1' },
  { kind: 'red', label: 'Red', count: 0, payout: '1:1' },
  { kind: 'black', label: 'Black', count: 0, payout: '1:1' },
  { kind: 'even', label: 'Even', count: 0, payout: '1:1' },
  { kind: 'odd', label: 'Odd', count: 0, payout: '1:1' },
  { kind: 'low', label: '1–18', count: 0, payout: '1:1' },
  { kind: 'high', label: '19–36', count: 0, payout: '1:1' },
]

export const europeanWheelOrder = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26] as const

export function neighborPockets(center: number, depth: number): number[] {
  const index = europeanWheelOrder.indexOf(center as typeof europeanWheelOrder[number])
  if (index < 0 || !Number.isInteger(depth) || depth < 0 || depth > 18) return []
  return Array.from({ length: depth * 2 + 1 }, (_, offset) =>
    europeanWheelOrder[(index - depth + offset + europeanWheelOrder.length) % europeanWheelOrder.length]!)
}

/** Validate presentation choices against the server's single-zero table geometry. */
export function legalSelection(kind: RouletteBetKind, values: readonly number[]): boolean {
  const option = betOptions.find(item => item.kind === kind)
  if (!option || values.length !== option.count || !values.every(isPocket) || new Set(values).size !== values.length) return false
  if (kind === 'column' || kind === 'dozen') return values[0]! >= 1 && values[0]! <= 3
  if (kind === 'straight' || option.count === 0) return true

  const ordered = [...values].sort((left, right) => left - right)
  // C# uses truncation for (number - 1) / 3, including zero's special row.
  const rows = new Set(ordered.map(number => Math.trunc((number - 1) / 3)))
  const columns = new Set(ordered.map(number => (number - 1) % 3))
  switch (kind) {
    case 'split':
      return ordered[0] === 0
        ? ordered[1]! >= 1 && ordered[1]! <= 3
        : rows.size === 1 && columns.size === 2 && ordered[1]! - ordered[0]! === 1
          || columns.size === 1 && rows.size === 2 && ordered[1]! - ordered[0]! === 3
    case 'street':
      return ordered[0] === 0
        ? ordered[1] === 1 && ordered[2] === 2 || ordered[1] === 2 && ordered[2] === 3
        : rows.size === 1 && columns.size === 3
    case 'corner':
      return rows.size === 2 && columns.size === 2 && ordered[1]! - ordered[0]! === 1
        && ordered[2]! - ordered[0]! === 3 && ordered[3]! - ordered[2]! === 1
    case 'six-line':
      return rows.size === 2 && columns.size === 3 && ordered[3]! - ordered[0]! === 3
        && ordered[5]! - ordered[2]! === 3
    default:
      return false
  }
}

export function makeBet(kind: RouletteBetKind, values: readonly number[], stake: number): RouletteBetRequest {
  if (!legalSelection(kind, values)) throw new RangeError('Choose a valid Roulette layout.')
  const cents = moneyCents(stake)
  if (cents === null || cents <= 0) throw new RangeError('Choose a positive Roulette chip value.')
  const scalar = kind === 'straight' || kind === 'column' || kind === 'dozen'
  return {
    kind,
    stake: cents / 100,
    number: scalar ? values[0]! : null,
    numbers: scalar ? [] : [...values].sort((left, right) => left - right),
  }
}

export function coveredPockets(bet: RouletteBetRequest): number[] {
  const selection = bet.kind === 'straight' || bet.kind === 'column' || bet.kind === 'dozen'
    ? bet.number === null ? [] : [bet.number]
    : bet.numbers
  if (!legalSelection(bet.kind, selection)) return []
  if (bet.kind === 'straight') return [bet.number!]
  if (bet.kind === 'split' || bet.kind === 'street' || bet.kind === 'corner' || bet.kind === 'six-line') return [...bet.numbers].sort((left, right) => left - right)
  const pockets = Array.from({ length: 36 }, (_, index) => index + 1)
  switch (bet.kind) {
    case 'column': return pockets.filter(number => number % 3 === bet.number! % 3)
    case 'dozen': return pockets.filter(number => number >= bet.number! * 12 - 11 && number <= bet.number! * 12)
    case 'red': return pockets.filter(number => pocketColor(number) === 'red')
    case 'black': return pockets.filter(number => pocketColor(number) === 'black')
    case 'even': return pockets.filter(number => number % 2 === 0)
    case 'odd': return pockets.filter(number => number % 2 !== 0)
    case 'low': return pockets.filter(number => number <= 18)
    case 'high': return pockets.filter(number => number >= 19)
  }
}

export function formatBetLabel(bet: RouletteBetRequest): string {
  const option = betOptions.find(item => item.kind === bet.kind)
  if (bet.kind === 'straight') return `Number ${bet.number ?? '—'}`
  if (bet.kind === 'column') return `Column ${bet.number ?? '—'}`
  if (bet.kind === 'dozen') return `Dozen ${bet.number ?? '—'}`
  if (option && option.count > 1) return `${option.label} · ${[...bet.numbers].sort((left, right) => left - right).join(', ')}`
  return option?.label ?? 'Roulette bet'
}

export function validStake(value: number, status: RouletteStatus | null, balance: number): boolean {
  if (!status?.available) return false
  const stake = moneyCents(value)
  const minimum = moneyCents(status.minimumStake)
  const maximum = moneyCents(status.maximumStake)
  const increment = moneyCents(status.stakeIncrement)
  const available = moneyCents(balance)
  return stake !== null && minimum !== null && maximum !== null && increment !== null && available !== null
    && minimum > 0 && maximum >= minimum && increment > 0 && available >= 0
    && stake >= minimum && stake <= maximum && stake <= available && (stake - minimum) % increment === 0
}

/** Keep familiar denominations while including the actual table endpoints. */
export function availableChipValues(status: RouletteStatus | null): number[] {
  if (!status?.available) return []
  const minimum = moneyCents(status.minimumStake)
  const maximum = moneyCents(status.maximumStake)
  const increment = moneyCents(status.stakeIncrement)
  if (minimum === null || maximum === null || increment === null || minimum <= 0 || maximum < minimum || increment <= 0) return []
  const last = minimum + Math.floor((maximum - minimum) / increment) * increment
  return [...new Set([minimum / 100, last / 100, 1, 5, 10, 25, 50, 100])]
    .filter(value => validStake(value, status, status.maximumStake))
    .sort((left, right) => left - right)
}

export function totalStake(bets: readonly Pick<RouletteBet, 'stake'>[]): number {
  return bets.reduce((sum, bet) => sum + Math.round(bet.stake * 100), 0) / 100
}

export function totalReturn(settlements: readonly Pick<RouletteSettlement, 'totalReturn'>[]): number {
  return settlements.reduce((sum, settlement) => sum + Math.round(settlement.totalReturn * 100), 0) / 100
}

/** Aggregate server settlement amounts for display; this does not settle bets. */
export function roundTotals(round: RouletteRound): Readonly<{ stake: number; totalReturn: number; net: number }> {
  const stake = totalStake(round.bets)
  const returned = totalReturn(round.settlements)
  return { stake, totalReturn: returned, net: Math.round((returned - stake) * 100) / 100 }
}

function isPocket(value: number): boolean { return Number.isInteger(value) && value >= 0 && value <= 36 }
function moneyCents(value: number): number | null {
  if (!Number.isFinite(value)) return null
  const cents = Math.round(value * 100)
  return Number.isSafeInteger(cents) && Math.abs(value * 100 - cents) < .000001 ? cents : null
}
