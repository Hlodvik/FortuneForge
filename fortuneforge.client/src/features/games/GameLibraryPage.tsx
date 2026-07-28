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

function MachineCard({ game }: { game: SlotGameCatalogEntry }) {
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
        {game.playHref === null ? 'Coming soon' : 'Ready to play'}
      </span>
      <strong>{game.title}</strong>
      <p>{game.description}</p>
      <span className="machine-card__action">
        {game.playHref === null ? 'In the forge' : 'Choose machine'}
        {game.playHref !== null && <span aria-hidden="true"> →</span>}
      </span>
    </>
  )

  return game.playHref === null
    ? <article className={cardClassName}>{content}</article>
    : <a className={cardClassName} href={game.playHref}>{content}</a>
}
