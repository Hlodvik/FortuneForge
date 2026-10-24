import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AndarBaharGame, HttpAndarBaharGateway, type AndarBaharCard } from '@fortuneforge/games-andar-bahar'
import '@fortuneforge/games-andar-bahar/styles.css'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'
import type { CardRank } from '../../games/cards/shared/cards'
import '../../games/cards/shared/playingCards.css'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './AndarBaharRoute.css'

const cardRanks: Readonly<Record<AndarBaharCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: AndarBaharCard) => <PlayingCard card={{
  id: `andar-bahar-${card.rank}-${card.suit}`, rank: cardRanks[card.rank], suit: card.suit,
}} />

export function AuthenticatedAndarBaharRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/andar-bahar')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Andar Bahar…" errorTitle="Andar Bahar could not be opened." onRetry={reload} />
  }
  return <AndarBaharSession key={account.userId} initialAccount={account} />
}

function AndarBaharSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const active = useRef(true)
  const refreshGeneration = useRef(0)
  const identity = useRef(initialAccount.userId)
  identity.current = initialAccount.userId
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => new HttpAndarBaharGateway('/api/games/andar-bahar', practiceMode
    ? practiceAccountFetch(fetchWithAccountSession)
    : fetchWithAccountSession), [practiceMode])

  useEffect(() => { refreshGeneration.current++; setAccount(initialAccount) }, [initialAccount])
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const refreshBalance = useCallback(() => {
    const requestedUser = initialAccount.userId
    const requestedGeneration = ++refreshGeneration.current
    void getCurrentAccount().then(next => {
      if (active.current && refreshGeneration.current === requestedGeneration
        && identity.current === requestedUser && next.userId === requestedUser) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])
  const scope = `${initialAccount.userId}:${practiceMode ? 'practice' : 'account'}`

  return <InGameShell account={account} theme="casino" className="andar-bahar-page" bodyClassName="andar-bahar-route__body">
    <AndarBaharGame key={scope} playerId={scope} gateway={gateway} onBalanceChange={practiceMode ? undefined : refreshBalance} renderCardFace={renderCardFace} />
  </InGameShell>
}
