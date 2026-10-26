import { useRef, useState, type ReactNode } from 'react'
import heroArtwork from '../../assets/branding/fortune-forge-hero-v1.webp'
import { PlayerAccountMenus } from '../../components/PlayerHeader'
import { GameReportControl } from '../../components/GameReportControl'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { SLOT_GAME_CATALOG } from '../../games/slots/catalog'
import { useRecentSlotGame } from '../../games/slots/useRecentSlotGame'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import { useCatalogueVisibility } from '../../features/catalog/catalogVisibility'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import './homeLobby.css'

type GameCategory = 'Cards' | 'Casino' | 'Puzzles' | 'Slots'
type LobbyGame = {
  href: string
  title: string
  summary: string
  category: GameCategory
  image?: string
  imagePresentation?: 'contain' | 'cover'
}
type PlayedGame = LobbyGame & { playedAtUtc: string; active: boolean }

const nonSlotGames: readonly LobbyGame[] = [
  { href: '/cards/blackjack', title: 'Fortune Blackjack', category: 'Cards', summary: 'Your next hand is waiting.' },
  { href: '/games/dragon-tiger', title: 'Dragon Tiger', category: 'Casino', summary: 'One card each. Higher card wins.' },
  { href: '/games/mines', title: 'Mines', category: 'Casino', summary: 'Choose your path. Uncover what’s below.' },
  { href: '/cards/texas-holdem', title: 'Texas Hold’em', category: 'Cards', summary: 'Take your seat at the poker table.' },
  { href: '/cards/solitaire', title: 'Competitive Solitaire', category: 'Cards', summary: 'A classic deal. A fresh challenge.' },
  { href: '/cards/three-card-poker', title: 'Three Card Poker', category: 'Cards', summary: 'Three cards against the dealer.' },
  { href: '/cards/house-holdem', title: 'House Hold’em', category: 'Cards', summary: 'Your hand against the house.' },
  { href: '/cards/hearts', title: 'Hearts', category: 'Cards', summary: 'Keep your score low and read the table.' },
  { href: '/games/andar-bahar', title: 'Andar Bahar', category: 'Casino', summary: 'Pick a side and follow the cards.' },
  { href: '/games/hi-lo', title: 'Hi-Lo', category: 'Cards', summary: 'Will the next card be higher or lower?' },
  { href: '/games/baccarat', title: 'Baccarat', category: 'Casino', summary: 'A timeless table-game favourite.' },
  { href: '/games/casino-war', title: 'Casino War', category: 'Cards', summary: 'Turn over a card and face the dealer.' },
  { href: '/games/video-poker', title: 'Video Poker', category: 'Cards', summary: 'Hold your favourites. Draw your next hand.' },
  { href: '/games/roulette', title: 'Roulette', category: 'Casino', summary: 'Make your pick and watch the wheel.' },
  { href: '/games/craps', title: 'Craps', category: 'Casino', summary: 'A new roll. A new possibility.' },
  { href: '/games/keno', title: 'Keno', category: 'Casino', summary: 'Choose your numbers and follow the draw.' },
  { href: '/games/sic-bo', title: 'Sic Bo', category: 'Casino', summary: 'Three dice. Make your call.' },
  { href: '/games/liars-dice', title: 'Liar’s Dice', category: 'Casino', summary: 'Read the bids. Call the bluff.' },
  { href: '/games/2048', title: '2048', category: 'Puzzles', summary: 'Slide, combine and chase the next tile.' },
  { href: '/games/drop-merge', title: 'Drop Merge', category: 'Puzzles', summary: 'Drop a number. Build something bigger.' },
]
const cardRoutes: Readonly<Record<string, string>> = {
  blackjack: '/cards/blackjack', 'texas-holdem': '/cards/texas-holdem',
  'three-card-poker': '/cards/three-card-poker', 'house-holdem': '/cards/house-holdem',
  solitaire: '/cards/solitaire', 'hi-lo': '/games/hi-lo', 'andar-bahar': '/games/andar-bahar', mines: '/games/mines',
}
const categories = ['All games', 'Slots', 'Cards', 'Casino', 'Puzzles'] as const
type CategoryFilter = typeof categories[number]

