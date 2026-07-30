import { useState } from 'react'
import nimbusCloud from '../../assets/slots/symbols/wukong/nimbus-cloud.png'
import wukongMedallion from '../../assets/slots/symbols/wukong/wukong-medallion.png'
import { ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { useRecentSlotGame } from '../games/useRecentSlotGame'
import { logoutAccount } from './services/accountsApi'
import { useAuthenticatedAccount } from './useAuthenticatedAccount'
import './landing.css'

export function HomePage() {
  const { account, error, isLoading, reload } = useAuthenticatedAccount()
  const [isLoggingOut, setIsLoggingOut] = useState(false)
  const recentGame = useRecentSlotGame(account?.userId)

  async function handleLogout() {
    setIsLoggingOut(true)
    try {
      await logoutAccount()
    } finally {
      window.location.replace('/')
    }
  }

  return (
    <div className="player-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <PaymentAlertsMenu />
          <nav className="landing-nav" aria-label="Account navigation">
            <a className="landing-nav__link" href="/home/settings">Settings</a>
            <button
              className="landing-nav__link landing-nav__button"
              type="button"
              disabled={isLoggingOut}
              onClick={() => void handleLogout()}
            >
              {isLoggingOut ? 'Leaving…' : 'Log out'}
            </button>
          </nav>
        </div>
      </header>

      <main className="player-main">
        {isLoading && <AccountLoading label="Opening your account…" />}
        {!isLoading && error !== null && (
          <AccountError message={error} onRetry={reload} />
        )}
        {!isLoading && account !== null && (
          <>
            <section className="player-welcome" aria-labelledby="player-title">
              <div>
                <p className="account-eyebrow">Player home</p>
                <h1 id="player-title">Welcome back, <span>{account.playerName}</span>.</h1>
                <p>Your games and account controls are all gathered here.</p>
              </div>
              <div className="player-welcome__actions">
                {account.role.toLowerCase() === 'admin' && (
                  <a className="landing-button landing-button--secondary" href="/admin/invoices">
                    Customer invoices
                  </a>
                )}
                <a className="landing-button landing-button--gold" href="/home/rand">
                  Add Rand
                </a>
                <a className="landing-button landing-button--secondary" href="/home/rand#withdrawal-request">
                  Request withdrawal
                </a>
                <a className="landing-button landing-button--secondary" href="/home/settings">
                  Account settings
                </a>
              </div>
            </section>

            <section className="recently-played" aria-labelledby="recently-played-title">
              <div className="player-section-heading">
                <p className="account-eyebrow">Jump back in</p>
                <h2 id="recently-played-title">Recently played</h2>
              </div>

              {recentGame.isLoading && (
                <div className="recently-played__empty" role="status">Finding your latest machine…</div>
              )}
              {!recentGame.isLoading && recentGame.game !== null && recentGame.game.playHref !== null && (
                <a className="recent-game-card" href={recentGame.game.playHref}>
                  <span className={`recent-game-card__art recent-game-card__art--${recentGame.game.imagePresentation}`} aria-hidden="true">
                    <img src={recentGame.game.image} alt="" draggable="false" />
                  </span>
                  <span>
                    <small>Continue playing</small>
                    <strong>{recentGame.game.title}</strong>
                    <p>Go straight to your most recently played machine.</p>
                  </span>
                  <span className="recent-game-card__action" aria-hidden="true">→</span>
                </a>
              )}
              {!recentGame.isLoading && recentGame.game === null && (
                <div className="recently-played__empty">
                  <span>{recentGame.error === null ? 'No recently played machine yet.' : 'Recent play is temporarily unavailable.'}</span>
                  <a href="/slots">Choose a slot machine <span aria-hidden="true">→</span></a>
                </div>
              )}
            </section>

            <section className="game-library" aria-labelledby="games-title">
              <div className="player-section-heading">
                <p className="account-eyebrow">Game room</p>
                <h2 id="games-title">Choose your game</h2>
              </div>
              <div className="game-library__grid">
                <a className="game-card game-card--available" href="/slots">
                  <img src={wukongMedallion} alt="" aria-hidden="true" />
                  <span className="game-card__status">Three machines</span>
                  <strong>Fortune Slots</strong>
                  <p>Choose a themed machine, including Wukong's Journey to the West.</p>
                  <span className="game-card__action">Choose a machine <span aria-hidden="true">→</span></span>
                </a>
                <button className="game-card game-card--coming" type="button" disabled>
                  <img src={nimbusCloud} alt="" aria-hidden="true" />
                  <span className="game-card__status">Coming soon</span>
                  <strong>Fortune Blackjack</strong>
                  <p>A new table game is being forged. This game cannot be opened yet.</p>
                  <span className="game-card__action">Coming soon</span>
                </button>
              </div>
            </section>
          </>
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
      <strong>Your account could not be opened.</strong>
      <span>{message}</span>
      <button className="landing-button landing-button--secondary" type="button" onClick={onRetry}>
        Try again
      </button>
      <a href="/login">Return to login</a>
    </div>
  )
}
