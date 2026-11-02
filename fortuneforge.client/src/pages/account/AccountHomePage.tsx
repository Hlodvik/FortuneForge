import { ResponsiveSlotImage } from '../../features/slots/components/ResponsiveSlotImage'
import { lazy, Suspense, useCallback, useEffect, useRef, useState, type MouseEvent, type SyntheticEvent } from 'react'
import heroArtwork from '../../assets/branding/fortune-forge-hero-v1.webp'
import { GameCatalogueRail } from '../../components/GameCatalogueRail'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { useCatalogueVisibility } from '../../features/catalog/catalogVisibility'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import { orderByCataloguePopularity, useCataloguePopularity } from '../../features/catalog/catalogPopularity'
import { useRecentGameActivity } from '../../features/catalog/recentGameActivity'
import { HOME_CATEGORIES, HOME_GAME_CATALOG, parseHomeCategory, type CategoryFilter, type LobbyGame } from './homeGameCatalog'
import { AccountLobbyShell } from './AccountLobbyShell'
import './accountOverlay.css'

const catalogueAnchor = 'home-lobby-games-title'
const AccountSettingsContent = lazy(() => import('./AccountSettingsPage').then(module => ({ default: module.AccountSettingsContent })))
const AccountHistoryContent = lazy(() => import('./AccountHistoryPage').then(module => ({ default: module.AccountHistoryContent })))

type HomeOverlay = 'history' | 'settings' | null

function overlayFromPath(pathname: string): HomeOverlay {
  if (pathname === '/home/history') return 'history'
  if (pathname === '/home/settings') return 'settings'
  return null
}

function initialCategory(): CategoryFilter {
  return parseHomeCategory(typeof window === 'undefined' ? null : new URLSearchParams(window.location.search).get('category'))
}

