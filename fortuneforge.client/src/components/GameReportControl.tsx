import { useEffect, useRef, useState, type FormEvent } from 'react'
import { GameReportError, sendGameReport, type ReportCategory, type ReportInput } from '../features/reports/gameReportsApi'
import './GameReportControl.css'

type Submission = { input: ReportInput; key: string }
export function GameReportControl({ gameTitle, send = sendGameReport }: { gameTitle?: string; send?: typeof sendGameReport }) {
  const [open, setOpen] = useState(false), [category, setCategory] = useState<ReportCategory>('bug'), [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [receipt, setReceipt] = useState('')
  const dialog = useRef<HTMLDialogElement>(null), trigger = useRef<HTMLButtonElement>(null)
  const submission = useRef<Submission | null>(null), inFlight = useRef<AbortController | null>(null), active = useRef(true)
  useEffect(() => { active.current = true; return () => { active.current = false; inFlight.current?.abort() } }, [])
  useEffect(() => {
    if (!open) return
    const element = dialog.current, triggerElement = trigger.current; element?.showModal()
    return () => { element?.close(); triggerElement?.focus() }
  }, [open])
  const close = () => { if (!inFlight.current) setOpen(false) }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (inFlight.current) return
    const input = submission.current ?? { input: { category, message: message.trim(), path: window.location.pathname }, key: `game-report-${crypto.randomUUID()}` }
    if (input.input.message.length < 10 || input.input.message.length > 3000) { setError('Please use 10 to 3,000 characters so we can understand your report.'); return }
    submission.current = input
    const controller = new AbortController(); inFlight.current = controller
    setBusy(true); setError('')
    const timer = window.setTimeout(() => controller.abort(), 20_000)
    try {
      const result = await Promise.race([
        send(input.input, input.key, controller.signal),
        new Promise<never>((_resolve, reject) => controller.signal.addEventListener('abort', () => reject(new Error('Your report could not be confirmed yet. Retry to send the same report safely.')), { once: true })),
      ])
      if (active.current && !controller.signal.aborted) { setReceipt(result.id); submission.current = null }
    } catch (reason) {
      if (reason instanceof GameReportError && reason.status === 400) submission.current = null
      if (active.current) setError(reason instanceof Error ? reason.message : 'Your report could not be confirmed. Please retry.')
    } finally {
      window.clearTimeout(timer)
      if (inFlight.current === controller) inFlight.current = null
      if (active.current) setBusy(false)
    }
  }
  function begin() {
    if (receipt) { setReceipt(''); setMessage(''); setCategory('bug'); setError('') }
    setOpen(true)
  }
  return <><button ref={trigger} type="button" className="in-game-navbar__report" aria-label="Report this game" aria-haspopup="dialog" onClick={begin}><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M5 21V4m0 0c5-5 9 5 14 0v10c-5 5-9-5-14 0"/></svg><span>Report</span></button>
    {open && <dialog ref={dialog} className="game-report-dialog" aria-labelledby="game-report-title" onCancel={event => { event.preventDefault(); close() }} onClick={event => { if (event.target === event.currentTarget) close() }}><div>
      <button type="button" className="game-report-dialog__close" aria-label="Close report" disabled={busy} onClick={close}>×</button>
      <p className="game-report-dialog__eyebrow">HELP US MAKE IT BETTER</p><h2 id="game-report-title">Report {gameTitle || 'this game'}</h2>
      {receipt ? <><p role="status">Thank you! Your report has been saved for our team.</p><small>Reference: {receipt.slice(0, 12)}</small><button type="button" className="game-report-dialog__send" onClick={close}>Done</button></> : <form onSubmit={event => void submit(event)}>
        <label>What would you like to share?<select value={category} disabled={busy || submission.current !== null} onChange={event => setCategory(event.target.value as ReportCategory)}><option value="bug">Something is broken</option><option value="suggestion">I have a suggestion</option><option value="other">Something else</option></select></label>
        <label>Your report<textarea rows={6} minLength={10} maxLength={3000} required readOnly={busy || submission.current !== null} value={message} onChange={event => setMessage(event.target.value)} placeholder="What happened, or what would make the game better?"/></label>
        <p className="game-report-dialog__privacy">Your account, game page and this message are sent to our team. No screenshots or device telemetry are collected. Please do not include passwords or payment details.</p>
        {error && <p role="alert">{error}</p>}{submission.current && error && <p className="game-report-dialog__privacy">Retry sends the same report, without creating a duplicate.</p>}
        <button type="submit" className="game-report-dialog__send" disabled={busy || message.trim().length < 10}>{busy ? 'Sending…' : submission.current ? 'Retry report' : 'Send report'}</button>
      </form>}
    </div></dialog>}
  </>
}
