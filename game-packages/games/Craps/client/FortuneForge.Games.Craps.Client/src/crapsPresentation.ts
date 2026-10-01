import type { CrapsExtraBetKind, CrapsExtraBetRequest, CrapsRollResult, CrapsRound, CrapsStatus } from './contracts'

export type { CrapsExtraBet, CrapsExtraBetKind, CrapsExtraBetRequest, CrapsOutcome, CrapsPhase, CrapsRoll, CrapsRollResult, CrapsRound, CrapsStatus } from './contracts'

export type CrapsExtraBetOption = Readonly<{
  kind: CrapsExtraBetRequest['kind']
  label: string
  coverageText: string
  payout: string
}>

export type CrapsRoundTotals = Readonly<{ stake: number; returned: number; net: number | null; workingStake: number }>

export const pointNumbers = [4, 5, 6, 8, 9, 10] as const

export const extraBetOptions: readonly CrapsExtraBetOption[] = [
  { kind: 'field', label: 'Field', coverageText: '2, 3, 4, 9, 10, 11, 12', payout: '1:1 · 2 and 12 pay 2:1' },
  { kind: 'any-seven', label: 'Any Seven', coverageText: '7', payout: '4:1' },
  { kind: 'any-craps', label: 'Any Craps', coverageText: '2, 3, 12', payout: '7:1' },
]

/** Check the draft as entered; never change a wager or apply a wallet limit. */
export function validStake(value: number, status: CrapsStatus | null): boolean {
  if (!status?.available) return false
  const stake = moneyCents(value)
  const minimum = moneyCents(status.minimumStake)
  const maximum = moneyCents(status.maximumStake)
  const increment = moneyCents(status.stakeIncrement)
  return stake !== null && minimum !== null && maximum !== null && increment !== null
    && minimum > 0 && maximum >= minimum && increment > 0
    && stake >= minimum && stake <= maximum && (stake - minimum) % increment === 0
}

/** Offer familiar denominations and the first/last wager allowed by the table. */
export function availableChipValues(status: CrapsStatus | null): number[] {
  if (!status?.available) return []
  const minimum = moneyCents(status.minimumStake)
  const maximum = moneyCents(status.maximumStake)
  const increment = moneyCents(status.stakeIncrement)
  if (minimum === null || maximum === null || increment === null || minimum <= 0 || maximum < minimum || increment <= 0) return []
  const last = minimum + Math.floor((maximum - minimum) / increment) * increment
  return [...new Set([minimum / 100, last / 100, 1, 5, 10, 25, 50, 100])]
    .filter(value => validStake(value, status))
    .sort((left, right) => left - right)
}

export function extraBetLabel(kind: CrapsExtraBetKind): string {
  return kind === 'odds' ? 'Pass Odds' : extraBetOptions.find(option => option.kind === kind)!.label
}

/** Profit ratios describe the server's rules; settlement amounts remain authoritative. */
export function oddsCopy(point: number | null): string {
  if (point === 4 || point === 10) return 'Pays 2:1'
  if (point === 5 || point === 9) return 'Pays 3:2'
  if (point === 6 || point === 8) return 'Pays 6:5'
  return ''
}

/** Aggregate server amounts for display without resolving any bet locally. */
export function roundTotals(round: CrapsRound): CrapsRoundTotals {
  const extras = round.extraBets ?? []
  const stakeCents = Math.round(round.stake * 100) + sumCents(extras.map(bet => bet.stake))
  const returnedCents = Math.round((round.lastOutcome?.totalReturn ?? 0) * 100)
    + sumCents(extras.filter(bet => bet.resolved).map(bet => bet.totalReturn ?? 0))
  const workingCents = (round.phase === 'resolved' ? 0 : Math.round(round.stake * 100))
    + sumCents(extras.filter(bet => !bet.resolved).map(bet => bet.stake))
  return {
    stake: stakeCents / 100,
    returned: returnedCents / 100,
    net: workingCents === 0 ? (returnedCents - stakeCents) / 100 : null,
    workingStake: workingCents / 100,
  }
}

export function resultLabel(round: CrapsRound | null): string {
  if (!round) return 'Place your Pass Line bet'
  if (round.phase === 'come-out') return 'Come-out ready'
  const outcome = round.lastOutcome
  if (round.phase === 'point') {
    return outcome?.result === 'no-decision'
      ? `${outcome.total} rolled · Point ${round.point} holds`
      : outcome?.result === 'point-established'
        ? `Point ${round.point} established`
        : `Point ${round.point} is on`
  }
  switch (outcome?.result) {
    case 'natural-win': return `Natural ${outcome.total} · Pass Line wins`
    case 'craps-loss': return `Craps ${outcome.total} · Pass Line loses`
    case 'point-hit': return `Point ${round.point} made · Pass Line wins`
    case 'seven-out': return 'Seven-out · Pass Line loses'
    default: return 'Hand complete'
  }
}

export function rollResultLabel(result: CrapsRollResult | null): string {
  switch (result) {
    case 'natural-win': return 'Natural'
    case 'craps-loss': return 'Craps'
    case 'point-established': return 'Point established'
    case 'point-hit': return 'Point made'
    case 'seven-out': return 'Seven-out'
    case 'no-decision': return 'No decision'
    default: return 'Roll'
  }
}

function sumCents(values: readonly number[]): number { return values.reduce((total, value) => total + Math.round(value * 100), 0) }

function moneyCents(value: number): number | null {
  if (!Number.isFinite(value)) return null
  const cents = Math.round(value * 100)
  return Number.isSafeInteger(cents) && Math.abs(value * 100 - cents) < .000001 ? cents : null
}
