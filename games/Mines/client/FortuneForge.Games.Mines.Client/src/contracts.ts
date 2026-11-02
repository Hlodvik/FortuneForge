export type MinesTile = Readonly<{index:number;isRevealed:boolean;kind:'hidden'|'gem'|'mine'}>
export type MinesRound = Readonly<{
  roundId:string;wagerCents:number;mineCount:number;status:'active'|'lost'|'cashed-out'|'cleared'|'consolation';
  safeReveals:number;gemsRemaining:number;currentMultiplier:number;nextMultiplier:number|null;
  cashOutCents:number;canCashOut:boolean;payoutCents:number;tiles:readonly MinesTile[];
  /** Accepted-round rule. Missing metadata belongs to the original 99% RTP rules. */
  houseEdgeBasisPoints?:number;
  /** Missing metadata is accepted legacy policy 1, never the current bounded curve. */
  payoutPolicyVersion?:number;nextMinePayoutCents?:number;
}>
export type MinesSession = Readonly<{
  balanceCents:number;practice:boolean;round:MinesRound|null;
  minimumWagerCents:number;maximumWagerCents:number;houseEdgeBasisPoints:number;
  payoutPolicyVersion?:number;
}>
export type MinesCommand =
  | Readonly<{action:'start';requestId:string;wagerCents:number;mineCount:number}>
  | Readonly<{action:'reveal';requestId:string;roundId:string;tile:number}>
  | Readonly<{action:'cashout';requestId:string;roundId:string}>
export interface MinesGateway {
  getSession(signal?:AbortSignal):Promise<MinesSession>
  execute(command:MinesCommand):Promise<MinesSession>
}
export class MinesGatewayError extends Error {
  constructor(message:string,readonly code='mines-unavailable',readonly status=0){super(message);this.name='MinesGatewayError'}
}
