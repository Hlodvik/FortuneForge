export type KenoTicket = Readonly<{ numbers: readonly number[] }>
export type KenoDraw = Readonly<{ numbers: readonly number[] }>
export type KenoRoundRequest = Readonly<{ ticket: KenoTicket }>
export type KenoRound = Readonly<{
  roundId: string
  balance: number
  phase: 'completed'
  ticket: KenoTicket
  draw: KenoDraw
  hitCount: number
  outcome: string | null
}>
export type KenoRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey?: string }>

export interface KenoGateway {
  getStatus(signal?: AbortSignal): Promise<KenoStatus>
  createRound(request: KenoRoundRequest, options?: KenoRequestOptions): Promise<KenoRound>
}

export type KenoStatus = Readonly<{ available: boolean; balance: number; mode: string }>

export class KenoGatewayError extends Error {
  readonly code: string
  readonly status: number

  constructor(message: string, code = 'keno-request-failed', status = 0) {
    super(message)
    this.name = 'KenoGatewayError'
    this.code = code
    this.status = status
  }
}
