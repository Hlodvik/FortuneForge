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
  walletBalanceCents?: number
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
export type CrapsExtraBet = Readonly<{ kind: CrapsExtraBetKind; stake: number; resolved: boolean; won: boolean; totalReturn: number | null; wagerId?: string | null; placedForRoll?: number | null; resolvedOnRoll?: number | null; cancelled?: boolean }>
export type CrapsExtraBetRequest = Readonly<{ kind: Exclude<CrapsExtraBetKind, 'odds'>; stake: number }>
export type CrapsActionContext = Readonly<{ expectedRevision: number; expectedRollNumber: number }>

export type CrapsRound = Readonly<{
  roundId: string
  stake: number
  phase: CrapsPhase
  point: number | null
  rolls: readonly CrapsRoll[]
  lastOutcome: CrapsOutcome | null
  extraBets?: readonly CrapsExtraBet[]
  walletBalanceCents?: number
  revision?: number
  totalWagerCents?: number
  totalReturnCents?: number
}>

export interface CrapsGateway {
  getStatus(signal?: AbortSignal): Promise<CrapsStatus>
  getActiveRound?(signal?: AbortSignal): Promise<CrapsRound | null>
  getRound?(roundId: string, signal?: AbortSignal): Promise<CrapsRound>
  startRound(stake: number, signal?: AbortSignal, extraBets?: readonly CrapsExtraBetRequest[], operationId?: string): Promise<CrapsRound>
  roll(roundId: string, signal?: AbortSignal, operationId?: string, context?: CrapsActionContext): Promise<CrapsRound>
  placeOdds?(roundId: string, stake: number, signal?: AbortSignal, operationId?: string, context?: CrapsActionContext): Promise<CrapsRound>
  setWager?(roundId: string, kind: CrapsExtraBetKind, stake: number, signal?: AbortSignal, operationId?: string, context?: CrapsActionContext): Promise<CrapsRound>
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
