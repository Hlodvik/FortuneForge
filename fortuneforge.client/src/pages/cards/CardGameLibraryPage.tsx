import { useEffect, useState } from 'react'
import type { AccountSummary } from '../../features/account/services/accountsApi'
import { GameTypeMenu } from '../../components/GameTypeMenu'
import blackjackPreview from '../../assets/cards/previews/blackjack-game.png'
import holdemPreview from '../../assets/cards/previews/holdem-game.png'
import solitairePreview from '../../assets/cards/previews/solitaire-game.png'
import { CardRoomNavigation } from './CardRoomNavigation'
import '../index.css'

export type CardGameAvailabilityState = 'checking' | 'available' | 'unavailable'

export type CardGameAvailability = Readonly<{
  blackjack: CardGameAvailabilityState
  texasHoldem: CardGameAvailabilityState
  solitaire: CardGameAvailabilityState
}>

export function CardGameLibraryPage({
  account,
  demoMode = false,
  availability,
}: {
  account?: AccountSummary
  demoMode?: boolean
  availability?: CardGameAvailability
}) {
  const blackjackState = availability?.blackjack ?? 'checking'
  const holdemState = availability?.texasHoldem ?? 'checking'
  const solitaireState = availability?.solitaire ?? 'checking'
  const [balanceCredits, setBalanceCredits] = useState(account?.balances.slotsCredits ?? 0)

  useEffect(() => {
    if (account) setBalanceCredits(account.balances.slotsCredits)
  }, [account])

  return (
    <div className="player-page game-picker-page">
      {demoMode ? (
        <header className="landing-bar">
          <a className="landing-brand" href="/" aria-label="Fortune Forge home">
            <span className="landing-brand__spark" aria-hidden="true">✦</span>
            <span>Fortune Forge</span>
          </a>
          <div className="landing-bar__account">
            <span className="demo-mode-badge">Card game preview</span>
            <a className="landing-nav__link" href="/login">Log in</a>
          </div>
        </header>
      ) : account ? (
        <CardRoomNavigation playerName={account.playerName} balanceCredits={balanceCredits} showOtherGames={false}
          onBalanceChange={setBalanceCredits} />
      ) : null}

      <div className={demoMode ? '' : 'game-hub-layout'}>
        {!demoMode && <GameTypeMenu active="cards" />}
        <main className={`${demoMode ? '' : 'game-hub-content '}game-picker-main`}>
          {demoMode && <GameTypeMenu active="cards" demoMode />}
          <section className="game-picker-heading game-picker-heading--compact" aria-labelledby="card-picker-title">
            <p className="account-eyebrow">Table room</p>
            <h1 id="card-picker-title">Choose your card game</h1>
          </section>

          <section className="card-game-library" aria-label="Available card games">
            <CardGameCard
              available={!demoMode && blackjackState === 'available'}
              description="A five-seat Blackjack table with adjustable wagers, visible turns, and continuous rounds."
              href="/cards/blackjack"
              image={blackjackPreview}
              title="Fortune Blackjack"
              unavailableLabel={demoMode ? 'Internal route only' : undefined}
            />
            <CardGameCard
              available={!demoMode && holdemState === 'available'}
              description="Multi-seat poker with private cards, a live pot, and play that continues hand after hand."
              href="/cards/texas-holdem"
              image={holdemPreview}
              title="Texas Hold’em"
              unavailableLabel={demoMode ? 'Internal route only' : undefined}
            />
            <CardGameCard
              available={!demoMode && solitaireState === 'available'}
              description="Build each foundation from Ace through King in a classic Klondike deal."
              href="/cards/solitaire"
              image={solitairePreview}
              title="Competitive Solitaire"
              unavailableLabel={demoMode ? 'Internal route only' : undefined}
            />
            {!demoMode && <CardGameCard
              available
              description="A four-player trick-taking match where the lowest score wins."
              href="/cards/hearts"
              mark="♥"
              title="Hearts"
            />}
          </section>
        </main>
      </div>
    </div>
  )
}

function CardGameCard({
  available,
  description,
  href,
  image,
  mark,
  title,
  unavailableLabel,
}: {
  available: boolean
  description: string
  href: string
  image?: string
  mark?: string
  title: string
  unavailableLabel?: string
}) {
  const className = `machine-card card-game-card ${available ? 'machine-card--available' : 'machine-card--coming'}`
  const content = <>
    {image && <img className="card-game-card__preview" src={image} alt="" loading="lazy" decoding="async" />}
    {mark && <span className="compact-placeholder-card__mark" aria-hidden="true">{mark}</span>}
    <strong>{title}</strong>
    <p>{description}</p>
    {!available && unavailableLabel && <span className="machine-card__action machine-card__action--disabled">{unavailableLabel}</span>}
  </>

  return available
    ? <a className={className} href={href} aria-label={`Play game: ${title}`}>{content}</a>
    : <article className={className}>{content}</article>
}
