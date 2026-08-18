import { useCallback, useEffect, useState } from 'react'
import {
  getOperationsDashboard,
  type OperationsDashboard,
} from '../../../features/admin/operations/operationsApi'
import './adminOperations.css'

const money = new Intl.NumberFormat('en-ZA', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const dateTime = new Intl.DateTimeFormat('en-ZA', { dateStyle: 'medium', timeStyle: 'short' })

export function AdminOperationsPage() {
  const [dashboard, setDashboard] = useState<OperationsDashboard | null>(null)
  const [hours, setHours] = useState(24)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try { setDashboard(await getOperationsDashboard(hours)) }
    catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Operations data could not be loaded.')
    } finally { setLoading(false) }
  }, [hours])

  useEffect(() => { void load() }, [load])

  return (
    <main className="operations-page">
      <header className="operations-header">
        <div><p>Read-only administration</p><h1>Operations</h1><span>Real-player financial activity and platform health.</span></div>
        <div className="operations-controls">
          <label>UTC window<select value={hours} onChange={(event) => setHours(Number(event.target.value))}><option value={24}>24 hours</option><option value={168}>7 days</option><option value={720}>30 days</option></select></label>
          <button type="button" onClick={() => void load()} disabled={loading}>Refresh</button>
          <a href="/home">Home</a>
        </div>
      </header>

      {loading && dashboard === null && <section className="operations-state" role="status">Loading sanitized operations data…</section>}
      {error && <section className="operations-state operations-state--error" role="alert"><strong>Operations unavailable.</strong><span>{error}</span><button type="button" onClick={() => void load()}>Try again</button></section>}
      {dashboard && <AdminOperationsDashboardView dashboard={dashboard} />}
    </main>
  )
}

export function AdminOperationsDashboardView({ dashboard }: { dashboard: OperationsDashboard }) {
  const { overview } = dashboard
  return <>
    {!overview.complete && <section className="operations-warning" role="alert"><strong>Partial result</strong>{overview.limitations.map((item) => <span key={item}>{item}</span>)}</section>}
    <section aria-labelledby="financial-title">
      <SectionHeading id="financial-title" title="House gaming P&L" note="Real-player completed games only" />
      <div className="operations-grid operations-grid--money">
        <Metric label="House gaming net" value={credits(overview.houseGamingNetCredits)} tone={overview.houseGamingNetCredits >= 0 ? 'good' : 'bad'} />
        <Metric label="Slots wagered / won" value={`${credits(overview.slots.wageredCredits)} / ${credits(overview.slots.paidCredits)}`} />
        <Metric label="Blackjack wager / payout" value={`${credits(overview.blackjack.wageredCredits)} / ${credits(overview.blackjack.paidCredits)}`} />
        <Metric label="Real-human pool Solitaire fees" value={credits(overview.solitaire.platformFeeCredits)} />
        <Metric label="Real-human pool Hold'em fees" value={credits(overview.texasHoldem.platformFeeCredits)} />
      </div>
    </section>
    <section aria-labelledby="funding-title">
      <SectionHeading id="funding-title" title="Funding flows" note="Purchases and withdrawals are not gaming P&L" />
      <div className="operations-grid"><Metric label="Completed purchases" value={`${credits(overview.funding.completedPurchaseCredits)} · ${overview.funding.completedPurchases}`} /><Metric label="Completed withdrawals" value={`${credits(overview.funding.completedWithdrawalCredits)} · ${overview.funding.completedWithdrawals}`} /></div>
    </section>
    <section className="operations-bots" aria-labelledby="bots-title">
      <SectionHeading id="bots-title" title="Synthetic bot telemetry" note={dashboard.bots.financialTreatment} />
      <div className="operations-grid">{dashboard.bots.games.map((game) => <Metric key={game.game} label={label(game.game)} value={`${game.activeLeases} active · ${game.completedTurns} completed turns`} meta={game.enabled ? 'Enabled' : 'Disabled'} />)}</div>
    </section>
    <section aria-labelledby="integrity-title">
      <SectionHeading id="integrity-title" title="Integrity" note="Sanitized consistency checks" />
      <div className="operations-list">{dashboard.integrity.checks.map((check) => <article key={check.id}><strong>{check.summary}</strong><span className={`operations-pill operations-pill--${check.status}`}>{check.status}</span><small>{check.recordsChecked} checked · {check.findings} findings</small></article>)}</div>
    </section>
    <section aria-labelledby="matches-title">
      <SectionHeading id="matches-title" title="Recent matches and queues" note="No identities, private cards, seeds, or raw state" />
      <div className="operations-columns"><div className="operations-list">{dashboard.matches.items.length === 0 ? <Empty /> : dashboard.matches.items.map((match) => <article key={match.matchId}><strong>{label(match.game)} · {match.status}</strong><span>{match.playerCount} player{match.playerCount === 1 ? '' : 's'} · net {credits(match.houseNetCredits)}</span><small>{dateTime.format(new Date(match.startedAtUtc))}</small></article>)}</div><div className="operations-list">{dashboard.queues.items.length === 0 ? <Empty /> : dashboard.queues.items.map((queue) => <article key={queue.queueId}><strong>{label(queue.game)} · {queue.status}</strong><span>{queue.queuedPlayers}/{queue.requiredPlayers} queued · {credits(queue.entryCredits)}</span><small>{dateTime.format(new Date(queue.updatedAtUtc))}</small></article>)}</div></div>
    </section>
    <section aria-labelledby="activity-title">
      <SectionHeading id="activity-title" title="Recent activity" note="Opaque event identifiers only" />
      <div className="operations-list">{dashboard.activity.items.length === 0 ? <Empty /> : dashboard.activity.items.map((item) => <article key={item.eventId}><strong>{label(item.game)} · {item.status}</strong><span>{item.category}{item.houseNetCredits === null ? '' : ` · net ${credits(item.houseNetCredits)}`}</span><small>{dateTime.format(new Date(item.occurredAtUtc))} · {item.eventId}</small></article>)}</div>
    </section>
  </>
}

function SectionHeading({ id, title, note }: { id: string; title: string; note: string }) { return <header className="operations-section-heading"><h2 id={id}>{title}</h2><p>{note}</p></header> }
function Metric({ label: metricLabel, value, meta, tone }: { label: string; value: string; meta?: string; tone?: 'good' | 'bad' }) { return <article className={`operations-metric${tone ? ` operations-metric--${tone}` : ''}`}><span>{metricLabel}</span><strong>{value}</strong>{meta && <small>{meta}</small>}</article> }
function Empty() { return <p className="operations-empty">No records in this UTC window.</p> }
function credits(value: number) { return `R${money.format(value)}` }
function label(value: string) { return value.split('-').map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(' ') }
