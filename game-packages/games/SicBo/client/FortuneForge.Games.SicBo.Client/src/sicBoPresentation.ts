import type { SicBoBetKind, SicBoBetRequest, SicBoRound, SicBoSettlement, SicBoStatus } from './contracts'

export type { SicBoBetKind, SicBoBetRequest, SicBoRound, SicBoSettlement, SicBoStatus } from './contracts'

export type SicBoBetTarget = Readonly<Omit<SicBoBetRequest, 'stake'> & {
  id: string
  label: string
  odds: string
  coverageText: string
  profitOdds: readonly number[]
}>
export type SicBoPendingRoll = Readonly<{
  scope: string
  idempotencyKey: string
  bets: readonly SicBoBetRequest[]
  roundId?: string
}>
export type SicBoHistoryEntry = SicBoRound
export type SicBoHistoryEnvelope = Readonly<{ scope: string; rounds: readonly SicBoHistoryEntry[] }>

const emptySelection = { face: null, total: null, firstFace: null, secondFace: null } as const
const totalOdds: Readonly<Record<number, number>> = { 4: 64, 5: 32, 6: 19, 7: 12, 8: 8.5, 9: 7, 10: 6.5, 11: 6.5, 12: 7, 13: 8.5, 14: 12, 15: 19, 16: 32, 17: 64 }
const faces = [1, 2, 3, 4, 5, 6] as const

function target(kind: SicBoBetKind, label: string, coverageText: string, profitOdds: readonly number[], selection = emptySelection as Omit<SicBoBetRequest, 'kind' | 'stake'>): SicBoBetTarget {
  const suffix = selection.face ?? selection.total ?? (selection.firstFace === null ? null : `${selection.firstFace}-${selection.secondFace}`)
  return { id: suffix === null ? kind : `${kind}-${suffix}`, kind, label, coverageText, profitOdds: [...profitOdds], odds: profitOdds.map(value => `${value}:1`).join(' / '), ...selection }
}

/** This is display coverage and the selected server paytable, never local settlement. */
export const betTargets: readonly SicBoBetTarget[] = [
  target('small', 'Small', 'Total 4–10 · triples lose', [1]),
  target('big', 'Big', 'Total 11–17 · triples lose', [1]),
  target('odd', 'Odd', 'Odd total · triples lose', [1]),
  target('even', 'Even', 'Even total · triples lose', [1]),
  target('any-triple', 'Any Triple', 'Any three alike', [32]),
  ...faces.map(face => target('single-number', `Single ${face}`, 'One / two / three appearances', [1, 2, 12], { ...emptySelection, face })),
  ...faces.map(face => target('specific-double', `Double ${face}`, 'Two or three appearances', [11.5], { ...emptySelection, face })),
  ...faces.map(face => target('specific-triple', `Triple ${face}`, 'Three appearances', [195], { ...emptySelection, face })),
  ...Array.from({ length: 14 }, (_, index) => index + 4).map(total => target('total', `Total ${total}`, `Dice total ${total}`, [totalOdds[total]!], { ...emptySelection, total })),
  ...faces.flatMap(firstFace => faces.filter(secondFace => secondFace > firstFace).map(secondFace =>
    target('two-number-combination', `Combination ${firstFace} + ${secondFace}`, 'Both faces appear', [6], { ...emptySelection, firstFace, secondFace }))),
]

export function targetsForKind(kind: SicBoBetKind): readonly SicBoBetTarget[] { return betTargets.filter(item => item.kind === kind) }

export function createTargetBet(targetOrId: SicBoBetTarget | string, stake: number): SicBoBetRequest | null {
  const chosen = betTargets.find(item => item.id === (typeof targetOrId === 'string' ? targetOrId : targetOrId.id))
  const cents = moneyCents(stake)
  if (!chosen || cents === null || cents <= 0) return null
  return { kind: chosen.kind, stake: cents / 100, face: chosen.face, total: chosen.total, firstFace: chosen.firstFace, secondFace: chosen.secondFace }
}

export function betLabel(bet: SicBoBetRequest): string {
  return betTargets.find(item => sameSelection(item, bet))?.label ?? 'Sic Bo bet'
}

/** Read the exact plain-decimal draft, including the table's minimum-anchored step. */
export function parseStake(raw: string, status: SicBoStatus | null): number | null {
  const text = raw.trim()
  if (!/^(?:\d+(?:\.\d{1,2})?|\.\d{1,2})$/.test(text)) return null
  const value = Number(text)
  return validStake(value, status) ? value : null
}

