import { useEffect, useState } from 'react'
import { gameAnalyticsApi, type GameAnalyticsApi, type GameAnalyticsReport } from '../../../features/admin/portal/gameAnalyticsApi'

export const auditDate = (date: string | null) => date ? new Date(date).toLocaleString() : 'Not recorded'
export const auditMoney = (cents: number) => `R${(cents / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
export const auditGameName = (id: string) => id === 'texas-holdem' ? 'Texas Hold’em' : id.split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ')
export function AdminGameAnalyticsPanel({ userId, api = gameAnalyticsApi }: { userId?: string; api?: GameAnalyticsApi }) {
  const [period, setPeriod] = useState('day'), [date, setDate] = useState(''), [account, setAccount] = useState(userId ?? '')
  const [filter, setFilter] = useState({ period: 'day', date: '', account: userId ?? '' }), [revision, setRevision] = useState(0)
  const [report, setReport] = useState<GameAnalyticsReport | null>(null), [error, setError] = useState('')
  useEffect(() => { let active = true; setReport(null); setError(''); api.metrics(filter.period, filter.date || undefined, userId || filter.account || undefined).then(value => { if (active) setReport(value) }).catch(reason => { if (active) setError(reason instanceof Error ? reason.message : 'Analytics could not be loaded.') }); return () => { active = false } }, [api, userId, filter, revision])
  return <section aria-label="Game analytics"><form className="admin-toolbar" onSubmit={event => { event.preventDefault(); setFilter({ period, date, account: account.trim() }); setRevision(value => value + 1) }}>
    <label>Period<select value={period} onChange={event => { setPeriod(event.target.value); setDate('') }}><option value="day">Day</option><option value="week">Week</option><option value="month">Month</option><option value="alltime">All time</option></select></label>
    {period !== 'alltime' && <label>{period === 'week' ? 'Date within week (UTC)' : 'Date (UTC)'}<input aria-label="Reporting date" type={period === 'month' ? 'month' : 'date'} value={date} onChange={event => setDate(event.target.value)} /></label>}
    {!userId && <label>Account ID (optional)<input value={account} onChange={event => setAccount(event.target.value)} placeholder="All human players" /></label>}
    <button type="submit">Load statistics</button>
  </form>
    <p className="admin-note">UTC reporting periods; weeks start Monday. Human rounds count each participating player. Unique rounds count the shared deal or completed match once. Current players are recent foreground sessions, not seated bots.</p>
    {error && <p className="admin-error" role="alert">{error}</p>}{!error && !report && <p role="status">Loading game statistics…</p>}
    {report && <><div className="admin-metrics">{[['Current players', report.currentPlayers.toLocaleString()], ['Human rounds', report.totals.humanRounds.toLocaleString()], ['Unique rounds', report.totals.uniqueRounds.toLocaleString()], ['Wagered', auditMoney(report.totals.wagerCents)], ['Returned', auditMoney(report.totals.payoutCents)], ['Net wins', auditMoney(report.totals.netWinCents)], ['Net losses', auditMoney(report.totals.netLossCents)], ['Observed time', `${(report.totals.observedActiveMilliseconds / 3600000).toFixed(1)} hours`]].map(([label, value]) => <article className="admin-metric" key={label}><span>{label}</span><strong>{value}</strong></article>)}</div>
      <p className="admin-note">Tracking began {auditDate(report.trackingStartedAtUtc)}. Totals read {auditDate(report.asOfUtc)}; presence {auditDate(report.presenceAsOfUtc)}. Earlier rounds have not been estimated.</p>
      {report.limitations.length > 0 && <aside className="admin-coverage">{report.limitations.map(note => <p key={note}>{note}</p>)}</aside>}
      <div className="admin-table-wrap"><table><thead><tr><th>Game</th><th>Now</th><th>Human rounds</th><th>Unique rounds</th><th>Paid / free / practice</th><th>Wagered</th><th>Returned</th><th>Net wins</th><th>Net losses</th><th>Time</th></tr></thead><tbody>{report.games.map(({ gameId, metrics: m, currentPlayers }) => <tr key={gameId}><td>{auditGameName(gameId)}</td><td>{currentPlayers}</td><td>{m.humanRounds}</td><td>{m.uniqueRounds}</td><td>{m.paidRounds} / {m.freeRounds} / {m.practiceRounds}</td><td>{auditMoney(m.wagerCents)}</td><td>{auditMoney(m.payoutCents)}</td><td>{auditMoney(m.netWinCents)}{m.unknownMoneyRounds > 0 && <small>{m.unknownMoneyRounds} rounds missing money data</small>}</td><td>{auditMoney(m.netLossCents)}</td><td>{Math.round(m.observedActiveMilliseconds / 60000)} min</td></tr>)}</tbody></table></div>
    </>}
  </section>
}
