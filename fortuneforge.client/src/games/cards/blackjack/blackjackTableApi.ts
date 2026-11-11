import { createBlackjackTableApi } from '@fortuneforge/games-blackjack/table-api'
import { fetchWithAccountSession } from '../../../features/account/services/accountsApi'

export * from '@fortuneforge/games-blackjack/table-api'

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
} = createBlackjackTableApi(fetchWithAccountSession)
