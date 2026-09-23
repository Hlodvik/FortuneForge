export type SicBoBetKind =
  | 'small' | 'big' | 'odd' | 'even' | 'single-number' | 'total'
  | 'two-number-combination' | 'specific-double' | 'any-triple' | 'specific-triple'

export type SicBoBetRequest = Readonly<{
  kind: SicBoBetKind
  stake: number
  face: number | null
  total: number | null
  firstFace: number | null
  secondFace: number | null
}>

export type SicBoStatus = Readonly<{
  available: boolean
  minimumStake: number
  maximumStakePerBet: number
  stakeIncrement: number
  maximumBetsPerRound: number
  balance: number
  mode: string
}>

export type SicBoSettlement = Readonly<SicBoBetRequest & {
  betIndex: number
  won: boolean
  profitOdds: number
  profit: number
  totalReturn: number
}>

export type SicBoRound = Readonly<{
  roundId: string
  balance: number
  phase: 'settled'
  dice: readonly [number, number, number]
  total: number
  isTriple: boolean
  totalStaked: number
  totalReturn: number
  profit: number
  settlements: readonly SicBoSettlement[]
}>
export type SicBoRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey?: string }>

export interface SicBoGateway {
  getStatus(signal?: AbortSignal): Promise<SicBoStatus>
  createRound(bets: readonly SicBoBetRequest[], options?: SicBoRequestOptions): Promise<SicBoRound>
}

export class SicBoGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'sic-bo-request-failed', status = 0) {
    super(message)
    this.name = 'SicBoGatewayError'
    this.code = code
    this.status = status
  }
}
