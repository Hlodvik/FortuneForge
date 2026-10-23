import { useCallback, useEffect, useMemo, useState } from 'react'
import { DragonTigerGame, HttpDragonTigerGateway, type DragonTigerCard } from '@fortuneforge/games-dragon-tiger'
import '@fortuneforge/games-dragon-tiger/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'
import type { CardRank } from '../../games/cards/shared/cards'
import '../../games/cards/shared/playingCards.css'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { PracticeModeNavAction } from './PracticeModeBanner'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './DragonTigerRoute.css'

const cardRanks: Readonly<Record<DragonTigerCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: DragonTigerCard) => <PlayingCard card={{
  id: `dragon-tiger-${card.rank}-${card.suit}`, rank: cardRanks[card.rank], suit: card.suit,
}} />

export function AuthenticatedDragonTigerRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/dragon-tiger')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Dragon Tiger…" errorTitle="Dragon Tiger could not be opened." onRetry={reload} />
  }
  return <DragonTigerSession key={account.userId} initialAccount={account} />
}

function DragonTigerSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpDragonTigerGateway('/api/games/dragon-tiger', practiceAccountFetch(fetchWithAccountSession))
    : new HttpDragonTigerGateway('/api/games/dragon-tiger', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(next => {
      if (next.userId === initialAccount.userId) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])

  return <InGameShell account={account} title="Dragon Tiger" theme="casino" className="dragon-tiger-page" actions={<PracticeModeNavAction enabled={practiceMode} path="/games/dragon-tiger" />}>
    <DragonTigerGame key={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} renderCardFace={renderCardFace} />
  </InGameShell>
}
