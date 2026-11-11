import { SolitaireBoard } from '@fortuneforge/games-solitaire'
import bundledStyles from '@fortuneforge/games-solitaire/styles.css?inline'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useReleasedGame } from '../../features/game-releases/useReleasedGame'
import { CompetitiveSolitairePage, FreeSolitairePage } from '../../pages/cards/solitaire/CompetitiveSolitairePage'
import { AuthenticatedRouteState } from './AuthenticatedRouteState'

export function AuthenticatedSolitaireRoute() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount('/cards/solitaire')

  if (isLoading || account === null) {
    return <AuthenticatedRouteState error={error} loadingLabel="Opening competitive Solitaire…" errorTitle="Competitive Solitaire could not be opened." onRetry={reload} />
  }
  return <ReleasedSolitaireSession key={account.userId} account={account} />
}

export function ReleasedSolitaireSession({ account, freeOnly = false }: { account: AccountSummary; freeOnly?: boolean }) {
  const { Game: Board, pending } = useReleasedGame('solitaire', 'Solitaire', SolitaireBoard, bundledStyles)
  if (!Board) return pending
  return freeOnly ? <FreeSolitairePage account={account} Board={Board} /> :
    <CompetitiveSolitairePage account={account} Board={Board} />
}
