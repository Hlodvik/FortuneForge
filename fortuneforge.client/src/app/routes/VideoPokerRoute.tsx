import { useCallback, useEffect, useMemo, useState } from 'react'
import { HttpVideoPokerGateway, VideoPokerGame } from '@fortuneforge/games-video-poker'
import '@fortuneforge/games-video-poker/styles.css'
import { PlayerHeader } from '../../components/PlayerHeader'
import { fetchWithAccountSession, getCurrentAccount, type AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { WalletPracticeMode } from './WalletPracticeMode'
import { practiceAccountFetch, walletPracticeModeEnabled } from './walletPracticeMode'

export function AuthenticatedVideoPokerRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/games/video-poker')
  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Video Poker…" errorTitle="Video Poker could not be opened." onRetry={reload} />
  }
  return <VideoPokerSession initialAccount={account} />
}

function VideoPokerSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const practiceMode = walletPracticeModeEnabled()
  const gateway = useMemo(() => practiceMode
    ? new HttpVideoPokerGateway('/api/games/video-poker', practiceAccountFetch(fetchWithAccountSession))
    : new HttpVideoPokerGateway('/api/games/video-poker', fetchWithAccountSession), [practiceMode])

  useEffect(() => setAccount(initialAccount), [initialAccount])
  const refreshBalance = useCallback(() => {
    void getCurrentAccount().then(setAccount).catch(() => undefined)
  }, [])

  return <div className="player-page">
    <PlayerHeader account={account} />
    <WalletPracticeMode enabled={practiceMode} path="/games/video-poker" />
    <VideoPokerGame gateway={gateway} playerId={`${account.userId}:${practiceMode ? 'practice' : 'account'}`} onBalanceChange={practiceMode ? undefined : refreshBalance} />
  </div>
}