export function HomePage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount()
  const recentSlot = useRecentSlotGame(account?.userId)
  const cardHistory = useCardRoomHistory()
  const visibility = useCatalogueVisibility()
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState<CategoryFilter>('All games')
  const helpDialog = useRef<HTMLDialogElement>(null)
  const slotGames: LobbyGame[] = SLOT_GAME_CATALOG.flatMap(game => game.playHref ? [{
    href: game.playHref, title: game.shortTitle, summary: game.description,
    category: 'Slots' as const, image: game.image, imagePresentation: game.imagePresentation,
  }] : [])
  const visibleGames = [...nonSlotGames, ...slotGames].filter(game => visibility.isVisible(game.href))
  const slotHistory = recentSlot.games ?? (recentSlot.game && recentSlot.playedAtUtc
    ? [{ game: recentSlot.game, playedAtUtc: recentSlot.playedAtUtc }] : [])
  const recentCandidates: PlayedGame[] = [
    ...cardHistory.activities.flatMap(activity => {
      const game = nonSlotGames.find(entry => entry.href === cardRoutes[activity.game])
      return game ? [{ ...game, title: activity.gameLabel,
        summary: activity.completedAtUtc === null ? activity.summary : activity.title,
        playedAtUtc: activity.completedAtUtc ?? activity.startedAtUtc, active: activity.completedAtUtc === null }] : []
    }),
    ...slotHistory.flatMap(({ game, playedAtUtc }) => game.playHref ? [{
      href: game.playHref, title: game.shortTitle, summary: 'Ready for another spin?',
      category: 'Slots' as const, image: game.image, imagePresentation: game.imagePresentation,
      playedAtUtc, active: false,
    }] : []),
  ]
  const seenRoutes = new Set<string>()
  const recentGames = recentCandidates.sort((left, right) => Date.parse(right.playedAtUtc) - Date.parse(left.playedAtUtc))
    .filter(game => {
      if (!visibility.isVisible(game.href) || seenRoutes.has(game.href)) return false
      seenRoutes.add(game.href); return true
    })
  const gameHighlight = recentGames[0] ?? visibleGames[0] ?? null
  const isFeaturedGame = recentGames.length === 0
  const browsing = query.trim().length > 0 || category !== 'All games'
  const filteredGames = visibleGames.filter(game => (category === 'All games' || game.category === category)
    && `${game.title} ${game.category}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()))
  const rowGames = browsing ? filteredGames : recentGames.length ? recentGames.slice(0, 4) : visibleGames.slice(0, 4)
  const openHelp = () => helpDialog.current?.showModal()

  return (
    <div className="home-lobby">
      <aside className="home-lobby__sidebar">
        <a className="home-lobby__brand" href="/home" aria-label="Fortune Forge home"><span aria-hidden="true">✧</span><strong>FORTUNE<br />FORGE</strong></a>
        <nav className="home-lobby__navigation" aria-label="Player navigation">
          <LobbyNav href="/home" icon="home" current>Home</LobbyNav>
          <LobbyNav href="/games" icon="games">Games</LobbyNav>
          <LobbyNav href="/home/history" icon="history">History</LobbyNav>
          <LobbyNav href="/home/settings" icon="account">Account</LobbyNav>
        </nav>
        <button className="home-lobby__help" type="button" aria-label="Help" onClick={openHelp}><LobbyIcon name="help" /> <span>Help</span></button>
      </aside>
      <main className="home-lobby__main">
        <header className="home-lobby__topbar">
          <label className="home-lobby__search"><LobbyIcon name="search" /><span className="home-lobby__sr-only">Find a game</span><input type="search" placeholder="Find a game" value={query} onChange={event => setQuery(event.target.value)} /></label>
          <button className="home-lobby__top-help" type="button" aria-label="Help" onClick={openHelp}><LobbyIcon name="help" /></button>
          {account && <div className="home-lobby__account"><span className="home-lobby__avatar" aria-hidden="true">{account.playerName.slice(0, 1).toLocaleUpperCase()}</span><PlayerAccountMenus account={account} showBalance={false} /></div>}
        </header>
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && <AccountError message={error} onRetry={reload} />}
        {!isLoading && account !== null && (
          <section className="home-lobby__dashboard" aria-label={`${account.playerName}'s game dashboard`}>
            <section className="home-lobby__welcome">
              <img className="home-lobby__hero-art" src={heroArtwork} alt="" width="1600" height="640" decoding="async" fetchPriority="high" />
              <div className="home-lobby__greeting"><h1>Welcome back<span className="home-lobby__sr-only">, {account.playerName}</span>.</h1></div>
              <section className="home-lobby__balance" aria-label="Your balance"><div><span>Your balance</span><strong>R{account.balances.slotsCredits.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</strong></div><a className="home-lobby__gold-button" href="/home/rand">Add funds</a><a className="home-lobby__history-link" href="/home/history">View history <span aria-hidden="true">→</span></a></section>
            </section>

            {visibility.error && <p className="home-lobby__notice" role="alert">{visibility.error}</p>}
            <section className="home-lobby__spotlights" aria-label="Game and account shortcuts">
              {gameHighlight ? <a className="home-lobby__continue" href={gameHighlight.href}>
                <LobbyArtwork game={gameHighlight} priority />
                <div><small>{isFeaturedGame ? 'Featured today' : 'Last played'}</small><h2>{gameHighlight.title}</h2><p>{gameHighlight.summary}</p><span className="home-lobby__gold-button">{isFeaturedGame ? 'Play now' : recentGames[0]?.active ? 'Continue' : 'Play again'} <span aria-hidden="true">→</span></span></div>
              </a> : <a className="home-lobby__continue home-lobby__continue--empty" href="/games"><div><small>Your next game</small><h2>Find your favourite</h2><p>{visibility.loading ? 'Checking the catalogue…' : 'Explore the games currently available.'}</p><span className="home-lobby__gold-button">Browse games <span aria-hidden="true">→</span></span></div></a>}
              <a className="home-lobby__browse" href="/games"><span className="home-lobby__browse-mark" aria-hidden="true">✧</span><div><h2>Browse all games <span aria-hidden="true">→</span></h2><p>Explore our full collection.</p></div></a>
            </section>

            <section className="home-lobby__games" aria-labelledby="home-lobby-games-title">
              <div className="home-lobby__section-heading"><h2 id="home-lobby-games-title">{browsing ? query.trim() ? 'Search results' : category : recentGames.length ? 'Recently played' : 'Find your next favourite'}</h2>{!browsing && recentGames.length > 0 && <a href="/home/history">View history <span aria-hidden="true">→</span></a>}</div>
              <div className="home-lobby__filters" role="group" aria-label="Filter games">{categories.map(item => <button key={item} type="button" aria-pressed={category === item} onClick={() => setCategory(item)}>{item === 'Casino' ? 'Casino & dice' : item}</button>)}</div>
              {rowGames.length ? <div className="home-lobby__game-grid">{rowGames.map(game => <a className="home-lobby__game-card" key={game.href} href={game.href}><LobbyArtwork game={game} /><strong>{game.title}</strong><span>{game.category}</span></a>)}</div>
                : <p className="home-lobby__notice" role="status">{visibility.loading ? 'Checking the catalogue…' : browsing ? 'No games match. Try another name or category.' : 'Games will appear here when they are available.'}</p>}
            </section>

          </section>
        )}
      </main>
      <dialog className="home-lobby__help-dialog" ref={helpDialog} aria-labelledby="home-lobby-help-title"><form method="dialog"><button className="home-lobby__help-close" aria-label="Close help">×</button></form><small>A little help</small><h2 id="home-lobby-help-title">What can we help with?</h2><p>Each game has its own how-to-play guide. For your account and payments, start here.</p><a href="/home/settings">Account settings <span aria-hidden="true">→</span></a><a href="/home/rand">Funds and withdrawals <span aria-hidden="true">→</span></a><a href="/home/history">Your game history <span aria-hidden="true">→</span></a><GameReportControl gameTitle="the player lobby" /></dialog>
    </div>
  )
}

