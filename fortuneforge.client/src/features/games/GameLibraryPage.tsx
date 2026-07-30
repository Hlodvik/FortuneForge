import type { CSSProperties } from 'react'
import type { AccountSummary } from '../landing/services/accountsApi'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import '../landing/landing.css'
import { SLOT_GAME_CATALOG, type SlotGameCatalogEntry } from './gameCatalog'

export function GameLibraryPage({ account }: { account: AccountSummary }) {
  return (
    <div className="player-page game-picker-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          <ForgeCreditAmount amount={account.balances.slotsCredits} />
          <PaymentAlertsMenu />
          <a className="landing-nav__link" href="/home">Home</a>
        </div>
      </header>

      <main className="game-picker-main">
        <section className="game-picker-heading" aria-labelledby="game-picker-title">
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
  )
}

export function DemoGameLibraryPage() {
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
    `machine-card--${game.imagePresentation}`,
    game.playHref === null ? 'machine-card--coming' : 'machine-card--available',
  ].join(' ')
  const content = (
    <>
      <span className="machine-card__art" style={artStyle} aria-hidden="true">
        <img src={game.image} alt="" draggable="false" />
      </span>
      <span className="machine-card__status">
        {demoMode ? 'No-account demo' : game.playHref === null ? 'Coming soon' : 'Ready to play'}
      </span>
      <strong>{game.title}</strong>
      <p>{game.description}</p>
      <span className="machine-card__action">
        {demoMode ? 'Play demo' : game.playHref === null ? 'In the forge' : 'Choose machine'}
        {game.playHref !== null && <span aria-hidden="true"> →</span>}
      </span>
    </>
  )

  if (demoMode) {
    return <a className={cardClassName} href={game.demoHref}>{content}</a>
  }

  return game.playHref === null
    ? <article className={cardClassName}>{content}</article>
    : <a className={cardClassName} href={game.playHref}>{content}</a>
}