export function HomePage() {
  const [returnPath] = useState(() => typeof window === 'undefined' ? '/home'
    : `${window.location.pathname}${window.location.search}${window.location.hash}`)
  const { account, error, isLoading, reload } = useAuthenticatedAccount(returnPath)
  const [overlay, setOverlay] = useState<HomeOverlay>(() => typeof window === 'undefined' ? null : overlayFromPath(window.location.pathname))
  const isOverlayOpen = overlay !== null
  const overlayDialog = useRef<HTMLDialogElement>(null)
  const recent = useRecentGameActivity(account?.userId)
  const popularity = useCataloguePopularity()
  const visibility = useCatalogueVisibility()
  const [category, setCategory] = useState<CategoryFilter>(initialCategory)
  const visibleGames = HOME_GAME_CATALOG.filter(game => visibility.isVisible(game.href))
  const orderedGames = orderByCataloguePopularity(visibleGames, popularity.games)
  const categoryGames = orderedGames.filter(game => category === 'Popular' || game.category === category)
  const recentGames = [...recent.games]
    .filter(play => Number.isFinite(Date.parse(play.playedAtUtc)))
    .sort((left, right) => Date.parse(right.playedAtUtc) - Date.parse(left.playedAtUtc) || left.gameId.localeCompare(right.gameId))
    .flatMap(play => visibleGames.find(game => game.id === play.gameId) ?? [])
    .filter((game, index, games) => games.findIndex(candidate => candidate.id === game.id) === index)
  const gameHighlight = recentGames[0] ?? null
  const otherRecentGames = recentGames.slice(1)
  const accountId = account?.userId

  const openOverlay = useCallback((next: Exclude<HomeOverlay, null>) => {
    const path = `/home/${next}`
    if (window.location.pathname === path) {
      setOverlay(next)
      return
    }
    const existingState = window.history.state && typeof window.history.state === 'object' ? window.history.state : {}
    if (window.location.pathname === '/home') {
      window.history.pushState({ ...existingState, fortuneForgeHomeOverlay: true }, '', path)
    } else {
      window.history.replaceState(existingState, '', path)
    }
    setOverlay(next)
  }, [])

  const closeOverlay = useCallback(() => {
    if (window.history.state?.fortuneForgeHomeOverlay) {
      window.history.back()
    } else {
      window.history.replaceState(window.history.state, '', '/home')
    }
    setOverlay(null)
  }, [])

  useEffect(() => {
    const syncOverlay = () => setOverlay(overlayFromPath(window.location.pathname))
    window.addEventListener('popstate', syncOverlay)
    return () => window.removeEventListener('popstate', syncOverlay)
  }, [])

  useEffect(() => {
    const dialog = overlayDialog.current
    if (!dialog) return
    if (overlay) {
      if (!dialog.open) dialog.showModal()
      dialog.querySelector<HTMLButtonElement>('.home-lobby__overlay-close')?.focus()
    }
    if (!overlay && dialog.open) dialog.close()
    document.title = overlay === 'history' ? 'User History — Fortune Forge'
      : overlay === 'settings' ? 'Account Settings — Fortune Forge'
        : 'Home — Fortune Forge'
  }, [overlay])

  useEffect(() => {
    if (!isOverlayOpen) return undefined
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.body.style.overflow = previousOverflow }
  }, [isOverlayOpen])

  function handleOverlayBackdrop(event: MouseEvent<HTMLDialogElement>) {
    if (event.target !== event.currentTarget) return
    const bounds = event.currentTarget.getBoundingClientRect()
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) closeOverlay()
  }

  function handleOverlayCancel(event: SyntheticEvent<HTMLDialogElement>) {
    event.preventDefault()
    closeOverlay()
  }

  useEffect(() => {
    if (!accountId || isLoading || window.location.hash !== `#${catalogueAnchor}`) return
    document.getElementById(catalogueAnchor)?.scrollIntoView?.({ block: 'start' })
  }, [accountId, isLoading])

  return (
    <AccountLobbyShell account={account} activeNav={overlay === 'history' ? 'history' : overlay === 'settings' ? 'account' : 'home'} onOpenOverlay={openOverlay} homeLayout>
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && <AccountError message={error} onRetry={reload} />}
        {!isLoading && account !== null && (
          <section className="home-lobby__dashboard" aria-label={`${account.playerName}'s game dashboard`}>
            <section className="home-lobby__welcome">
              <img className="home-lobby__hero-art" src={heroArtwork} alt="" width="1600" height="640" decoding="async" fetchPriority="high" />
              <div className="home-lobby__greeting"><h1>Welcome back, <span className="home-lobby__player-name">{account.playerName}</span></h1></div>
            </section>

            {visibility.error && <p className="home-lobby__notice" role="alert">{visibility.error}</p>}
            {gameHighlight && <section className={`home-lobby__spotlights${otherRecentGames.length ? ' home-lobby__spotlights--with-recent' : ''}`} aria-label="Last played game">
              <a className="home-lobby__featured-art" href={gameHighlight.href} aria-label={`Open ${gameHighlight.title}`}>
                <LobbyArtwork game={gameHighlight} priority />
              </a>
              <div className="home-lobby__spotlight-content">
                <div className="home-lobby__spotlight-copy"><small>Last played</small><h2>{gameHighlight.title}</h2><a className="home-lobby__continue home-lobby__gold-button" href={gameHighlight.href} aria-label={`Play ${gameHighlight.title} again`}>Play again <span aria-hidden="true">→</span></a></div>
                {otherRecentGames.length > 0 && <section className="home-lobby__recent" aria-labelledby="home-lobby-recent-title">
                  <h3 id="home-lobby-recent-title">Recently played</h3>
                  <GameCatalogueRail label="Recently played" className="home-lobby__recent-rail" resetKey={gameHighlight.id}>
                    {otherRecentGames.map(game => <a className="home-lobby__recent-game" key={game.href} href={game.href} aria-label={game.title}><LobbyArtwork game={game} /></a>)}
                  </GameCatalogueRail>
                </section>}
              </div>
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
        <dialog ref={overlayDialog} className={`home-lobby__overlay home-lobby__overlay--${overlay ?? 'closed'}`} aria-label={overlay === 'history' ? 'Game history' : 'Account settings'} onClick={handleOverlayBackdrop} onCancel={handleOverlayCancel}>
          <button className="home-lobby__overlay-close" type="button" aria-label="Close panel" onClick={closeOverlay}>×</button>
          {account !== null && overlay !== null && <Suspense fallback={<div className="home-lobby__notice" role="status">Opening…</div>}>
            {overlay === 'history' ? <AccountHistoryContent account={account} /> : <AccountSettingsContent account={account} reload={reload} />}
          </Suspense>}
          {account === null && <div className="home-lobby__notice" role={error ? 'alert' : 'status'}>{error ?? 'Opening your account…'}{error && <button type="button" onClick={reload}>Try again</button>}</div>}
        </dialog>
    </AccountLobbyShell>
  )
}

function LobbyGameCard({ game }: { game: LobbyGame }) {
  return <a className="home-lobby__game-card" href={game.href}><LobbyArtwork game={game} /><strong>{game.title}</strong></a>
}

function LobbyArtwork({ game, priority = false }: { game: LobbyGame; priority?: boolean }) {
  const artwork = getCataloguePresentationArt(game.href)
  const src = artwork?.src ?? game.image
  return src ? <ResponsiveSlotImage imageSrcSet={artwork?.imageSrcSet} className={`home-lobby__game-art${game.imagePresentation === 'contain' && !artwork ? ' home-lobby__game-art--contain' : ''}`} src={src} alt="" width={artwork?.width ?? 768} height={artwork?.height ?? 384} loading={priority ? 'eager' : 'lazy'} decoding="async" draggable="false" /> : <span className="home-lobby__art-fallback" aria-hidden="true">✧</span>
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
