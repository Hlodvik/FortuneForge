import { useEffect, useRef, useState } from 'react'
import { gameAnalyticsApi, type GameAnalyticsApi, type RoundAuditPage, type RoundAuditDetail, type RiskIncident, type PlayerRiskState, type IncidentDraft } from '../../../features/admin/portal/gameAnalyticsApi'
import { auditDate, auditGameName, auditMoney } from './AdminGameAnalyticsPanel'
import './adminGameReviews.css'

const message = (error: unknown) => error instanceof Error ? error.message : 'The review could not be completed.'

export function AdminRoundReviewsPanel({ api = gameAnalyticsApi }: { api?: GameAnalyticsApi }) {
  const [game, setGame] = useState(''), [status, setStatus] = useState(''), [filters, setFilters] = useState({ game: '', status: '' }), [cursor, setCursor] = useState<string | null>(null)
  const [page, setPage] = useState<RoundAuditPage | null>(null), [error, setError] = useState(''), [selected, setSelected] = useState<string | null>(null)
  useEffect(() => { let active = true; setPage(null); setError(''); api.rounds(filters.game || undefined, filters.status || undefined, cursor).then(value => { if (active) setPage(value) }).catch(reason => { if (active) setError(message(reason)) }); return () => { active = false } }, [api, filters, cursor])
  if (selected) return <RoundDetail id={selected} api={api} onBack={() => setSelected(null)} />
  return <section aria-label="Round reviews"><p className="admin-note">Private settlement evidence and integrity checks. A flagged accounting or card record can be a server defect; it is not automatically a player strike. Incomplete records stay held. Clean evidence is eligible for deletion seven days after audit.</p>
    <form className="admin-toolbar" onSubmit={event => { event.preventDefault(); setCursor(null); setFilters({ game: game.trim(), status }) }}><label>Game ID<input value={game} onChange={event => setGame(event.target.value)} placeholder="All games" /></label><label>Audit status<select value={status} onChange={event => setStatus(event.target.value)}><option value="">All statuses</option><option value="flagged">Flagged</option><option value="incomplete">Incomplete</option><option value="passed">Passed</option></select></label><button type="submit">Load rounds</button></form>
    {error && <p className="admin-error" role="alert">{error}</p>}{!page && !error && <p role="status">Loading round reviews…</p>}
    {page && <><div className="admin-table-wrap"><table><thead><tr><th>Game / round</th><th>Completed</th><th>Status</th><th>Players</th><th>Evidence</th><th /></tr></thead><tbody>{page.items.map(round => <tr key={round.id}><td>{auditGameName(round.gameId)}<small>{round.roundId}</small></td><td>{auditDate(round.completedAtUtc)}</td><td>{round.auditStatus}</td><td>{round.participants.filter(player => player.isAccountBacked).length}</td><td>{round.evidencePresent ? 'Retained' : 'Deleted; summary kept'}</td><td><button type="button" onClick={() => setSelected(round.id)}>Review round</button></td></tr>)}</tbody></table>{!page.items.length && <p className="admin-empty">No matching round audits.</p>}</div><div className="admin-pagination"><button type="button" disabled={!cursor} onClick={() => setCursor(null)}>First page</button><button type="button" disabled={!page.nextCursor} onClick={() => setCursor(page.nextCursor)}>Next page →</button></div></>}
  </section>
}

