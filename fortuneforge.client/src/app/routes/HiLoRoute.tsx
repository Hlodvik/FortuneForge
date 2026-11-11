import { useCallback, useEffect, useMemo, useState } from 'react'
import { HiLoGame as BundledHiLoGame, HttpHiLoGateway, type HiLoCard } from '@fortuneforge/games-hi-lo'
import bundledStyles from '@fortuneforge/games-hi-lo/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { InGameShell } from '../../components/InGameShell'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'
import type { CardRank } from '../../games/cards/shared/cards'
import '../../games/cards/shared/playingCards.css'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'
import './HiLoRoute.css'

const cardRanks: Readonly<Record<HiLoCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: HiLoCard) => <PlayingCard card={{
  id: `hi-lo-${card.rank}-${card.suit}`, rank: cardRanks[card.rank], suit: card.suit,
}} />

export function AuthenticatedHiLoRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/hi-lo')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Hi-Lo…" errorTitle="Hi-Lo could not be opened." onRetry={reload} />
  }
  return <HiLoSession key={account.userId} initialAccount={account} />
}

function HiLoSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpHiLoGateway('/api/games/hi-lo', practiceAccountFetch(fetchWithAccountSession))
    : new HttpHiLoGateway('/api/games/hi-lo', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(next => {
      if (next.userId === initialAccount.userId) setAccount(next)
    }).catch(() => undefined)
  }, [initialAccount.userId])

  const { Game: HiLoGame, pending } = useReleasedGame('hi-lo', 'Hi-Lo', BundledHiLoGame, bundledStyles)
  if (HiLoGame === null) return pending

  return <InGameShell account={account} title="Hi-Lo" className="hi-lo-page">
    <HiLoGame key={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} renderCardFace={renderCardFace} />
  </InGameShell>
}