export function validStake(value: number, status: SicBoStatus | null): boolean {
  if (!status?.available) return false
  const cents = moneyCents(value)
  const limits = stakeLimits(status)
  return cents !== null && limits !== null && cents >= limits.minimum && cents <= limits.maximum
    && (cents - limits.minimum) % limits.increment === 0
}

export function availableChipValues(status: SicBoStatus | null): number[] {
  if (!status?.available) return []
  const limits = stakeLimits(status)
  if (!limits) return []
  const last = limits.minimum + Math.floor((limits.maximum - limits.minimum) / limits.increment) * limits.increment
  return [...new Set([limits.minimum / 100, last / 100, 1, 5, 10, 25, 50, 100])]
    .filter(value => validStake(value, status)).sort((left, right) => left - right)
}

/** Sum integer cents, avoiding binary-float accumulation and silent rounding. */
export function totalStake(bets: readonly Pick<SicBoBetRequest, 'stake'>[]): number {
  let total = 0
  for (const bet of bets) {
    const cents = moneyCents(bet.stake)
    if (cents === null || cents <= 0 || !Number.isSafeInteger(total + cents)) return NaN
    total += cents
  }
  return total / 100
}

export function legalSlip(bets: readonly SicBoBetRequest[], status: SicBoStatus | null, balance: number): boolean {
  const available = moneyCents(balance)
  const total = moneyCents(totalStake(bets))
  return status?.available === true && Number.isSafeInteger(status.maximumBetsPerRound) && status.maximumBetsPerRound >= 1
    && bets.length >= 1 && bets.length <= status.maximumBetsPerRound && available !== null && available >= 0
    && bets.every(bet => validBetShape(bet) && validStake(bet.stake, status))
    && total !== null && total <= available
}

export function validBetShape(value: unknown): value is SicBoBetRequest {
  if (!isRecord(value) || typeof value.kind !== 'string' || typeof value.stake !== 'number') return false
  const stake = moneyCents(value.stake)
  return stake !== null && stake > 0 && betTargets.some(item => sameSelection(item, value))
}

export function pendingStorageKey(scope: string): string { return `fortuneforge:sic-bo:pending:${encodeURIComponent(scope)}` }
export function historyStorageKey(scope: string): string { return `fortuneforge:sic-bo:history:${encodeURIComponent(scope)}` }

/** Pending records retain the exact ordered slip and key, including duplicate bets. */
export function parsePendingRoll(value: unknown, scope: string): SicBoPendingRoll | null {
  if (!validScope(scope) || !isRecord(value) || value.scope !== scope || !validRequestKey(value.idempotencyKey)
    || !Array.isArray(value.bets) || value.bets.length < 1 || value.bets.length > 20
    || !value.bets.every(validStoredBet) || value.roundId !== undefined && !validRoundId(value.roundId)) return null
  return { scope, idempotencyKey: value.idempotencyKey, bets: value.bets.map(copyBet), ...(value.roundId === undefined ? {} : { roundId: value.roundId as string }) }
}

/** Storage is optional display history; malformed records cannot become a wager. */
export function parseRollHistory(value: unknown, scope: string): readonly SicBoHistoryEntry[] {
  if (!validScope(scope) || !isRecord(value) || value.scope !== scope || !Array.isArray(value.rounds)) return []
  const result: SicBoHistoryEntry[] = []
  const seen = new Set<string>()
  for (const item of value.rounds) {
    const round = parseStoredRound(item)
    if (!round || seen.has(round.roundId)) continue
    seen.add(round.roundId)
    result.push(round)
    if (result.length === 12) break
  }
  return result
}

export function appendRollHistory(history: readonly SicBoHistoryEntry[], round: SicBoRound): readonly SicBoHistoryEntry[] {
  const accepted = parseStoredRound(round)
  if (!accepted) return history
  const scope = 'append-history'
  return parseRollHistory({ scope, rounds: [accepted, ...history] }, scope)
}