function RoundDetail({ id, api, onBack }: { id: string; api: GameAnalyticsApi; onBack: () => void }) {
  const [detail, setDetail] = useState<RoundAuditDetail | null>(null), [error, setError] = useState(''), [player, setPlayer] = useState(''), [severity, setSeverity] = useState('possible'), [basis, setBasis] = useState(''), [rule, setRule] = useState(''), [confirm, setConfirm] = useState(false), [busy, setBusy] = useState(false), [created, setCreated] = useState<RiskIncident | null>(null)
  const pendingDraft = useRef<IncidentDraft | null>(null), inFlight = useRef(false)
  const [attempted, setAttempted] = useState(false)
  useEffect(() => { let active = true; setDetail(null); setError(''); api.evidence(id).then(value => { if (active) setDetail(value) }).catch(reason => { if (active) setError(message(reason)) }); return () => { active = false } }, [api, id])
  const flag = async () => {
    if (inFlight.current || !detail || !detail.summary.evidencePresent || !player || !confirm || rule.trim().length < 3 || basis.trim().length < 20) return
    pendingDraft.current ??= { userId: player, gameId: detail.summary.gameId, roundId: detail.summary.roundId, severity, sourceIncidentId: id, ruleId: rule.trim(), ruleVersion: 'admin-v1', attributionBasis: basis.trim(), evidenceIds: [id] }
    inFlight.current = true; setAttempted(true); setBusy(true); setError('')
    try { const result = await api.flag(pendingDraft.current); setCreated(result.incident) } catch (reason) { setError(message(reason)) } finally { inFlight.current = false; setBusy(false) }
  }
  return <section><button className="admin-back" type="button" disabled={busy} onClick={onBack}>← All round reviews</button>{error && <p className="admin-error" role="alert">{error}</p>}{!detail && !error && <p role="status">Loading private evidence…</p>}{detail && <>
    <h2>{auditGameName(detail.summary.gameId)} · {detail.summary.auditStatus}</h2><p>Round <code>{detail.summary.roundId}</code> · {auditDate(detail.summary.completedAtUtc)}</p><p className="admin-note">Evidence: {detail.retentionState}. {detail.retentionNote} {detail.summary.evidenceDeleteAfterUtc && `Eligible for deletion ${auditDate(detail.summary.evidenceDeleteAfterUtc)}.`}</p>
    <div className="admin-records">{detail.summary.findings.map((finding, index) => <article className="admin-record" key={`${finding.code}:${index}`}><div><strong>{finding.severity} · {finding.code}</strong><p>{finding.message}</p></div></article>)}</div>
    <div className="admin-table-wrap"><table><thead><tr><th>Participant</th><th>Wagered</th><th>Returned</th><th>Net</th></tr></thead><tbody>{detail.summary.participants.map((participant, index) => <tr key={`${participant.actorId}:${index}`}><td>{participant.displayName || participant.actorId}<small>{participant.actorId} · {participant.isAccountBacked ? 'Account' : 'House bot'}</small></td><td>{auditMoney(participant.wagerCents)}</td><td>{auditMoney(participant.payoutCents)}</td><td>{auditMoney(participant.netCents)}</td></tr>)}</tbody></table></div>
    <details className="admin-evidence"><summary>Private cards, actions and settlement payload</summary><pre>{prettyEvidence(detail.payloadJson)}</pre><small>{detail.summary.evidenceKind} · {detail.summary.auditorVersion}<br />SHA-256 {detail.summary.evidenceSha256}</small></details>
    {created ? <div className="admin-coverage" role="status">Incident recorded and evidence held. Review the decision under Player restrictions.<code>{created.id}</code></div> : detail.summary.evidencePresent && <form className="admin-incident-form" onSubmit={event => { event.preventDefault(); void flag() }}>
      <h3>Attribute an incident to a player</h3><p>Use evidence of this player’s actions. Duplicate checks of this same round do not add strikes. This action immediately applies the configured restrictions.</p>
      <label>Player<select required disabled={attempted} value={player} onChange={event => setPlayer(event.target.value)}><option value="">Select a human participant</option>{detail.summary.participants.filter(value => value.isAccountBacked).map(value => <option key={value.actorId} value={value.actorId}>{value.displayName || value.actorId}</option>)}</select></label>
      <label>Severity<select disabled={attempted} value={severity} onChange={event => setSeverity(event.target.value)}><option value="possible">Possibly suspicious</option><option value="high">Highly suspicious</option><option value="critical">Critical — pending review</option></select></label>
      <label>Rule or issue<input required readOnly={attempted} minLength={3} maxLength={200} value={rule} onChange={event => setRule(event.target.value)} /></label>
      <label>Evidence and reason for attribution<textarea required readOnly={attempted} aria-describedby="incident-player-explanation" minLength={20} maxLength={4000} value={basis} onChange={event => setBasis(event.target.value)} /></label>
      <p id="incident-player-explanation" className="admin-note">This explanation is shown to the player if their account is restricted. Describe what was flagged and why, without including private cards, other players’ information, or internal evidence references.</p>
      <p className="admin-note">Possible: three incidents block this game. High: withdrawals held; two block this game; two in each of two games restrict the account. Critical: account access restricted immediately, pending review.</p>
      <label className="admin-ack"><input type="checkbox" disabled={attempted} checked={confirm} onChange={event => setConfirm(event.target.checked)} />I have reviewed the evidence and the resulting restrictions.</label>{attempted && <p className="admin-note">A retry sends the same reviewed attribution and cannot add a second strike for this round.</p>}<button type="submit" disabled={busy || !confirm || !player || rule.trim().length < 3 || basis.trim().length < 20}>{busy ? 'Recording…' : attempted ? 'Retry recording' : 'Record incident'}</button>
    </form>}
  </>}</section>
}

