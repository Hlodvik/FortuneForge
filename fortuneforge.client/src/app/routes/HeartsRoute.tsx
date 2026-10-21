import { HeartsGame, HttpHeartsGateway, type HeartsCard } from '@fortuneforge/games-hearts'
import '@fortuneforge/games-hearts/styles.css'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'
import { fetchWithAccountSession, type AccountSummary } from '../../features/account/services/accountsApi'
import { PlayingCard } from '../../games/cards/shared/PlayingCard'
import type { CardRank, CardSuit } from '../../games/cards/shared/cards'
import '../../games/cards/shared/playingCards.css'

export function AuthenticatedHeartsRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/hearts')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening Hearts…" errorTitle="Hearts could not be opened." onRetry={reload} />
  }

  return <HeartsSession key={account.userId} initialAccount={account} />
}

function HeartsSession({ initialAccount }: Readonly<{ initialAccount: AccountSummary }>) {
  const [account, setAccount] = useState(initialAccount)
  const gateway = useMemo(() => new HttpHeartsGateway('/api/games/hearts', fetchWithAccountSession), [])
  useEffect(() => setAccount(initialAccount), [initialAccount])
  const publishBalance = useCallback((balance: number) => setAccount(current => ({ ...current, balances: { ...current.balances, slotsCredits: balance } })), [])
  return <InGameShell account={account} title="Hearts" theme="cards" bodyClassName="hearts-shell-body">
    <HeartsGame key={account.userId} gateway={gateway} balance={account.balances.slotsCredits} onBalanceChange={publishBalance} renderCard={renderHeartsCard} />
  </InGameShell>
}

function renderHeartsCard(card: HeartsCard, faceDown = false) {
  const label = card.label.replace(/[♠♥♣♦]/g, '')
  const rank = ({ A: 1, J: 11, Q: 12, K: 13 } as Record<string, number>)[label] ?? Number(label)
  return <PlayingCard card={{ id: card.code, suit: card.suit as CardSuit, rank: rank as CardRank }} faceDown={faceDown} />
}
