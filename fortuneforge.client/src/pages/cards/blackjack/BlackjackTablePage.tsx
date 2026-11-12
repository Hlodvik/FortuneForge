import type { ComponentProps } from 'react'
import {
  BlackjackTableGame,
  BlackjackTableContent as PackagedTableContent,
  BlackjackTablePreview as PackagedTablePreview,
  BlackjackTablePresentation,
  type BlackjackTablePresentationServices,
} from '@fortuneforge/games-blackjack'
import bundledStyles from '@fortuneforge/games-blackjack/styles.css?inline'
import { InGameShell } from '../../../components/InGameShell'
import { fetchWithAccountSession, type AccountSummary } from '../../../features/account/services/accountsApi'
import { GameLoadingScreen } from '../../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
import { useBundledGameStyles, useReleasedGame } from '../../../features/game-releases/useReleasedGame'
import { isPublicBlackjackTableResponse } from '../../../games/cards/blackjack/blackjackTableApi'
import { PlayingCard } from '../../../games/cards/shared/PlayingCard'
import { playCardAudio, useCardAudioClick } from '../../../games/cards/shared/cardAudio'
import '../../../games/cards/shared/playingCards.css'

export { BlackjackHowToPlay } from '@fortuneforge/games-blackjack'

function BlackjackTableLoading() {
  return <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/blackjack')} />
}

const presentation: BlackjackTablePresentationServices = {
  Loading: BlackjackTableLoading,
  PlayingCard,
  playCardAudio,
}

export function BlackjackTablePage({ account }: { account: AccountSummary }) {
  const { Game, pending } = useReleasedGame('blackjack', 'Blackjack', BlackjackTableGame, bundledStyles)
  const onCardAudioClick = useCardAudioClick()

  if (!Game) return pending
  return <Game
    startingBalanceCredits={account.balances.slotsCredits}
    fetchWithAccountSession={fetchWithAccountSession}
    isPublicResponse={isPublicBlackjackTableResponse}
    presentation={presentation}
    onCardAudioClick={onCardAudioClick}
    renderShell={({ balanceCredits, title, className, bodyClassName, children }) => <InGameShell
      account={{ ...account, balances: { ...account.balances, slotsCredits: balanceCredits } }}
      title={title}
      className={className}
      bodyClassName={bodyClassName}
    >{children}</InGameShell>}
  />
}

export function BlackjackTableContent(props: ComponentProps<typeof PackagedTableContent>) {
  return <BlackjackTablePresentation services={presentation}>
    <PackagedTableContent {...props} />
  </BlackjackTablePresentation>
}

export function BlackjackTablePreview(props: ComponentProps<typeof PackagedTablePreview>) {
  useBundledGameStyles(bundledStyles)
  return <BlackjackTablePresentation services={presentation}>
    <PackagedTablePreview {...props} />
  </BlackjackTablePresentation>
}
