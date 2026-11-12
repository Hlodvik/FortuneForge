import { createBlackjackTableApi } from '@fortuneforge/games-blackjack/table-api'
import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'

export * from '@fortuneforge/games-blackjack/table-api'

export function isPublicBlackjackTableResponse(value: unknown): boolean {
  return !hasForbiddenPublicField(value)
}

function hasForbiddenPublicField(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(hasForbiddenPublicField)
  if (typeof value !== 'object' || value === null) {
    return typeof value === 'string' && /^(?:bot|actor|user):/i.test(value)
  }
  return Object.entries(value).some(([key, child]) => {
    const normalized = key.replace(/[^a-z0-9]/gi, '').toLowerCase()
    return normalized.includes('bot')
      || normalized.includes('skill')
      || normalized.includes('seed')
      || normalized === 'actorid'
      || normalized === 'userid'
      || normalized === 'accountid'
      || normalized === 'deck'
      || normalized.startsWith('raw')
      || hasForbiddenPublicField(child)
  })
}

export const {
  getBlackjackTableStatus,
  getBlackjackTableSession,
  getBlackjackTableSnapshot,
  getBlackjackTableHistory,
  markBlackjackTableHistorySeen,
  joinBlackjackTableQueue,
  cancelBlackjackTableQueue,
  postBlackjackTableWager,
  sitOutBlackjackTableRound,
  postBlackjackTableAction,
  leaveBlackjackTable,
} = createBlackjackTableApi(fetchWithAccountSession, isPublicBlackjackTableResponse)
