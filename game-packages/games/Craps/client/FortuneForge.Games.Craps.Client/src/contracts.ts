export type CrapsPhase = 'come-out' | 'point' | 'resolved'

export type CrapsRollResult =
  | 'natural-win'
  | 'craps-loss'
  | 'point-established'
  | 'point-hit'
  | 'seven-out'
  | 'no-decision'

export type CrapsStatus = Readonly<{
  available: boolean
  minimumStake: number
  maximumStake: number
  stakeIncrement: number
  mode: string
}>

export type CrapsRoll = Readonly<{
  rollNumber: number
  first: number
  second: number
  total: number
  result: CrapsRollResult | null
}>

export type CrapsOutcome = Readonly<{
  first: number
  second: number
  total: number
  result: CrapsRollResult
  isTerminal: boolean
  totalReturn: number | null
}>

export type CrapsExtraBetKind = 'field' | 'any-seven' | 'any-craps' | 'odds'
export type CrapsExtraBet = Readonly<{ kind: CrapsExtraBetKind; stake: number; resolved: boolean; won: boolean; totalReturn: number | null }>
export type CrapsExtraBetRequest = Readonly<{ kind: Exclude<CrapsExtraBetKind, 'odds'>; stake: number }>

export type CrapsRound = Readonly<{
  roundId: string
  stake: number
  phase: CrapsPhase
  point: number | null
  rolls: readonly CrapsRoll[]
  lastOutcome: CrapsOutcome | null
  extraBets?: readonly CrapsExtraBet[]
}>

export interface CrapsGateway {
  getStatus(signal?: AbortSignal): Promise<CrapsStatus>
  startRound(stake: number, signal?: AbortSignal, extraBets?: readonly CrapsExtraBetRequest[]): Promise<CrapsRound>
  roll(roundId: string, signal?: AbortSignal): Promise<CrapsRound>
  placeOdds?(roundId: string, stake: number, signal?: AbortSignal): Promise<CrapsRound>
}

export class CrapsGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'craps-request-failed', status = 0) {
    super(message)
    this.name = 'CrapsGatewayError'
    this.code = code
    this.status = status
  }
}
