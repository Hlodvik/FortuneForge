import type { CSSProperties } from 'react'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameTypeMenu } from '../../components/GameTypeMenu'
import { PlayerHeader } from '../../components/PlayerHeader'
import { SLOT_GAME_CATALOG, type SlotGameCatalogEntry } from '../../games/slots/catalog'
import '../index.css'

export function SlotsLibraryPage({ account }: { account: AccountSummary }) {
  return (
    <div className="player-page game-picker-page">
      <PlayerHeader account={account} />
      <div className="game-hub-layout">
        <GameTypeMenu active="slots" />
        <main className="game-hub-content game-picker-main">
        <section className="game-picker-heading game-picker-heading--compact" aria-labelledby="game-picker-title">
          <p className="account-eyebrow">Slot collection</p>
          <h1 id="game-picker-title">Choose your machine</h1>
          <p>Welcome, {account.playerName}. Pick a realm and see what fortune has waiting.</p>
        </section>

        <section className="machine-library" aria-label="Available slot machines">
          {SLOT_GAME_CATALOG.map((game) => (
            <MachineCard game={game} key={game.id} />
          ))}
        </section>
        </main>
      </div>
    </div>
  )
}

export function DemoSlotsLibraryPage() {
  return (
    <div className="player-page game-picker-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          <span className="demo-mode-badge">Demo · R10,000</span>
          <a className="landing-nav__link" href="/login">Log in</a>
        </div>
      </header>

      <main className="game-picker-main">
        <GameTypeMenu active="slots" demoMode />
        <section className="game-picker-heading" aria-labelledby="demo-picker-title">
          <p className="account-eyebrow">No-account demo</p>
          <h1 id="demo-picker-title">Choose your demo.</h1>
          <p>Each machine starts with a local R10,000 demo wallet. Demo play never uses or changes an account balance.</p>
        </section>

        <section className="machine-library" aria-label="Available slot machine demos">
          {SLOT_GAME_CATALOG.map((game) => (
            <MachineCard demoMode game={game} key={game.id} />
          ))}
        </section>
      </main>
    </div>
  )
}

function MachineCard({ game, demoMode = false }: { game: SlotGameCatalogEntry; demoMode?: boolean }) {
  const artStyle = game.slotDivBackgroundImage
    ? { '--machine-card-slot-background': `url("${game.slotDivBackgroundImage}")` } as CSSProperties
    : undefined
  const cardClassName = [
    'machine-card',
    'machine-card--slot',
    `machine-card--${game.imagePresentation}`,
    game.playHref === null ? 'machine-card--coming' : 'machine-card--available',
  ].join(' ')
  const href = demoMode ? game.demoHref : game.playHref
  const actionLabel = demoMode ? 'Play demo' : game.playHref === null ? 'In the forge' : 'Play game'
  const statusLabel = demoMode ? 'No-account demo' : game.playHref === null ? 'Coming soon' : null

  return (
    <article className={cardClassName}>
      <span className="machine-card__art" style={artStyle} aria-hidden="true">
        <img src={game.image} alt="" draggable="false" />
      </span>
      {statusLabel && <span className="machine-card__status">{statusLabel}</span>}
      <strong>{game.title}</strong>
      <p>{game.description}</p>
      {href === null ? (
        <span className="machine-card__action machine-card__action--disabled">{actionLabel}</span>
      ) : (
        <a className="machine-card__action" href={href} aria-label={`${actionLabel}: ${game.title}`}>
          {actionLabel}<span aria-hidden="true"> →</span>
        </a>
      )}
    </article>
  )
}
