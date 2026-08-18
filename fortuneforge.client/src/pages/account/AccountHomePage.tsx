import blackjackPreview from '../../assets/cards/previews/blackjack-game.png'
import holdemPreview from '../../assets/cards/previews/holdem-game.png'
import solitairePreview from '../../assets/cards/previews/solitaire-game.png'
import { PlayerHeader } from '../../components/PlayerHeader'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useRecentSlotGame } from '../../games/slots/useRecentSlotGame'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import '../index.css'

const cardDetails = {
  blackjack: { href: '/cards/blackjack', image: blackjackPreview },
  'texas-holdem': { href: '/cards/texas-holdem', image: holdemPreview },
  solitaire: { href: '/cards/solitaire', image: solitairePreview },
} as const

export function HomePage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount()
  const recentSlot = useRecentSlotGame(account?.userId)
  const cardHistory = useCardRoomHistory()
  const recentCard = cardHistory.activities[0] ?? null
  const cardPlayedAt = recentCard?.completedAtUtc ?? recentCard?.startedAtUtc ?? null
  const slotIsLatest = recentSlot.game !== null && recentSlot.playedAtUtc !== null
    && (cardPlayedAt === null || Date.parse(recentSlot.playedAtUtc) > Date.parse(cardPlayedAt))
  const recent = slotIsLatest && recentSlot.game !== null
    ? {
        href: recentSlot.game.playHref ?? '/slots',
        image: recentSlot.game.image,
        title: recentSlot.game.title,
        summary: 'Continue your most recently played slot machine.',
      }
    : recentCard !== null
      ? {
          href: cardDetails[recentCard.game].href,
          image: cardDetails[recentCard.game].image,
          title: recentCard.gameLabel,
          summary: recentCard.completedAtUtc === null ? recentCard.summary : recentCard.title,
        }
      : null

  return (
    <div className="player-page player-home-page">
      {account !== null
        ? <PlayerHeader account={account} />
        : <header className="player-shell-header"><a className="player-shell-header__brand" href="/" aria-label="Fortune Forge home"><span className="player-shell-header__spark" aria-hidden="true">✦</span><strong>Fortune Forge</strong></a></header>}
      <main className="player-main player-home-main">
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && <AccountError message={error} onRetry={reload} />}
        {!isLoading && account !== null && (
          <section className="player-home-grid" aria-label={`${account.playerName}'s game dashboard`}>
            <a className="player-home-card player-home-card--games" href="/games">
              <span className="player-home-card__icon" aria-hidden="true">✦</span>
              <small>All game rooms</small><strong>View games</strong>
              <span>Browse every available table, machine, arcade game, and dice game.</span><b>Explore the forge →</b>
            </a>

            <section className="player-home-card player-home-card--recent" aria-labelledby="recently-played-title">
              <small>Recently played</small>
              {recent !== null ? (
                <a href={recent.href}>
                  <img src={recent.image} alt="" draggable="false" />
                  <strong id="recently-played-title">{recent.title}</strong>
                  <span>{recent.summary}</span><b>Continue →</b>
                </a>
              ) : (
                <div><span className="player-home-card__empty" aria-hidden="true">◇</span><strong id="recently-played-title">Nothing played yet</strong><span>Your latest game will appear here.</span></div>
              )}
            </section>

            <nav className="player-home-card player-home-card--account" aria-label="Account shortcuts">
              <a className="player-home-shortcut player-home-shortcut--recharge" href="/home/rand"><span aria-hidden="true">＋</span><strong>Recharge</strong><small>Add Rand to your balance</small></a>
              <a className="player-home-shortcut" href="/home/settings"><span aria-hidden="true">⚙</span><strong>Account settings</strong></a>
              <a className="player-home-shortcut" href="/home/history"><span aria-hidden="true">↺</span><strong>History</strong></a>
            </nav>
          </section>
        )}
      </main>
    </div>
  )
}

function AccountLoading({ label }: { label: string }) {
  return <div className="player-state" role="status"><span aria-hidden="true">✦</span>{label}</div>
}

function AccountError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="player-state player-state--error" role="alert">
      <strong>Your account could not be opened.</strong><span>{message}</span>
      <button className="landing-button landing-button--secondary" type="button" onClick={onRetry}>Try again</button>
      <a href="/login">Return to login</a>
    </div>
  )
}
