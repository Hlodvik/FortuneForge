import { fetchWithAccountSession, getCurrentAccount } from '../../features/account/services/accountsApi'

const GAME_ID = 'empty-slots-v1'
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const natural = (value: unknown): value is number => Number.isSafeInteger(value) && Number(value) >= 0
const symbol = (value: unknown) => typeof value === 'string' && /^S(0[1-9]|1[0-9]|2[0-3])$/.test(value)

export function validateJadeSpinRequest(value: unknown) {
  if (!isRecord(value) || !Number.isSafeInteger(value.wagerPoints) || Number(value.wagerPoints) < 2 ||
    Number(value.wagerPoints) > (value.useFreeSpin === true ? 2000 : 400) || Number(value.wagerPoints) % 2 !== 0 ||
    typeof value.useFreeSpin !== 'boolean' || typeof value.useSpecialBoost !== 'boolean' ||
    (value.useFreeSpin && value.useSpecialBoost)) throw new Error('Invalid Jade Dragon spin request.')
  return { gameId: GAME_ID, wagerPoints: Number(value.wagerPoints), useFreeSpin: value.useFreeSpin, useSpecialBoost: value.useSpecialBoost }
}

export function validateJadeOutcome(value: unknown): Record<string, unknown> & { reels: string[][]; slotsCreditsBalance: number } {
  if (!isRecord(value) || value.gameId !== GAME_ID || typeof value.spinId !== 'string' ||
    typeof value.slotsCreditsBalance !== 'number' || !Number.isFinite(value.slotsCreditsBalance) || value.slotsCreditsBalance < 0 ||
    value.pointValueInCents !== 25 || !isRecord(value.payout) || !natural(value.payout.totalPoints) ||
    !Array.isArray(value.reels) || value.reels.length !== 5 ||
    !Array.isArray(value.reels[0]) || ![4, 6].includes(value.reels[0].length) ||
    !value.reels.every(reel => Array.isArray(reel) && reel.length === (value.reels as unknown[][])[0].length && reel.every(symbol)) ||
    !natural(value.freeSpinsRemaining) || !natural(value.specialPointsBalance) || !Array.isArray(value.sealCollections) ||
    !(value.freeSpinWagerPoints === null || natural(value.freeSpinWagerPoints)) ||
    !(value.freeSpinFeatureMode === null || value.freeSpinFeatureMode === 'copy-expand-wild') ||
    !Array.isArray(value.payout.paylines) || !value.payout.paylines.every(line => isRecord(line) &&
      natural(line.amountPoints) && Array.isArray(line.matches) && line.matches.every(paid => isRecord(paid) &&
        isRecord(paid.match) && Array.isArray(paid.match.positions) && paid.match.positions.every(position => isRecord(position) &&
          natural(position.reel) && position.reel < 5 && natural(position.row) && position.row < (value.reels as unknown[][])[0].length)))
  ) throw new Error('The server returned an invalid Jade Dragon result. Reload to recover the settled balance.')
  return value as Record<string, unknown> & { reels: string[][]; slotsCreditsBalance: number }
}

async function json(response: Response) {
  const value: unknown = await response.json()
  if (!response.ok) throw new Error(isRecord(value) && typeof value.error === 'string' ? value.error : `Game service returned HTTP ${response.status}.`)
  return value
}

export async function loadJadeSession() {
  const [account, state, rules] = await Promise.all([
    getCurrentAccount(),
    fetchWithAccountSession(`/api/slots/state?gameId=${GAME_ID}`, { method: 'GET' }).then(json),
    fetch('/api/slots/jade-dragon/presentation', { cache: 'no-store' }).then(json),
  ])
  if (!isRecord(state) || !natural(state.freeSpinsRemaining) ||
    !(state.freeSpinWagerPoints === null || natural(state.freeSpinWagerPoints)) ||
    !natural(state.specialPointsBalance) || !Array.isArray(state.sealCollections) ||
    !isRecord(rules) || rules.gameId !== GAME_ID || !Array.isArray(rules.symbols) ||
    !Number.isFinite(account.balances.slotsCredits) || account.balances.slotsCredits < 0) throw new Error('Game state could not be loaded. Reload to retry.')
  return { state, rules, balanceCents: Math.round(account.balances.slotsCredits * 100) }
}

export async function spinJade(payload: unknown) {
  const request = validateJadeSpinRequest(payload)
  // Send once. Never retry a financial POST after a timeout or ambiguous transport error.
  const result = validateJadeOutcome(await json(await fetchWithAccountSession('/api/slots/spins', {
    method: 'POST', body: JSON.stringify(request),
  })))
  if (result.wagerPoints !== request.wagerPoints || result.isFreeSpin !== request.useFreeSpin) throw new Error('The settled response does not match the requested round. Reload to recover.')
  return { ...result, balanceCents: Math.round(result.slotsCreditsBalance * 100) }
}
