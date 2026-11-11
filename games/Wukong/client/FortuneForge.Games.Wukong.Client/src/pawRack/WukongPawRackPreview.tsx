import { useEffect, useRef, useState, type DragEvent, type KeyboardEvent } from 'react'
import '@fontsource-variable/sora'
import { SlotMachine } from '@fortuneforge/slot-client-core'
import { SlotSymbol } from '@fortuneforge/slot-client-core'
import { WUKONG_CABINET_THEME } from '../cabinetTheme'
import { WUKONG_SYMBOLS } from '../symbols'
import type { WukongPawGrant, WukongPawPreviewSymbol, WukongPawRackPreviewProps } from './types'
import './WukongPawRackPreview.css'

const pawTransferType = 'application/x-fortuneforge-prototype-paw'
const columns = [0, 1, 2, 3, 4] as const
const points = (value: number) => `${value.toLocaleString('en-ZA')} points`
const previewCashFormatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function EarnScrollIcon() {
  return <svg className="wukong-paw-preview__scroll-icon" viewBox="0 0 64 64" fill="none" aria-hidden="true">
    <path d="M18 11h27v37c0 5-4 8-9 8H18V11Z" fill="#ead4a1" stroke="#9f6b2b" strokeWidth="3" />
    <path d="M18 11c0-5-9-5-9 0v7h9m0 30h27c10 0 10 8 0 8H18" fill="#ba8841" stroke="#f3d58b" strokeWidth="3" />
    <path d="M26 21v17m7-17v17m7-17v17M23 35l20-11" stroke="#7e392b" strokeWidth="3" strokeLinecap="round" />
    <path d="m48 5 2 4 4 2-4 2-2 4-2-4-4-2 4-2 2-4Z" fill="#fff1b0" />
  </svg>
}