function prettyEvidence(payload: string | null) { if (!payload) return 'Detailed evidence is no longer retained.'; try { return JSON.stringify(JSON.parse(payload), null, 2) } catch { return payload } }

export function AdminRiskIncidentsPanel({ userId, api = gameAnalyticsApi }: { userId?: string; api?: GameAnalyticsApi }) {
  const [input, setInput] = useState(userId ?? ''), [account, setAccount] = useState(userId ?? ''), [revision, setRevision] = useState(0)
  const [data, setData] = useState<{ incidents: RiskIncident[]; state: PlayerRiskState | null } | null>(null), [error, setError] = useState(''), [selected, setSelected] = useState<RiskIncident | null>(null), [evidence, setEvidence] = useState<string | null>(null), [outcome, setOutcome] = useState('cleared'), [reason, setReason] = useState(''), [busy, setBusy] = useState(false)
  const [reviewedSeverity, setReviewedSeverity] = useState('possible')
  const pendingResolution = useRef<{ id: string; outcome: string; reason: string; severity: string } | null>(null), inFlight = useRef(false)
  const [attempted, setAttempted] = useState(false)
  useEffect(() => { let active = true; setData(null); setError(''); api.incidents(userId || account || undefined).then(value => { if (active) setData(value) }).catch(value => { if (active) setError(message(value)) }); return () => { active = false } }, [api, userId, account, revision])
  const chooseIncident = (incident: RiskIncident | null) => { if (inFlight.current) return; pendingResolution.current = null; setAttempted(false); setSelected(incident); setReason(''); setReviewedSeverity(incident?.severity ?? 'possible'); setOutcome(incident?.resolutionOutcome === 'cleared' ? 'upheld' : 'cleared') }
  const resolve = async () => {
    if (!selected || inFlight.current || reason.trim().length < 10 || !['possible', 'high', 'critical'].includes(reviewedSeverity)) return
    pendingResolution.current ??= { id: selected.id, outcome, reason: reason.trim(), severity: reviewedSeverity }
    const draft = pendingResolution.current
    inFlight.current = true; setAttempted(true); setBusy(true); setError('')
    try { await api.resolve(draft.id, draft.outcome, draft.reason, draft.severity); setSelected(null); setReason(''); pendingResolution.current = null; setAttempted(false); setRevision(value => value + 1) }
    catch (value) { setError(message(value)) } finally { inFlight.current = false; setBusy(false) }
  }
  if (evidence) return <RoundDetail id={evidence} api={api} onBack={() => setEvidence(null)} />
  return <section aria-label="Player restrictions"><p className="admin-note">Pending incidents are allegations. Confirmed cheating evidence stays linked to this account indefinitely. Cleared evidence is held for 30 days after review, or longer if another incident still needs it. Financial records and decision history remain.</p>
    {!userId && <form className="admin-toolbar" onSubmit={event => { event.preventDefault(); if (busy) return; chooseIncident(null); setAccount(input.trim()); setRevision(value => value + 1) }}><label>Account ID<input disabled={busy} value={input} onChange={event => setInput(event.target.value)} placeholder="All recent incidents" /></label><button type="submit" disabled={busy}>Find incidents</button></form>}
    {error && <p className="admin-error" role="alert">{error}</p>}{!data && !error && <p role="status">Loading incidents…</p>}
    {data?.state && <aside className="admin-coverage"><strong>{data.state.accountRestricted ? 'Account restricted' : 'Account access available'}</strong><p>{data.state.withdrawalsBlocked ? 'Withdrawals held' : 'No withdrawal hold'}</p><p>Blocked games: {data.state.blockedGameIds.map(auditGameName).join(', ') || 'None'}</p></aside>}
    {data && <><p className="admin-note">Showing up to 100 recent incidents{account ? ' for this account' : '; filter by account to review a player'}.</p><div className="admin-records">{data.incidents.map(incident => <article className="admin-record" key={incident.id}><div><strong>{incident.severity} · {auditGameName(incident.gameId)}</strong><small>{incident.userId} · {auditDate(incident.createdAtUtc)}</small><p>{incident.attributionBasis}</p><small>{incident.status} {incident.resolutionOutcome && `· ${incident.resolutionOutcome} by ${incident.resolvedBy}`}</small>{incident.resolutionReason && <p>{incident.resolutionReason}</p>}<div className="admin-review-actions">{incident.evidenceIds.map(id => <button key={id} type="button" disabled={busy} onClick={() => setEvidence(id)}>View evidence</button>)}</div></div><button type="button" disabled={busy} onClick={() => chooseIncident(incident)}>{incident.status === 'resolved' ? 'Review decision' : 'Resolve incident'}</button></article>)}{!data.incidents.length && <p className="admin-empty">No recorded incidents.</p>}</div></>}
    {selected && <form className="admin-incident-form" onSubmit={event => { event.preventDefault(); void resolve() }}>
      <h3>Review {selected.userId} · {auditGameName(selected.gameId)}</h3><p>{selected.attributionBasis}</p>
      <label>Decision<select disabled={attempted} value={outcome} onChange={event => setOutcome(event.target.value)}><option value="cleared">Clear incident — recalculate restrictions</option><option value="upheld">Confirm cheating — keep evidence indefinitely</option></select></label>
      <label>Reviewed severity<select disabled={attempted} value={reviewedSeverity} onChange={event => setReviewedSeverity(event.target.value)} aria-describedby="reviewed-severity-help"><option value="possible">Possibly suspicious</option><option value="high">Highly suspicious</option><option value="critical">Critical — restrict account access</option></select></label>
      <p id="reviewed-severity-help" className="admin-note">Severity starts at the incident’s current level. Change it only when the reviewed evidence supports that level. Confirming an incident does not automatically make it critical. This updates the existing incident without adding another strike.</p>
      <label>Decision reason<textarea required readOnly={attempted} aria-describedby="decision-player-explanation" minLength={10} maxLength={4000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      <p id="decision-player-explanation" className="admin-note">This reason is shown to the player while this incident contributes to an account restriction. Keep the explanation clear and exclude private evidence or other players’ information.</p>
      <p>Restrictions from other incidents remain. Previous decisions are retained in the audit trail.</p>
      <div className="admin-review-actions"><button type="button" disabled={busy} onClick={() => chooseIncident(null)}>Cancel</button><button type="submit" disabled={busy || reason.trim().length < 10 || !['possible', 'high', 'critical'].includes(reviewedSeverity)}>{busy ? 'Saving…' : attempted ? 'Retry decision' : 'Save decision'}</button></div>
    </form>}
  </section>
}
