import { useEffect, useState } from 'react'
import { ForgeCoin, ForgeCreditAmount } from '../../components/ForgeCreditAmount'
import {
  getSlotHistory,
  type SlotSpinHistoryItem,
} from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import '../index.css'

const numberFormatter = new Intl.NumberFormat('en-US')

export function AccountHistoryPage() {
  const { account, error: accountError, isLoading: isAccountLoading } = useAuthenticatedAccount()
  const [spins, setSpins] = useState<SlotSpinHistoryItem[]>([])
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)

  useEffect(() => {
    if (account === null) {
      return undefined
    }

    let isActive = true
    void getSlotHistory()
      .then((history) => {
        if (isActive) {
          setSpins(history.spins)
        }
      })
      .catch((requestError: unknown) => {
        if (isActive) {
          setHistoryError(requestError instanceof Error ? requestError.message : 'History could not be loaded.')
        }
      })
      .finally(() => {
        if (isActive) {
          setIsHistoryLoading(false)
        }
      })

    return () => {
      isActive = false
    }
  }, [account])

  return (
    <div className="player-page">
      <header className="landing-bar">
        <a className="landing-brand" href="/" aria-label="Fortune Forge home">
          <span className="landing-brand__spark" aria-hidden="true">✦</span>
          <span>Fortune Forge</span>
        </a>
        <div className="landing-bar__account">
          {account !== null && <ForgeCreditAmount amount={account.balances.slotsCredits} />}
          <a className="landing-nav__link" href="/home/settings">Back to settings</a>
        </div>
      </header>

      <main className="history-main">
        {(isAccountLoading || (account !== null && isHistoryLoading)) && (
          <div className="player-state" role="status">Reading your fortune…</div>
        )}
        {accountError !== null && <div className="player-state player-state--error" role="alert">{accountError}</div>}
        {account !== null && !isHistoryLoading && (
          <>
            <section className="history-heading">
              <p className="account-eyebrow">User history</p>
              <h1>{account.playerName}’s fortune</h1>
              <p>Your latest authenticated spins and lifetime slot totals.</p>
            </section>

            <section className="history-summary" aria-label="Slot history totals">
              <article><span>Spins</span><strong>{numberFormatter.format(account.slots.spinsPlayed)}</strong></article>
              <article><span>Wins</span><strong>{numberFormatter.format(account.slots.wins)}</strong></article>
              <article><span>Losses</span><strong>{numberFormatter.format(account.slots.losses)}</strong></article>
              <article>
                <span className="history-summary__coin-label"><ForgeCoin /> Net</span>
                <strong className={account.slots.netCredits >= 0 ? 'is-positive' : 'is-negative'}>{account.slots.netCredits < 0 ? '-' : ''}R{numberFormatter.format(Math.abs(account.slots.netCredits))}</strong>
              </article>
            </section>

            <section className="history-list" aria-labelledby="recent-history-title">
              <div className="player-section-heading">
                <p className="account-eyebrow">Most recent</p>
                <h2 id="recent-history-title">Spin results</h2>
              </div>
              {historyError !== null && <p className="account-form__error" role="alert">{historyError}</p>}
              {historyError === null && spins.length === 0 && (
                <div className="history-empty">
                  <span aria-hidden="true">✦</span>
                  <strong>No tracked spins yet</strong>
                  <p>Play Fortune Slots while logged in and your results will appear here.</p>
                  <a className="landing-button landing-button--primary" href="/slots">Play slots</a>
                </div>
              )}
              {spins.length > 0 && (
                <div className="history-rows">
                  {spins.map((spin) => (
                    <article key={spin.spinId} className={`history-row history-row--${spin.result}`}>
                      <span className="history-row__result">{spin.result}</span>
                      <span><small>Wager</small><strong>R{numberFormatter.format(spin.wageredSlotsCredits)}</strong></span>
                      <span><small>Won</small><strong>R{numberFormatter.format(spin.wonSlotsCredits)}</strong></span>
                      <span><small>Net</small><strong>{spin.netSlotsCredits > 0 ? '+' : spin.netSlotsCredits < 0 ? '-' : ''}R{numberFormatter.format(Math.abs(spin.netSlotsCredits))}</strong></span>
                      <time dateTime={spin.createdAtUtc}>{new Date(spin.createdAtUtc).toLocaleString()}</time>
                    </article>
                  ))}
                </div>
              )}
            </section>
          </>
        )}
      </main>
    </div>
  )
}
