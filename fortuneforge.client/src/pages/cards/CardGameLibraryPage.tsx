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
          <article className={`machine-card card-game-card ${!demoMode && blackjackState === 'available' ? 'machine-card--available' : 'machine-card--coming'}`}>
            <img className="card-game-card__preview" src={blackjackPreview} alt="" loading="lazy" decoding="async" />
            <span className="machine-card__status">{demoMode ? 'Internal preview' : availabilityLabel(blackjackState)}</span>
            <strong>Fortune Blackjack</strong>
            <p>{demoMode
              ? 'The no-account preview is available only by direct internal route.'
              : 'A five-seat Blackjack table with adjustable wagers, visible turns, and continuous rounds.'}</p>
            {!demoMode && blackjackState === 'available' ? (
              <a className="machine-card__action" href="/cards/blackjack" aria-label="Play game: Fortune Blackjack">Play Blackjack</a>
            ) : (
              <span className="machine-card__action machine-card__action--disabled">{disabledActionLabel(demoMode, blackjackState)}</span>
            )}
          </article>
          <article className={`machine-card card-game-card ${!demoMode && holdemState === 'available' ? 'machine-card--available' : 'machine-card--coming'}`}>
            <img className="card-game-card__preview" src={holdemPreview} alt="" loading="lazy" decoding="async" />
            <span className="machine-card__status">{demoMode ? 'Internal preview' : availabilityLabel(holdemState)}</span>
            <strong>Texas Hold&apos;em</strong>
            <p>{demoMode
              ? 'The account-neutral practice table is available only by direct internal route.'
              : 'Multi-seat poker with private cards, a live pot, and play that continues hand after hand.'}</p>
            {!demoMode && holdemState === 'available' ? (
              <a className="machine-card__action" href="/cards/texas-holdem" aria-label="Play game: Texas Hold'em">Play Hold’em</a>
            ) : (
              <span className="machine-card__action machine-card__action--disabled">{disabledActionLabel(demoMode, holdemState)}</span>
            )}
          </article>
          <article className={`machine-card card-game-card ${!demoMode && solitaireState === 'available' ? 'machine-card--available' : 'machine-card--coming'}`}>
            <img className="card-game-card__preview" src={solitairePreview} alt="" loading="lazy" decoding="async" />
            <span className="machine-card__status">{demoMode ? 'Internal preview' : availabilityLabel(solitaireState)}</span>
            <strong>Competitive Solitaire</strong>
            <p>{demoMode
              ? 'The account-neutral practice lab is available only by direct internal route.'
              : 'Play a competitive timed deal or relax with free single-player Klondike.'}</p>
            {!demoMode && solitaireState === 'available' ? (
              <a className="machine-card__action" href="/cards/solitaire" aria-label="Play game: Competitive Solitaire">Play Solitaire</a>
            ) : (
              <span className="machine-card__action machine-card__action--disabled">{disabledActionLabel(demoMode, solitaireState)}</span>
            )}
          </article>
          {['Pinochle', 'Spades', 'Hearts'].map((name) => (
            <article className="machine-card card-game-card machine-card--coming compact-placeholder-card" key={name}>
              <span className="compact-placeholder-card__mark" aria-hidden="true">{name === 'Hearts' ? '♥' : name === 'Spades' ? '♠' : '♣'}</span>
              <span className="machine-card__status">In the forge</span>
              <strong>{name}</strong>
              <p>{name === 'Pinochle' ? 'A partnership meld-and-trick table.' : `${name} trick-taking tables are coming later.`}</p>
              <span className="machine-card__action machine-card__action--disabled">Coming soon</span>
            </article>
          ))}
        </section>
      </main>
      </div>
    </div>
  )
}

function availabilityLabel(state: CardGameAvailabilityState): string {
  return state === 'available'
    ? 'Credit play available'
    : state === 'checking'
      ? 'Checking server availability'
      : 'Credit table unavailable'
}

function disabledActionLabel(demoMode: boolean, state: CardGameAvailabilityState): string {
  if (demoMode) return 'Internal route only'
  return state === 'checking' ? 'Checking availability' : 'Unavailable'
}
