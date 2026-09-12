import blackjackPreview from '../../assets/cards/previews/blackjack-game.png'
import holdemPreview from '../../assets/cards/previews/holdem-game.png'
import solitairePreview from '../../assets/cards/previews/solitaire-game.png'
import { PlayerHeader } from '../../components/PlayerHeader'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { SLOT_GAME_CATALOG } from '../../games/slots/catalog'
import { useRecentSlotGame } from '../../games/slots/useRecentSlotGame'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import '../index.css'

const cardDetails = {
  blackjack: { href: '/cards/blackjack', image: blackjackPreview },
  'texas-holdem': { href: '/cards/texas-holdem', image: holdemPreview },
  solitaire: { href: '/cards/solitaire', image: solitairePreview },
} as const

type HomeGameHighlight = {
  href: string
  image: string
  summary: string
  title: string
}

const featuredGameCycle: readonly HomeGameHighlight[] = [
  {
    href: '/cards/blackjack',
    image: blackjackPreview,
    title: 'Fortune Blackjack',
    summary: 'A quick, welcoming table for your first hand.',
  },
  featuredSlot('rainbow-realm', 'A bright reels game with a quick learning curve.'),
  {
    href: '/cards/solitaire',
    image: solitairePreview,
    title: 'Competitive Solitaire',
    summary: 'A calm solo game when you want to set your own pace.',
  },
  featuredSlot('gods-of-olympus', 'A high-energy reels game with a mythic theme.'),
  {
    href: '/cards/texas-holdem',
    image: holdemPreview,
    title: 'Texas Hold’em',
    summary: 'A social table for players ready to read the room.',
  },
  featuredSlot('pirates-fortune', 'A treasure-hunt reels game for a lively session.'),
  featuredSlot('wukong-journey-to-the-west', 'A polished five-reel adventure with seal collections.'),
]

function featuredSlot(id: string, summary: string): HomeGameHighlight {
  const game = SLOT_GAME_CATALOG.find((entry) => entry.id === id)
  if (game === undefined) {
    throw new Error(`Missing featured game '${id}'.`)
  }

  return {
    href: game.playHref ?? '/slots',
    image: game.image,
    title: game.shortTitle,
    summary,
  }
}

function featuredGameForToday(): HomeGameHighlight {
  return featuredGameCycle[new Date().getDay()]!
}

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
  const gameHighlight = recent ?? featuredGameForToday()
  const isFeaturedGame = recent === null

  return (
    <div className="player-page player-home-page">
      {account !== null
        ? <PlayerHeader account={account} />
        : <header className="player-shell-header"><a className="player-shell-header__brand" href="/" aria-label="Fortune Forge home"><span className="player-shell-header__spark" aria-hidden="true">✦</span><strong>Fortune Forge</strong></a></header>}
      <main className="player-main player-home-main">
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && <AccountError message={error} onRetry={reload} />}
        {!isLoading && account !== null && (
          <section className="player-home-dashboard" aria-label={`${account.playerName}'s game dashboard`}>
            <header className="player-home-welcome">
              <p>Player dashboard</p>
              <h1>Welcome, {account.playerName}</h1>
              <span>Pick up a game, check your progress, or manage your account.</span>
            </header>

            <section className="player-home-grid" aria-label="Game and account shortcuts">
              <a className="player-home-card player-home-card--games" href="/games">
                <span className="player-home-card__icon" aria-hidden="true">✦</span>
                <small>All game rooms</small><strong>View games</strong>
                <span>Browse every available table, machine, arcade game, and dice game.</span><b>Explore the forge →</b>
              </a>

              <section className="player-home-card player-home-card--recent" aria-labelledby="recently-played-title">
                <small>{isFeaturedGame ? 'Featured today' : 'Recently played'}</small>
                <a href={gameHighlight.href}>
                  <img src={gameHighlight.image} alt="" draggable="false" />
                  <strong id="recently-played-title">{gameHighlight.title}</strong>
                  <span>{gameHighlight.summary}</span><b>{isFeaturedGame ? 'Play now →' : 'Continue →'}</b>
                </a>
              </section>

              <nav className="player-home-card player-home-card--account" aria-label="Account shortcuts">
                <a className="player-home-shortcut player-home-shortcut--recharge" href="/home/rand"><span aria-hidden="true">＋</span><strong>Recharge</strong><small>Add Rand to your balance</small></a>
                <a className="player-home-account-link" href="/home/settings"><span aria-hidden="true">⚙</span><strong>Account settings</strong></a>
                <a className="player-home-account-link" href="/home/history"><span aria-hidden="true">↺</span><strong>History</strong></a>
              </nav>
            </section>
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
