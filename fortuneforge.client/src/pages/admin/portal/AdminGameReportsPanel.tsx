import { useEffect, useState } from 'react'
import { adminGameReportsApi, type AdminGameReportsApi, type GameReport, type ReportsPage, type ReportStatus } from '../../../features/reports/gameReportsApi'
import { AdminPortalError } from '../../../features/admin/portal/adminPortalApi'
import './adminGameReports.css'

const date = (value: string) => new Date(value).toLocaleString()
const label = (status: string) => status === 'in-review' ? 'In review' : status.charAt(0).toUpperCase() + status.slice(1)
const errorText = (error: unknown) => error instanceof Error ? error.message : 'Reports could not be loaded.'
export function AdminGameReportsPanel({ api = adminGameReportsApi }: { api?: AdminGameReportsApi }) {
  const [selected, setSelected] = useState<string | null>(null), [cursor, setCursor] = useState<string | null>(null)
  const [page, setPage] = useState<ReportsPage | null>(null), [error, setError] = useState(''), [revision, setRevision] = useState(0)
  useEffect(() => {
    if (selected) return
    let active = true; setPage(null); setError('')
    api.list(cursor).then(value => { if (active) setPage(value) }).catch(reason => { if (active) setError(errorText(reason)) })
    return () => { active = false }
  }, [api, cursor, revision, selected])
  if (selected) return <ReportDetail key={selected} id={selected} api={api} onBack={() => setSelected(null)}/>
  return <><div className="admin-toolbar"><p>Player-submitted bugs, suggestions and other feedback. Newest reports appear first. Messages may contain personal information; share them only as needed.</p><button type="button" onClick={() => setRevision(value => value + 1)}>Refresh reports</button></div>
    {!page && !error && <p role="status">Loading reports…</p>}{error && <div className="admin-error" role="alert">{error}<button type="button" onClick={() => setRevision(value => value + 1)}>Retry</button></div>}
    {page && <><div className="admin-table-wrap"><table><thead><tr><th>Game / category</th><th>Report</th><th>Received</th><th>Status</th><th/></tr></thead><tbody>{page.items.map(report => <tr key={report.id}><td><strong>{report.gameName}</strong><small>{label(report.category)}</small></td><td className="admin-reports__preview">{report.message.length > 140 ? `${report.message.slice(0, 140)}…` : report.message}</td><td>{date(report.createdAtUtc)}</td><td><span className="admin-status">{label(report.status)}</span></td><td><button type="button" onClick={() => setSelected(report.id)}>Read report</button></td></tr>)}</tbody></table>{!page.items.length && <p className="admin-empty">No player reports yet.</p>}</div><div className="admin-pagination"><button type="button" disabled={!cursor} onClick={() => setCursor(null)}>First page</button><button type="button" disabled={!page.nextCursor} onClick={() => setCursor(page.nextCursor)}>Next page →</button></div></>}
  </>
}
function ReportDetail({ id, api, onBack }: { id: string; api: AdminGameReportsApi; onBack: () => void }) {
  const [report, setReport] = useState<GameReport | null>(null), [status, setStatus] = useState<ReportStatus>('open'), [note, setNote] = useState('')
  const [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false), [revision, setRevision] = useState(0)
  useEffect(() => {
    let active = true; setReport(null); setError('')
    api.detail(id).then(value => { if (active && value.id === id) { setReport(value); setStatus(value.status); setNote(value.adminNote) } }).catch(reason => { if (active) setError(errorText(reason)) })
    return () => { active = false }
  }, [api, id, revision])
  async function save() {
    if (!report || busy) return
    setBusy(true); setError(''); setNotice('')
    try { const next = await api.update(report, status, note.trim()); setReport(next); setStatus(next.status); setNote(next.adminNote); setNotice('Report updated. The change was recorded in its audit history.') }
    catch (reason) {
      if (reason instanceof AdminPortalError && reason.status === 409) { setRevision(value => value + 1); setNotice('This report changed elsewhere. Its latest version has been loaded; review it before saving again.') }
      else setError(errorText(reason))
    } finally { setBusy(false) }
  }
  return <><button type="button" className="admin-back" disabled={busy} onClick={onBack}>← All reports</button>{!report && !error && <p role="status">Loading report…</p>}{error && <div className="admin-error" role="alert">{error}{!report && <button type="button" onClick={() => setRevision(value => value + 1)}>Retry</button>}</div>}{notice && <p role="status">{notice}</p>}
    {report && <article className="admin-report-detail"><p className="admin-eyebrow">{label(report.category)} · {report.gameName}</p><h2>Player report</h2><dl><div><dt>Game page</dt><dd><a href={report.path} target="_blank" rel="noreferrer">{report.path} ↗</a></dd></div><div><dt>Player account ID</dt><dd><code>{report.userId}</code></dd></div><div><dt>Received</dt><dd>{date(report.createdAtUtc)}</dd></div><div><dt>Last updated</dt><dd>{date(report.updatedAtUtc)}</dd></div><div><dt>Report reference</dt><dd><code>{report.id}</code></dd></div></dl><section className="admin-report-detail__message" aria-label="Player message">{report.message}</section>
      <form onSubmit={event => { event.preventDefault(); void save() }}><label>Status<select value={status} disabled={busy} onChange={event => setStatus(event.target.value as ReportStatus)}>{(['open', 'in-review', 'resolved', 'closed'] as const).map(value => <option value={value} key={value}>{label(value)}</option>)}</select></label><label>Internal admin note<textarea value={note} disabled={busy} onChange={event => setNote(event.target.value)} rows={4} maxLength={1000}/></label><p className="admin-note">Internal notes are never returned to the player. Status changes record the administrator, timestamp and previous status.</p><button type="submit" disabled={busy || status === report.status && note.trim() === report.adminNote}>{busy ? 'Saving…' : 'Update report'}</button></form>
    </article>}
  </>
}
