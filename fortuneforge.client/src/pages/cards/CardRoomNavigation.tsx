import { CardRoomHeader } from '../../games/cards/shared/CardRoomHeader'
import { CardRoomHistory } from '../../games/cards/shared/CardRoomHistory'
import { cardRoomUnseenCount } from '../../games/cards/shared/cardRoomHistoryTypes'
import { useCardRoomHistory } from './useCardRoomHistory'
import type { AccountSummary } from '../../features/account/services/accountsApi'

export function CardRoomNavigation({
  account,
  onBalanceChange,
}: {
  account: AccountSummary
  onBalanceChange?: (balanceCredits: number) => void
}) {
  const history = useCardRoomHistory(onBalanceChange)
  return (
    <CardRoomHeader
      account={account}
      unseenCount={cardRoomUnseenCount(history.activities)}
      onHistoryToggle={(open) => {
        if (open) void history.refresh()
      }}
      historyContent={(
        <CardRoomHistory
          activities={history.activities}
          loading={history.loading}
          error={history.error}
          busyId={history.busyId}
          onSelect={(activity) => void history.select(activity)}
        />
      )}
    />
  )
}
