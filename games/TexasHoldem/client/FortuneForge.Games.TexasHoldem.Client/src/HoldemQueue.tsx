export function HoldemQueue({ busy, onCancel }: Readonly<{ busy: boolean; onCancel?: () => void }>) {
  return <section className="ff-holdem-queue" aria-label="Finding a poker table" aria-busy="true">
    <span className="ff-holdem-queue__mark" aria-hidden="true">♠</span>
    <div className="ff-holdem-queue-copy" role="status"><h1>Finding table…</h1></div>
    {onCancel && <button type="button" className="ff-holdem-secondary" disabled={busy} onClick={() => { if (!busy) onCancel() }}>Cancel</button>}
  </section>
}
