import { useCallback, useEffect, useMemo, useState } from 'react'
import { BaccaratGame, HttpBaccaratGateway, type BaccaratCard } from '@fortuneforge/games-baccarat'
import '@fortuneforge/games-baccarat/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'
import type { CardRank } from '../../games/cards/shared/cards'
import '../../games/cards/shared/playingCards.css'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './BaccaratRoute.css'

const cardRanks: Readonly<Record<BaccaratCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: BaccaratCard) => <PlayingCard card={{
  id: `baccarat-${card.rank}-${card.suit}`, rank: cardRanks[card.rank], suit: card.suit,
}} />

export function AuthenticatedBaccaratRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/baccarat')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Baccarat…" errorTitle="Baccarat could not be opened." onRetry={reload} />
  }
  return <BaccaratSession initialAccount={account} />
}

function BaccaratSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpBaccaratGateway('/api/games/baccarat', practiceAccountFetch(fetchWithAccountSession))
    : new HttpBaccaratGateway('/api/games/baccarat', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <InGameShell account={account} title="Baccarat" theme="casino" className="baccarat-page" bodyClassName="baccarat-route__body">
    <BaccaratGame key={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} showTitle={false} renderCardFace={renderCardFace} />
  </InGameShell>
}
