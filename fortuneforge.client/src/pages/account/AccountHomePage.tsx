import { useEffect, useRef, useState, type ReactNode } from 'react'
import '@fontsource-variable/sora'
import heroArtwork from '../../assets/branding/fortune-forge-hero-v1.webp'
import { PlayerAccountMenus } from '../../components/PlayerHeader'
import { GameReportControl } from '../../components/GameReportControl'
import { GameCatalogueRail } from '../../components/GameCatalogueRail'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useCatalogueVisibility } from '../../features/catalog/catalogVisibility'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import { orderByCataloguePopularity, useCataloguePopularity } from '../../features/catalog/catalogPopularity'
import { useRecentGameActivity } from '../../features/catalog/recentGameActivity'
import { HOME_CATEGORIES, HOME_GAME_CATALOG, parseHomeCategory, type CategoryFilter, type LobbyGame } from './homeGameCatalog'
import './homeLobby.css'

const catalogueAnchor = 'home-lobby-games-title'
const catalogueHref = `/home#${catalogueAnchor}`

function initialCategory(): CategoryFilter {
  return parseHomeCategory(typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('category'))
}

export function HomePage() {
  const [returnPath] = useState(() => typeof window === 'undefined' ? '/home'
    : `/home${window.location.search}${window.location.hash}`)
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)
  const recent = useRecentGameActivity(account?.userId)
  const popularity = useCataloguePopularity()
  const visibility = useCatalogueVisibility()
  const [category, setCategory] = useState<CategoryFilter>(initialCategory)
  const helpDialog = useRef<HTMLDialogElement>(null)
  const visibleGames = HOME_GAME_CATALOG.filter(game => visibility.isVisible(game.href))
  const orderedGames = orderByCataloguePopularity(visibleGames, popularity.games)
  const categoryGames = orderedGames.filter(game => category === 'Popular' || game.category === category)
  const gameHighlight = [...recent.games].sort((left, right) => Date.parse(right.playedAtUtc) - Date.parse(left.playedAtUtc))
    .flatMap(play => visibleGames.find(game => game.id === play.gameId) ?? [])[0] ?? null
  const openHelp = () => helpDialog.current?.showModal()
  const browseAll = () => setCategory('Popular')
  const accountId = account?.userId

  useEffect(() => {
    if (!accountId || isLoading || window.location.hash !== `#${catalogueAnchor}`) return
    document.getElementById(catalogueAnchor)?.scrollIntoView?.({ block: 'start' })
  }, [accountId, isLoading])

  return (
    <div className="home-lobby">
      <aside className="home-lobby__sidebar">
        <a className="home-lobby__brand" href="/home" aria-label="Fortune Forge home">
          <svg className="home-lobby__brand-spade" viewBox="0 0 48 48" fill="currentColor" stroke="none" aria-hidden="true" focusable="false">
            <path d="M24 2C20 11 5 17 5 27c0 9 10 13 17 5-1 7-5 11-10 13h24c-5-2-9-6-10-13 7 8 17 4 17-5C43 17 28 11 24 2Z" />
          </svg>
          <strong>FORTUNE<br />FORGE</strong>
        </a>
        <nav className="home-lobby__navigation" aria-label="Player navigation">
          <LobbyNav href="/home" icon="home" current>Home</LobbyNav>
          <LobbyNav href={catalogueHref} icon="games" onClick={browseAll}>Games</LobbyNav>
          <LobbyNav href="/home/history" icon="history">History</LobbyNav>
          <LobbyNav href="/home/settings" icon="account">Account</LobbyNav>
        </nav>
        <button className="home-lobby__help" type="button" aria-label="Help" onClick={openHelp}><LobbyIcon name="help" /> <span>Help</span></button>
      </aside>
      <main className="home-lobby__main">
        <header className="home-lobby__topbar">
          {account && <a className="home-lobby__header-balance" href="/home/rand" aria-label={`Balance R${account.balances.slotsCredits.toFixed(2)}. Add funds`}><span>Balance</span><strong>R{account.balances.slotsCredits.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</strong></a>}
          {account && <div className="home-lobby__account"><span className="home-lobby__avatar" aria-hidden="true">{account.playerName.slice(0, 1).toLocaleUpperCase()}</span><PlayerAccountMenus account={account} showBalance={false} /></div>}
        </header>
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && <AccountError message={error} onRetry={reload} />}
        {!isLoading && account !== null && (
          <section className="home-lobby__dashboard" aria-label={`${account.playerName}'s game dashboard`}>
            <section className="home-lobby__welcome">
              <img className="home-lobby__hero-art" src={heroArtwork} alt="" width="1600" height="640" decoding="async" fetchPriority="high" />
              <div className="home-lobby__greeting"><h1>Welcome back, <span className="home-lobby__player-name">{account.playerName}</span></h1></div>
            </section>

            {visibility.error && <p className="home-lobby__notice" role="alert">{visibility.error}</p>}
            {gameHighlight && <section className="home-lobby__spotlights" aria-label="Last played game">
              <a className="home-lobby__continue" href={gameHighlight.href}>
                <LobbyArtwork game={gameHighlight} priority />
                <div><small>Last played</small><h2>{gameHighlight.title}</h2><span className="home-lobby__gold-button">Play again <span aria-hidden="true">→</span></span></div>
              </a>
            </section>}
            {recent.error && <p className="home-lobby__inline-notice" role="status">Your recent play couldn’t be loaded. <button type="button" onClick={recent.refresh}>Try again</button></p>}

            <section className="home-lobby__games" aria-labelledby="home-lobby-games-title">
              <div className="home-lobby__section-heading"><h2 id="home-lobby-games-title">Games</h2></div>
              <div className="home-lobby__filters" role="group" aria-label="Filter games">{HOME_CATEGORIES.map(item => <button key={item} type="button" aria-pressed={category === item} onClick={() => setCategory(item)}>{item}</button>)}</div>
              {categoryGames.length ? category === 'Popular'
                ? <GameCatalogueRail label={category} resetKey={category}>{categoryGames.map(game => <LobbyGameCard key={game.href} game={game} />)}</GameCatalogueRail>
                : <ul className="home-lobby__game-grid" aria-label={category}>{categoryGames.map(game => <li key={game.href}><LobbyGameCard game={game} /></li>)}</ul>
                : <p className="home-lobby__notice" role="status">{visibility.loading ? 'Checking the catalogue…' : 'No games are currently available in this category.'}</p>}
              {popularity.error && <p className="home-lobby__inline-notice" role="status">{popularity.games === null ? 'Popular order is unavailable. Games are listed by name.' : 'Popular order couldn’t be refreshed.'} <button type="button" onClick={popularity.reload}>Try again</button></p>}
            </section>

          </section>
        )}
      </main>
      <dialog className="home-lobby__help-dialog" ref={helpDialog} aria-labelledby="home-lobby-help-title"><form method="dialog"><button className="home-lobby__help-close" aria-label="Close help">×</button></form><small>A little help</small><h2 id="home-lobby-help-title">What can we help with?</h2><p>Each game has its own how-to-play guide. For your account and payments, start here.</p><a href="/home/settings">Account settings <span aria-hidden="true">→</span></a><a href="/home/rand">Funds and withdrawals <span aria-hidden="true">→</span></a><a href="/home/history">Your game history <span aria-hidden="true">→</span></a><GameReportControl gameTitle="the player lobby" /></dialog>
    </div>
  )
}

function LobbyGameCard({ game }: { game: LobbyGame }) {
  return <a className="home-lobby__game-card" href={game.href}><LobbyArtwork game={game} /><strong>{game.title}</strong></a>
}

function LobbyArtwork({ game, priority = false }: { game: LobbyGame; priority?: boolean }) {
  const artwork = getCataloguePresentationArt(game.href)
  const src = artwork?.src ?? game.image
  return src ? <img className={`home-lobby__game-art${game.imagePresentation === 'contain' && !artwork ? ' home-lobby__game-art--contain' : ''}`} src={src} alt="" width={artwork?.width ?? 768} height={artwork?.height ?? 384} loading={priority ? 'eager' : 'lazy'} decoding="async" draggable="false" /> : <span className="home-lobby__art-fallback" aria-hidden="true">✧</span>
}

function LobbyNav({ href, icon, current, children, onClick }: { href: string; icon: string; current?: boolean; children: ReactNode; onClick?: () => void }) {
  return <a href={href} onClick={onClick} aria-label={typeof children === 'string' ? children : undefined} aria-current={current ? 'page' : undefined}><LobbyIcon name={icon} /><span>{children}</span></a>
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
