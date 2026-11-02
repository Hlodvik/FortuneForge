import { useEffect, useState } from 'react'
import { fetchWithAccountSession, logoutAccount } from '../../features/account/services/accountsApi'
import { SiteNavbar } from '../../components/SiteNavbar'
import './accountReview.css'

type ReviewIncident = {
  id: string; gameId: string; severity: 'possible' | 'high' | 'critical'; reason: string
  status: 'open' | 'resolved'; createdAtUtc: string; resolvedAtUtc: string | null
  resolutionOutcome: 'cleared' | 'upheld' | null; resolutionReason: string | null
}
type ReviewStatus = { accountRestricted: boolean; reviewStatus: 'pending' | 'confirmed' | 'restricted' | 'clear'; message: string; incidents: ReviewIncident[] }
const loadError = 'Your review status could not be loaded. Please try again.'
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const nullableString = (value: unknown) => value === null || typeof value === 'string'
function reviewStatus(body: unknown): ReviewStatus {
  if (!record(body) || typeof body.accountRestricted !== 'boolean' || typeof body.message !== 'string'
    || !['pending', 'confirmed', 'restricted', 'clear'].includes(String(body.reviewStatus))
    || (body.reviewStatus === 'clear') === body.accountRestricted) throw new Error(loadError)
  // Only this endpoint's explicit player-facing fields are presented. A clear account
  // must not disclose incidents that only restrict withdrawals or individual games.
  const incidents = body.accountRestricted && Array.isArray(body.incidents) ? body.incidents.filter((value): value is ReviewIncident =>
    record(value) && typeof value.id === 'string' && typeof value.gameId === 'string'
    && ['possible', 'high', 'critical'].includes(String(value.severity)) && typeof value.reason === 'string'
    && (value.status === 'open' || value.status === 'resolved') && typeof value.createdAtUtc === 'string'
    && nullableString(value.resolvedAtUtc) && nullableString(value.resolutionReason)
    && (value.resolutionOutcome === null || value.resolutionOutcome === 'cleared' || value.resolutionOutcome === 'upheld')) : []
  return { accountRestricted: body.accountRestricted, reviewStatus: body.reviewStatus as ReviewStatus['reviewStatus'], message: body.message, incidents }
}
const gameName = (id: string) => id === 'texas-holdem' ? 'Texas Hold’em' : id.split('-').map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ')
function ReviewDate({ value }: { value: string }) {
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? <>Date unavailable</> : <time dateTime={value}>{date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}</time>
}

export function AccountReviewPage() {
  const [status, setStatus] = useState<ReviewStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true); setError(null)
    void fetchWithAccountSession('/api/account/review', { method: 'GET', cache: 'no-store', signal: controller.signal })
      .then(async response => {
        if (response.status === 401) { window.location.replace('/login?returnTo=%2Faccount%2Freview'); return }
        if (!response.ok) throw new Error(loadError)
        const body = reviewStatus(await response.json())
        if (!controller.signal.aborted) setStatus(body)
      }).catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Please try again.') })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [attempt])

  const signOut = async () => {
    try { await logoutAccount() } catch { /* Local credentials are cleared even if the session request fails. */ }
    window.location.replace('/login')
  }
  return <div className="account-review-page"><SiteNavbar mode="restricted" /><main className="account-review" aria-labelledby="account-review-title">
    <section className="account-review__card account-review__page-card">
      <h1 id="account-review-title">{status?.accountRestricted === false ? 'Review complete' : status?.reviewStatus === 'confirmed' ? 'Review decision' : 'Account review'}</h1>
      <p role="status">{loading ? 'Checking your review status…' : status?.message ?? 'Your account is temporarily unavailable while our team reviews it.'}</p>
      {error && <p role="alert">{error}</p>}
      {status?.accountRestricted && status.incidents.length > 0 && <section className="account-review__incidents" aria-labelledby="account-review-incidents-title">
        <h2 id="account-review-incidents-title">What was flagged</h2>
        <p>These incidents contributed to your account restriction.</p>
        {status.incidents.map((incident, index) => <article className="account-review__incident" key={incident.id} aria-labelledby={`account-review-incident-${index}`}>
          <h3 id={`account-review-incident-${index}`}>{gameName(incident.gameId)}</h3>
          <dl className="account-review__facts">
            <div><dt>Severity</dt><dd>{incident.severity === 'critical' ? 'Critical' : incident.severity === 'high' ? 'High' : 'Possibly suspicious'}</dd></div>
            <div><dt>Flagged</dt><dd><ReviewDate value={incident.createdAtUtc} /></dd></div>
          </dl>
          <h4>Why this was flagged</h4><p className="account-review__explanation">{incident.reason}</p>
          <div className="account-review__decision">
            <h4>{incident.status === 'open' ? 'Pending admin review' : 'Admin decision'}</h4>
            {incident.status === 'open' ? <p>No decision has been made on this incident yet.</p> : <>
              <p><strong>{incident.resolutionOutcome === 'upheld' ? 'Restriction upheld' : incident.resolutionOutcome === 'cleared' ? 'Incident cleared' : 'Review completed'}</strong></p>
              {incident.resolutionReason && <p className="account-review__explanation">{incident.resolutionReason}</p>}
              {incident.resolvedAtUtc && <p className="account-review__decision-date">Decided <ReviewDate value={incident.resolvedAtUtc} /></p>}
            </>}
          </div>
        </article>)}
      </section>}
      <div className="account-review__actions">
        {status?.accountRestricted === false && !error && !loading
          ? <a className="account-review__button" href="/home">Return home</a>
          : <button type="button" disabled={loading} onClick={() => setAttempt(value => value + 1)}>Check status</button>}
        <button type="button" className="account-review__secondary" onClick={() => void signOut()}>Sign out</button>
      </div>
    </section>
  </main></div>
}