function LobbyArtwork({ game, priority = false }: { game: LobbyGame; priority?: boolean }) {
  const artwork = getCataloguePresentationArt(game.href)
  const src = artwork?.src ?? game.image
  return src ? <img className={`home-lobby__game-art${game.imagePresentation === 'contain' && !artwork ? ' home-lobby__game-art--contain' : ''}`} src={src} alt="" width={artwork?.width ?? 768} height={artwork?.height ?? 384} loading={priority ? 'eager' : 'lazy'} decoding="async" draggable="false" /> : <span className="home-lobby__art-fallback" aria-hidden="true">✧</span>
}

function LobbyNav({ href, icon, current, children }: { href: string; icon: string; current?: boolean; children: ReactNode }) {
  return <a href={href} aria-label={typeof children === 'string' ? children : undefined} aria-current={current ? 'page' : undefined}><LobbyIcon name={icon} /><span>{children}</span></a>
}

function LobbyIcon({ name }: { name: string }) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{name === 'home' ? <><path d="m3 10 9-7 9 7v10H5V10" /><path d="M9 20v-7h6v7" /></> : name === 'games' ? <><path d="M7 7h10c3 0 5 11 2 12l-4-4H9l-4 4C2 18 4 7 7 7Z" /><path d="M7 9v5m-2.5-2.5h5m6.5-1h.01m2 3h.01" /></> : name === 'history' ? <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l4 2" /></> : name === 'account' ? <><circle cx="12" cy="7" r="4" /><path d="M5 21v-3c0-6 14-6 14 0v3Z" /></> : name === 'search' ? <><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></> : <><circle cx="12" cy="12" r="9" /><path d="M9.8 8a2.4 2.4 0 0 1 4.5 1.2c0 2-2.3 2-2.3 4M12 17h.01" /></>}</svg>
}

function AccountLoading({ label }: { label: string }) {
  return <div className="home-lobby__notice" role="status"><span aria-hidden="true">✧</span> {label}</div>
}

function AccountError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="home-lobby__notice" role="alert">
      <strong>Your account could not be opened.</strong><span>{message}</span>
      <button className="home-lobby__gold-button" type="button" onClick={onRetry}>Try again</button>
      <a href="/login">Return to login</a>
    </div>
  )
}