function parseStoredRound(value: unknown): SicBoRound | null {
  if (!isRecord(value) || !validRoundId(value.roundId) || value.phase !== 'settled' || !isDice(value.dice)
    || value.total !== value.dice.reduce((sum, die) => sum + die, 0)
    || value.isTriple !== (value.dice[0] === value.dice[1] && value.dice[1] === value.dice[2])
    || !isNonNegativeMoney(value.balance) || !isNonNegativeMoney(value.totalStaked) || !isNonNegativeMoney(value.totalReturn)
    || typeof value.profit !== 'number' || moneyCents(value.profit) === null || !Array.isArray(value.settlements)
    || value.settlements.length < 1 || value.settlements.length > 20) return null
  const settlements: SicBoSettlement[] = []
  for (let index = 0; index < value.settlements.length; index++) {
    const item = value.settlements[index]
    if (!isRecord(item)) return null
    const bet = validStoredBet(item) ? item : null
    if (!bet || item.betIndex !== index || typeof item.won !== 'boolean'
      || typeof item.profitOdds !== 'number' || !Number.isFinite(item.profitOdds)
      || typeof item.profit !== 'number' || moneyCents(item.profit) === null || !isNonNegativeMoney(item.totalReturn)) return null
    const stakeCents = moneyCents(bet.stake)!
    const profitCents = moneyCents(item.profit)!
    const returnCents = moneyCents(item.totalReturn)!
    const allowedOdds = betTargets.find(target => sameSelection(target, bet))!.profitOdds
    if (item.won
      ? !allowedOdds.includes(item.profitOdds) || profitCents !== stakeCents * item.profitOdds || returnCents !== stakeCents + profitCents
      : item.profitOdds !== 0 || profitCents !== -stakeCents || returnCents !== 0) return null
    settlements.push({ ...copyBet(bet), betIndex: index, won: item.won, profitOdds: item.profitOdds, profit: item.profit, totalReturn: item.totalReturn })
  }
  const staked = settlements.reduce((sum, item) => sum + moneyCents(item.stake)!, 0)
  const returned = settlements.reduce((sum, item) => sum + moneyCents(item.totalReturn)!, 0)
  if (moneyCents(value.totalStaked)! !== staked || moneyCents(value.totalReturn)! !== returned || moneyCents(value.profit)! !== returned - staked) return null
  return { roundId: value.roundId, phase: 'settled', balance: value.balance, dice: [...value.dice], total: value.total as number,
    isTriple: value.isTriple as boolean, totalStaked: value.totalStaked, totalReturn: value.totalReturn, profit: value.profit, settlements }
}

function validStoredBet(value: unknown): value is SicBoBetRequest {
  return validBetShape(value) && Number.isInteger(value.stake) && value.stake >= 1 && value.stake <= 100
}
function copyBet(bet: SicBoBetRequest): SicBoBetRequest { return { kind: bet.kind, stake: bet.stake, face: bet.face, total: bet.total, firstFace: bet.firstFace, secondFace: bet.secondFace } }
function sameSelection(target: Omit<SicBoBetRequest, 'stake'>, value: Record<string, unknown> | Omit<SicBoBetRequest, 'stake'>): boolean {
  return target.kind === value.kind && target.face === value.face && target.total === value.total && target.firstFace === value.firstFace && target.secondFace === value.secondFace
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null && !Array.isArray(value) }
function isDice(value: unknown): value is SicBoRound['dice'] { return Array.isArray(value) && value.length === 3 && value.every(die => Number.isInteger(die) && die >= 1 && die <= 6) }
function validRoundId(value: unknown): value is string { return typeof value === 'string' && /^[\da-f]{64}$/.test(value) }
function validRequestKey(value: unknown): value is string { return typeof value === 'string' && /^[A-Za-z0-9_-]{16,128}$/.test(value) }
function validScope(value: string): boolean { return value.trim().length > 0 }
function isNonNegativeMoney(value: unknown): value is number { return typeof value === 'number' && value >= 0 && moneyCents(value) !== null }
function moneyCents(value: number): number | null {
  if (!Number.isFinite(value)) return null
  const scaled = value * 100
  const cents = Math.round(scaled)
  return Number.isSafeInteger(cents) && Math.abs(scaled - cents) <= Number.EPSILON * Math.max(1, Math.abs(scaled)) * 4 ? cents : null
}
function stakeLimits(status: SicBoStatus): Readonly<{ minimum: number; maximum: number; increment: number }> | null {
  const minimum = moneyCents(status.minimumStake)
  const maximum = moneyCents(status.maximumStakePerBet)
  const increment = moneyCents(status.stakeIncrement)
  return minimum !== null && minimum > 0 && maximum !== null && maximum >= minimum && increment !== null && increment > 0
    ? { minimum, maximum, increment } : null
}
