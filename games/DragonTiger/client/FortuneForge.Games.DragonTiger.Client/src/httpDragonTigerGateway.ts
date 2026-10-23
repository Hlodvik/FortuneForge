import { DragonTigerGatewayError, type DragonTigerCard, type DragonTigerGateway, type DragonTigerRequestOptions, type DragonTigerRound, type DragonTigerStakes, type DragonTigerStatus } from './contracts'

export class HttpDragonTigerGateway implements DragonTigerGateway {
  constructor(readonly basePath = '/api/games/dragon-tiger', private readonly requestFn: typeof fetch = fetch) {}
  getStatus(signal?: AbortSignal) { return this.request('/status', { signal }, isStatus) }
  async getLatestRound(signal?: AbortSignal) {
    return this.request('/rounds/latest', { signal }, (v): v is DragonTigerRound | null => v === null || isRound(v), true)
  }
  async createRound(stakes: DragonTigerStakes, options: DragonTigerRequestOptions) {
    const round = await this.request('/rounds', {
      method: 'POST', signal: options.signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': options.idempotencyKey }, body: JSON.stringify(stakes),
    }, isRound)
    if (!round.bets.every(bet => bet.stake === stakes[`${bet.side}Stake`])) {
      throw new DragonTigerGatewayError('The table did not confirm these wagers. Restore this deal before playing again.', 'dragon-tiger-invalid-response')
    }
    return round
  }
  private async request<T>(path: string, init: RequestInit, validate: (v: unknown) => v is T, allowEmpty = false): Promise<T> {
    const request = this.requestFn
    const response = await request(`${this.basePath.replace(/\/$/, '')}${path}`, init)
    const value: unknown = response.status === 204 && allowEmpty ? null : await response.json().catch(() => undefined)
    if (!response.ok) {
      const error = record(value) ? value : {}
      throw new DragonTigerGatewayError(typeof error.message === 'string' ? error.message : `The table request failed (${response.status}).`, typeof error.code === 'string' ? error.code : undefined, response.status)
    }
    if (!validate(value)) throw new DragonTigerGatewayError('The table returned an incomplete result. Restore this deal before playing again.', 'dragon-tiger-invalid-response')
    return value
  }
}
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object'
const finite = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v)
const money = (v: unknown): v is number => finite(v) && v >= 0 && Math.abs(v * 100 - Math.round(v * 100)) < .00001
const equal = (a: number, b: number) => Math.abs(a - b) < .00001
const rankNames = ['ace','two','three','four','five','six','seven','eight','nine','ten','jack','queen','king']
const card = (v: unknown): v is DragonTigerCard => record(v) && rankNames.includes(String(v.rank)) && ['clubs','diamonds','hearts','spades'].includes(String(v.suit))
function isStatus(v: unknown): v is DragonTigerStatus {
  return record(v) && typeof v.available === 'boolean' && money(v.balance) && money(v.minimumStake) && v.minimumStake > 0 && money(v.maximumStake) && v.maximumStake >= v.minimumStake && money(v.maximumTotalStake) && v.maximumTotalStake >= v.minimumStake && money(v.stakeIncrement) && v.stakeIncrement > 0 && typeof v.mode === 'string'
}
export function isRound(v: unknown): v is DragonTigerRound {
  if (!record(v) || typeof v.roundId !== 'string' || !v.roundId || !money(v.balance) || v.phase !== 'settled' || !card(v.dragonCard) || !card(v.tigerCard) || !money(v.totalStake) || v.totalStake <= 0 || !finite(v.profit) || !money(v.totalReturn)) return false
  const dragon = rankNames.indexOf(v.dragonCard.rank) + 1, tiger = rankNames.indexOf(v.tigerCard.rank) + 1
  const outcome = dragon > tiger ? 'dragon' : dragon < tiger ? 'tiger' : 'tie'
  if (v.dragonValue !== dragon || v.tigerValue !== tiger || v.outcome !== outcome || !Array.isArray(v.bets) || v.bets.length !== 3) return false
  const sides = new Set<string>(); let total = 0, returns = 0
  for (const b of v.bets) {
    if (!record(b) || !['dragon','tiger','tie'].includes(String(b.side)) || sides.has(String(b.side)) || !money(b.stake) || !money(b.totalReturn) || !finite(b.profit)) return false
    sides.add(String(b.side)); total += b.stake; returns += b.totalReturn
    const expected = b.stake === 0 ? 0 : b.side === outcome ? b.stake * (b.side === 'tie' ? 9 : 2) : outcome === 'tie' && b.side !== 'tie' ? b.stake / 2 : 0
    const disposition = b.stake === 0 ? 'not-placed' : b.side === outcome ? 'win' : outcome === 'tie' && b.side !== 'tie' ? 'half-loss' : 'loss'
    if (!equal(b.totalReturn, expected) || !equal(b.profit, expected - b.stake) || b.disposition !== disposition) return false
  }
  return equal(v.totalStake, total) && equal(v.totalReturn, returns) && equal(v.profit, returns - total) && Number.isInteger(v.shoeCardsUsed) && Number.isInteger(v.shoeCardsRemaining) && Number(v.shoeCardsUsed) >= 2 && Number(v.shoeCardsRemaining) >= 0 && Number(v.shoeCardsUsed) + Number(v.shoeCardsRemaining) === 416
}
