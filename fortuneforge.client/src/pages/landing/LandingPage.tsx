import { useEffect, useState, type ReactNode } from 'react'
import heroImage from '../../assets/branding/fortune-forge-hero-v1.webp'
import { useOptionalAccountSession } from '../../features/account/useOptionalAccountSession'
import { useCatalogueVisibility } from '../../features/catalog/catalogVisibility'
import { getCataloguePresentationArt } from '../../features/catalog/cataloguePresentationArt'
import '@fontsource-variable/sora'
import './landingRedesign.css'

type Category = 'All games' | 'Slots' | 'Card games' | 'Table & dice' | 'Puzzles'
const categories: readonly Category[] = ['All games', 'Slots', 'Card games', 'Table & dice', 'Puzzles']
const discoveries: ReadonlyArray<{ title: string; route: string; category: Category; label?: string }> = [
  { title: 'Fortune Blackjack', route: '/cards/blackjack', category: 'Card games' },
  { title: 'Dragon Tiger', route: '/games/dragon-tiger', category: 'Card games' },
  { title: 'Mines', route: '/games/mines', category: 'All games', label: 'Casino game' },
  { title: 'Cosmic Fortune', route: '/slots/cosmic-fortune', category: 'Slots' },
  { title: 'Baccarat', route: '/games/baccarat', category: 'Card games' },
  { title: 'Roulette', route: '/games/roulette', category: 'Table & dice' },
  { title: 'Craps', route: '/games/craps', category: 'Table & dice' },
  { title: 'Rainbow Realm', route: '/slots/rainbow-realm', category: 'Slots' },
  { title: '2048', route: '/games/2048', category: 'Puzzles' },
  { title: 'Drop Merge', route: '/games/drop-merge', category: 'Puzzles' },
]

export function LandingPage() {
  const { account, isLoading: isCheckingSession } = useOptionalAccountSession()
  const visibility = useCatalogueVisibility()
  const [category, setCategory] = useState<Category>('All games')
  const games = discoveries.filter(game => visibility.isVisible(game.route)
    && (category === 'All games' || game.category === category)).slice(0, 4)

  useEffect(() => {
    if (account !== null) window.location.replace('/home')
  }, [account])

  if (account !== null) {
    return <main className="ff-landing ff-landing--opening" role="status">Opening your home…</main>
  }

  return (
    <div className="ff-landing">
      <a className="ff-landing__skip" href="#landing-main">Skip to content</a>
      <header className="ff-landing__header">
        <a className="ff-landing__brand" href="/" aria-label="Fortune Forge home">
          <ForgeStar /><span>Fortune Forge</span>
        </a>
        <nav className="ff-landing__nav" aria-label="Primary navigation">
          <a className="ff-landing__games-link" href="#discover-games">Games</a>
          {!isCheckingSession && <a className="ff-landing__login" href="/login">Log in</a>}
          <a className="ff-landing__button ff-landing__button--small" href="/create-account">Create account</a>
        </nav>
      </header>

      <main id="landing-main">
        <section className="ff-landing__hero" aria-labelledby="landing-title">
          <div className="ff-landing__hero-art" aria-hidden="true">
            <img src={heroImage} alt="" width={1600} height={640} fetchPriority="high" decoding="async" />
          </div>
          <div className="ff-landing__hero-copy">
            <p className="ff-landing__eyebrow">Find your favourite</p>
            <h1 id="landing-title">Your next game <span>starts here.</span></h1>
            <p className="ff-landing__lede">Discover cards, slots and more. Find your next favourite at Fortune Forge.</p>
            <div className="ff-landing__hero-actions">
              <a className="ff-landing__button" href="#discover-games">Explore games <Arrow /></a>
              <a className="ff-landing__text-link" href="/create-account">Create an account</a>
            </div>
          </div>
        </section>

        <section className="ff-landing__discover" id="discover-games" aria-labelledby="discover-title">
          <nav className="ff-landing__categories" aria-label="Game categories">
            {categories.map(item => (
              <button type="button" key={item} className={category === item ? 'is-selected' : ''}
                aria-pressed={category === item} onClick={() => setCategory(item)}>
                <CategoryIcon category={item} /><span>{item}</span>
              </button>
            ))}
          </nav>
          <div className="ff-landing__section-heading">
            <h2 id="discover-title">Find your next favourite</h2>
            <a href="/games">View all games <Arrow /></a>
          </div>
          <div className="ff-landing__game-grid" aria-live="polite" aria-busy={visibility.loading}>
            {games.map(game => {
              const artwork = getCataloguePresentationArt(game.route)!
              return <a className="ff-landing__game-card" key={game.route} href={game.route} aria-label={'Play ' + game.title}>
                <div className="ff-landing__game-art">
                  <img src={artwork.src} width={artwork.width} height={artwork.height} alt="" loading="lazy" decoding="async" />
                  <span className="ff-landing__play-icon" aria-hidden="true"><Arrow /></span>
                </div>
                <strong>{game.title}</strong><span>{game.label ?? game.category}</span>
              </a>
            })}
            {games.length === 0 && (
              <p className="ff-landing__empty" role="status">
                {visibility.error ? 'Games could not be checked. Please reload to try again.'
                  : visibility.loading ? 'Finding your next game…' : 'No games in this category right now. Try another category.'}
              </p>
            )}
          </div>
        </section>

        <section className="ff-landing__invite" aria-labelledby="invite-title">
          <div><h2 id="invite-title">Your seat is waiting.</h2><p>Create an account to get started.</p></div>
          <a className="ff-landing__button" href="/create-account">Create account <Arrow /></a>
        </section>
      </main>
      <footer className="ff-landing__footer">
        <a className="ff-landing__brand" href="/"><ForgeStar /><span>Fortune Forge</span></a>
        <nav aria-label="Footer navigation">
          <a href="/login">Log in</a><a href="/create-account">Create account</a>
        </nav>
      </footer>
    </div>
  )
}

function ForgeStar() {
  return <svg viewBox="0 0 40 40" width="38" height="38" fill="currentColor" aria-hidden="true">
    <path d="m20 1 5.6 13.4L39 20l-13.4 5.6L20 39l-5.6-13.4L1 20l13.4-5.6Z" />
    <path d="m20 11 2.7 6.3L29 20l-6.3 2.7L20 29l-2.7-6.3L11 20l6.3-2.7Z" fill="#0b0c10" opacity=".44" />
  </svg>
}

function Arrow() {
  return <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
}

function CategoryIcon({ category }: { category: Category }) {
  const paths: Record<Category, ReactNode> = {
    'All games': <><rect x="3" y="3" width="6" height="6" rx="1" /><rect x="15" y="3" width="6" height="6" rx="1" /><rect x="3" y="15" width="6" height="6" rx="1" /><rect x="15" y="15" width="6" height="6" rx="1" /></>,
    Slots: <><rect x="4" y="3" width="16" height="18" rx="3" /><path d="M8 8v6m4-6v6m4-6v6M7 18h10" /></>,
    'Card games': <><rect x="8" y="3" width="11" height="17" rx="2" transform="rotate(12 13.5 11.5)" /><path d="m6 6-3 1 3 14 7-1m0-10 2 3-2 2-2-2Z" /></>,
    'Table & dice': <><rect x="4" y="5" width="15" height="15" rx="3" /><path d="M7 10h.01M11 14h.01M15 18h.01M10 5V3h12v12h-3" /></>,
    Puzzles: <><path d="m3 8 4-5h10l4 5-9 13Z M3 8h18M7 3l5 18 5-18" /></>,
  }
  return <svg viewBox="0 0 24 24" width="25" height="25" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[category]}</svg>
}