export function WukongPawRackPreview(props: WukongPawRackPreviewProps) {
  const [selectedGrantId, setSelectedGrantId] = useState<string | null>(null)
  const [hoveredColumn, setHoveredColumn] = useState<number | null>(null)
  const columnButtons = useRef<(HTMLButtonElement | null)[]>([])
  const requestLock = useRef(false)
  const busy = props.pending || props.phase === 'spinning'
  const validReels = props.reels.length === 5 && props.reels.every(reel => reel.length > 0 && reel.length <= 6)
  const validWager = Number.isSafeInteger(props.wagerPoints) && props.wagerPoints > 0 &&
    props.wagerOptions.includes(props.wagerPoints) && Number.isFinite(props.pointValueInCents) && props.pointValueInCents > 0
  const placementValid = props.placement !== null && Number.isInteger(props.placement.column) &&
    props.placement.column >= 0 && props.placement.column < 5 && props.placement.wagerPoints === props.wagerPoints &&
    props.grants.some(grant => grant.id === props.placement?.grantId)
  const canPrepare = !busy && props.phase === 'ready' && props.placement === null && validReels && validWager
  const canSpin = !busy && validReels && validWager &&
    (canPrepare || props.phase === 'placed' && placementValid)
  const selectedGrant = canPrepare ? props.grants.find(grant => grant.id === selectedGrantId) : undefined
  const validGrant = (grant: WukongPawGrant) => Boolean(grant.id) && Number.isSafeInteger(grant.wagerPoints) && grant.wagerPoints > 0
  const observedScrolls = Number.isSafeInteger(props.observedEarnScrolls) && props.observedEarnScrolls >= 0
    ? props.observedEarnScrolls : 0

  useEffect(() => { requestLock.current = false }, [props.pending, props.phase, props.placement?.id, props.error])

  function cashValueLabel(symbol: WukongPawPreviewSymbol) {
    if (!validWager || symbol === 'PAW_EARN_SCROLL' || !symbol.startsWith('RAND_')) return undefined
    const multiplier = WUKONG_SYMBOLS.definitions[symbol]?.wagerMultiplier
    return multiplier === undefined ? undefined
      : `R${previewCashFormatter.format(multiplier * props.wagerPoints * props.pointValueInCents / 100)}`
  }

  function selectGrant(grant: WukongPawGrant) {
    if (canPrepare && validGrant(grant) && !requestLock.current) setSelectedGrantId(grant.id)
  }

  function place(grantId: string | undefined, column: number) {
    const grant = props.grants.find(item => item.id === grantId)
    if (!canPrepare || !grant || !validGrant(grant) || requestLock.current) return
    requestLock.current = true
    setHoveredColumn(null)
    props.onPlace(grant.id, column)
  }

  function dragPaw(event: DragEvent<HTMLButtonElement>, grant: WukongPawGrant) {
    if (!canPrepare || !validGrant(grant) || requestLock.current) { event.preventDefault(); return }
    selectGrant(grant)
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData(pawTransferType, grant.id)
  }

  function dropPaw(event: DragEvent<HTMLButtonElement>, column: number) {
    event.preventDefault()
    if (!Array.from(event.dataTransfer.types).includes(pawTransferType)) return
    place(event.dataTransfer.getData(pawTransferType), column)
  }

  function columnKey(event: KeyboardEvent<HTMLButtonElement>, column: number) {
    if (!canPrepare || !selectedGrant) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      place(selectedGrant.id, column)
    } else if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      event.preventDefault()
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? 4
        : (column + (event.key === 'ArrowRight' ? 1 : 4)) % 5
      columnButtons.current[next]?.focus()
    }
  }

  const status = props.phase === 'spinning' ? 'Preview admitted. Wager and placement are locked.'
    : props.pending ? 'A preview request is being confirmed. Controls are locked.'
      : props.phase === 'blocked' ? 'Local preview blocked. No new spin can be submitted.'
    : props.phase === 'settled' ? 'Preview complete. Its result is sealed; set up a new preview to place another paw.'
      : props.placement ? `One paw is reserved for column ${props.placement.column + 1}. Cancel before admission to change it.`
        : selectedGrant ? 'Paw selected. Choose a reel column for the NEXT preview spin.'
          : 'Stored paws are optional. Leave them banked, or choose one for the next preview.'

  return <main className="wukong-paw-preview" data-paw-phase={props.phase}>
    <header className="wukong-paw-preview__heading">
      <span className="wukong-paw-preview__local-badge">LOCAL PREVIEW · SYNTHETIC FIXTURES</span>
      <h1>Wukong’s stored paw rack</h1>
      <p>Keep paws banked. Place one only when you want a guaranteed paw symbol in the next preview.</p>
    </header>

    <section className="wukong-paw-preview__earn-role" aria-labelledby="paw-earn-role-title">
      <EarnScrollIcon />
      <div><h2 id="paw-earn-role-title">Proposed earn symbol · Celestial tally-scroll</h2>
        <p>Distinct from the cash-collecting paw. Proposed art; earning threshold, rate and wager rule are undecided.</p>
        <strong aria-live="polite">{observedScrolls} earn {observedScrolls === 1 ? 'scroll' : 'scrolls'} observed in this preview</strong>
      </div>
      <label className="wukong-paw-preview__fixture-toggle"><input type="checkbox" checked={props.fixtureEarnSymbolInjection}
        disabled={busy || props.phase !== 'ready' || props.placement !== null}
        onChange={event => { if (canPrepare && !requestLock.current) props.onFixtureEarnSymbolChange(event.currentTarget.checked) }} />Show illustrative scroll</label>
      <small>Observed symbols do not award paws. Active earning and live consumption are off.</small>
    </section>

    <section className="wukong-paw-preview__rack" aria-labelledby="paw-rack-title">
      <div className="wukong-paw-preview__rack-heading"><h2 id="paw-rack-title">Stored paw rack</h2>
        <span>{props.grants.length} stored synthetic {props.grants.length === 1 ? 'paw' : 'paws'}</span></div>
      <p id="paw-placement-instructions">Drag a fixture paw to a column, or select a paw then tap a column. Keyboard: Tab to a column, arrows to choose, Enter to place.</p>
      <div className="wukong-paw-preview__grants">
        {props.grants.map((grant, index) => <button key={grant.id} type="button"
          className="wukong-paw-preview__grant" draggable={canPrepare && validGrant(grant)}
          disabled={!canPrepare || !validGrant(grant)} aria-pressed={selectedGrant?.id === grant.id}
          aria-label={`Select stored synthetic paw ${index + 1} (${points(grant.wagerPoints)} recorded)`}
          aria-describedby="paw-placement-instructions" onClick={() => selectGrant(grant)}
          onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); selectGrant(grant) } }}
          onDragStart={event => dragPaw(event, grant)} onDragEnd={() => setHoveredColumn(null)}>
          <img src={WUKONG_SYMBOLS.definitions.PAW?.image} alt="" draggable={false} />
          <span>Paw {index + 1}</span><small>SYNTHETIC FIXTURE</small><small>{points(grant.wagerPoints)} recorded</small>
        </button>)}
        {props.grants.length === 0 && <p className="wukong-paw-preview__empty">No stored fixture paws. An unplaced preview spin is still available.</p>}
      </div>
    </section>

    <p className="wukong-paw-preview__status" role="status" aria-atomic="true">{status}</p>
    {props.error && <p className="wukong-paw-preview__error" role="alert">{props.error}</p>}
    {(!validReels || !validWager || props.phase === 'placed' && !placementValid) &&
      <p className="wukong-paw-preview__error" role="alert">Preview setup is unavailable. No spin can be submitted.</p>}

    <section className="wukong-paw-preview__cabinet" aria-label="Five-column preview cabinet">
      <div role="group" aria-label="Next preview paw placement">
      <SlotMachine cabinetTheme={WUKONG_CABINET_THEME} reelCount={columns.length} renderReel={column =>
        <div className="wukong-paw-preview__reel-stack">
          <button type="button" ref={element => { columnButtons.current[column] = element }}
          className="wukong-paw-preview__column" data-drop-active={hoveredColumn === column}
          data-placed={props.placement?.column === column}
          aria-label={`Place selected synthetic paw on reel column ${column + 1}`}
          aria-describedby="paw-placement-instructions" disabled={!canPrepare || !selectedGrant}
          onClick={() => place(selectedGrant?.id, column)} onKeyDown={event => columnKey(event, column)}
          onDragOver={event => {
            if (canPrepare && Array.from(event.dataTransfer.types).includes(pawTransferType)) {
              event.preventDefault(); event.dataTransfer.dropEffect = 'move'; setHoveredColumn(column)
            }
          }} onDragLeave={() => setHoveredColumn(null)} onDrop={event => dropPaw(event, column)}>
          <span>Column {column + 1}</span><strong>{props.placement?.column === column ? 'Paw locked' : 'Place paw'}</strong>
          </button>
          {(props.reels[column] ?? []).map((symbol, row) => symbol === 'PAW_EARN_SCROLL'
          ? <span className="wukong-paw-preview__earn-symbol" key={row} role="img" aria-label="Proposed Celestial tally-scroll earn symbol">
            <EarnScrollIcon /><small>PROPOSED EARN SCROLL</small></span>
          : <SlotSymbol key={row} animated={false} symbol={symbol} symbolSet={WUKONG_SYMBOLS} reelIndex={column} rowIndex={row} valueLabel={cashValueLabel(symbol)} />)}
        </div>} />
      </div>
      {busy && <div className="wukong-paw-preview__spin-lock" role="status">Resolving local preview…</div>}
    </section>

    <section className="wukong-paw-preview__controls" aria-label="Preview controls">
      <fieldset disabled={!canPrepare}><legend>Preview wager · no account funds</legend>
        <div className="wukong-paw-preview__wagers">{props.wagerOptions.map(wager => <button key={wager} type="button"
          aria-pressed={props.wagerPoints === wager} onClick={() => { if (canPrepare && !requestLock.current) { setSelectedGrantId(null); props.onWager(wager) } }}>
          {points(wager)}</button>)}</div>
        <small>Reference value: {validWager ? `R${(props.wagerPoints * props.pointValueInCents / 100).toFixed(2)}` : 'unavailable'}. Recorded grant wagers do not select an eligibility rule.</small>
      </fieldset>
      <div className="wukong-paw-preview__actions">
        <button className="wukong-paw-preview__spin" type="button" disabled={!canSpin} onClick={() => {
          if (canSpin && !requestLock.current) { requestLock.current = true; props.onSpin() }
        }}>Preview spin{props.placement ? ' with placed paw' : ' · keep paws stored'}</button>
        <button type="button" disabled={busy || props.phase !== 'placed' || props.placement === null} onClick={() => {
          if (!busy && props.phase === 'placed' && !requestLock.current) { setSelectedGrantId(null); props.onClear() }
        }}>Cancel placement before admission</button>
        {props.phase === 'settled' && <button type="button" disabled={busy} onClick={() => { if (!busy) { setSelectedGrantId(null); props.onNextPreview() } }}>Set up next preview</button>}
      </div>
    </section>

    {props.lastResult && <section className="wukong-paw-preview__result" aria-labelledby="paw-result-title">
      <h2 id="paw-result-title">Last sealed preview result</h2>
      <dl><div><dt>Paw symbols</dt><dd>{props.lastResult.pawCount}</dd></div>
        <div><dt>Grabbed coin value</dt><dd>{points(props.lastResult.moneyGrabPoints)}</dd></div>
        <div><dt>Total return</dt><dd>{points(props.lastResult.totalPoints)}</dd></div></dl>
      <p>A guaranteed paw is not a guaranteed win. With no cash symbols, its grab can return zero.</p>
    </section>}
    <footer className="wukong-paw-preview__footer"><span>Local in-memory fixture bank: survives page reloads, resets when the host restarts. No wallet, purchases or automatic earning/use.</span>
      <button type="button" disabled={busy || props.placement !== null} onClick={() => { if (!busy && props.placement === null) { setSelectedGrantId(null); props.onResetFixture() } }}>Reset synthetic fixture inventory</button></footer>
  </main>
}
