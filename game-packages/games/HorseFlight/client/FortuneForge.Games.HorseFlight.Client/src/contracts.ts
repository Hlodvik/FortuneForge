export type HorseFlightPhase = 'running' | 'obstacle-collision' | 'fell'
export type HorseFlightStatus = Readonly<{ available: boolean; tickMilliseconds: number; balance: number; mode: string }>
export type HorseFlightStart = Readonly<{ runId: string; seed: number; tickMilliseconds: number }>
export type HorseFlightResult = Readonly<{ runId: string; balance: number; score: number; phase: Exclude<HorseFlightPhase, 'running'>; totalTicks: number; jumpTicks: readonly number[]; rightClickTicks?: readonly number[] }>
export type HorseFlightRequestOptions = Readonly<{ signal?: AbortSignal; idempotencyKey?: string }>
export interface HorseFlightGateway { getStatus(signal?: AbortSignal): Promise<HorseFlightStatus>; start(options?: HorseFlightRequestOptions): Promise<HorseFlightStart>; complete(runId: string, totalTicks: number, jumpTicks: readonly number[], options?: HorseFlightRequestOptions, rightClickTicks?: readonly number[]): Promise<HorseFlightResult> }
export class HorseFlightGatewayError extends Error { constructor(message: string, readonly code: string, readonly status: number) { super(message); this.name = 'HorseFlightGatewayError' } }
