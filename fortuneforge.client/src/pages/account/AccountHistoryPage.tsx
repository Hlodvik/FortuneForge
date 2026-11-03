import { useEffect, useState } from 'react'
import { ForgeCoin } from '../../components/ForgeCreditAmount'
import {
  getSlotHistory,
  type AccountSummary,
  type SlotSpinHistoryItem,
} from '../../features/account/services/accountsApi'
import { useAuthenticatedAccount } from '../../features/account/useAuthenticatedAccount'
import { CardRoomHistory } from '../../games/cards/shared/CardRoomHistory'
import { useCardRoomHistory } from '../cards/useCardRoomHistory'
import { AccountLobbyShell } from './AccountLobbyShell'
import '../../games/cards/shared/cardRoomHeader.css'
import '../index.css'
import './accountHistory.css'

const numberFormatter = new Intl.NumberFormat('en-US')

export function AccountHistoryPage() {
  const { account, error: accountError, isLoading: isAccountLoading } = useAuthenticatedAccount()

  return (
    <AccountLobbyShell account={account} activeNav="history">
      {isAccountLoading && <div className="account-history"><div className="account-history__notice" role="status">Loading your history…</div></div>}
      {accountError !== null && <div className="account-history"><div className="account-history__notice account-history__notice--error" role="alert">{accountError}</div></div>}
      {account !== null && <AccountHistoryContent account={account} />}
    </AccountLobbyShell>
  )
}

export function AccountHistoryContent({ account }: { account: AccountSummary }) {
  const cardHistory = useCardRoomHistory()
  const [spins, setSpins] = useState<SlotSpinHistoryItem[]>([])
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)

  useEffect(() => {
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
  }, [account.userId])

  return (
      <div className="account-history">
        {isHistoryLoading && (
          <div className="account-history__notice" role="status">Loading your history…</div>
        )}
        {!isHistoryLoading && (
          <>
            <section className="history-heading">
              <p className="account-history__eyebrow">Your activity</p>
              <h1>{account.playerName}’s history</h1>
              <p>Your card-game results, latest authenticated spins, and lifetime slot totals.</p>
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

            <section className="history-list" aria-label="Card-game history">
              <CardRoomHistory activities={cardHistory.activities} loading={cardHistory.loading}
                error={cardHistory.error} busyId={cardHistory.busyId} onSelect={activity => void cardHistory.select(activity)} />
            </section>

            <section className="history-list" aria-labelledby="recent-history-title">
              <div className="player-section-heading">
                <p className="account-history__eyebrow">Most recent</p>
                <h2 id="recent-history-title">Spin results</h2>
              </div>
              {historyError !== null && <p className="account-history__error" role="alert">{historyError}</p>}
              {historyError === null && spins.length === 0 && (
                <div className="history-empty">
                  <span aria-hidden="true">♠</span>
                  <strong>No tracked spins yet</strong>
                  <p>Play Fortune Slots while logged in and your results will appear here.</p>
                  <a className="home-lobby__gold-button" href="/home?category=Slots">Play slots</a>
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
      </div>
  )
}
