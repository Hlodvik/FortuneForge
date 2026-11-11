import { HeartsGame as BundledHeartsGame, HttpHeartsGateway, type HeartsCard, type HeartsParticipation } from '@fortuneforge/games-hearts'
import bundledStyles from '@fortuneforge/games-hearts/styles.css?inline'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { InGameShell } from '../../components/InGameShell'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useGameLeaveGuard } from '../../features/navigation/GameLeaveGuard'
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
  const [participation, setParticipation] = useState<HeartsParticipation>(null)
  const gateway = useMemo(() => new HttpHeartsGateway('/api/games/hearts', fetchWithAccountSession), [])
  useEffect(() => setAccount(initialAccount), [initialAccount])
  const publishBalance = useCallback((balance: number) => setAccount(current => ({ ...current, balances: { ...current.balances, slotsCredits: balance } })), [])
  useGameLeaveGuard(participation === null ? null : {
    title: 'Leave Hearts?',
    description: participation === 'paid-pending'
      ? 'Your entry request is still being confirmed and may have been accepted. Leaving this page does not cancel it. Return to reconnect before choosing another action.'
      : participation === 'paid-queue'
        ? 'Your entry is reserved and the table may start while you are away. Leaving this page does not cancel the queue. Use Cancel & return entry at the table to request a refund.'
        : participation === 'paid-match'
          ? 'This match stays active if you leave this page. Missing a turn forfeits your entry and your chance to win the pot. Return before the turn timer expires to keep playing.'
          : participation === 'free-pending' || participation === 'trial-pending'
            ? 'Your practice hand is still being opened. Leaving this page may lose your progress. No account credits are at stake.'
            : 'Leaving this page loses your current practice progress. No account credits are at stake.',
  })
  const { Game: HeartsGame, pending } = useReleasedGame('hearts', 'Hearts', BundledHeartsGame, bundledStyles)
  if (HeartsGame === null) return pending

  return <InGameShell account={account} title="Hearts" bodyClassName="hearts-shell-body">
    <HeartsGame key={account.userId} gateway={gateway} balance={account.balances.slotsCredits} onBalanceChange={publishBalance} onParticipationChange={setParticipation} renderCard={renderHeartsCard} />
  </InGameShell>
}

function renderHeartsCard(card: HeartsCard, faceDown = false) {
  const label = card.label.replace(/[♠♥♣♦]/g, '')
  const rank = ({ A: 1, J: 11, Q: 12, K: 13 } as Record<string, number>)[label] ?? Number(label)
  return <PlayingCard card={{ id: card.code, suit: card.suit as CardSuit, rank: rank as CardRank }} faceDown={faceDown} />
}
