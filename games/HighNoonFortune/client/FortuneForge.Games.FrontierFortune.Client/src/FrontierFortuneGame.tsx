import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { SidecarSpinControls } from '@fortuneforge/slot-client-core/components/SidecarSpinControls'
import '@fortuneforge/slot-client-core/styles/sidecarSpinControls.css'
import { ART, CABINET_GEOMETRY } from './art.js'
import { FRONTIER_RULES, FRONTIER_SYMBOLS, HAND_NAMES } from './rules.js'
import { HandRail } from './HandRail.js'
import { FrontierHowToWin } from './FrontierHowToWin.js'
import { PaylineTracers } from './PaylineTracers.js'
import { FrontierWinDisplay } from './FrontierWinDisplay.js'
import { useFrontier } from './useFrontier.js'
import type { FrontierFortuneGameProps } from './types.js'
import './styles.css'

const money = (cents: number) => `R${(cents / 100).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
type SymbolId = keyof typeof ART.symbols

export function FrontierFortuneGame(props: FrontierFortuneGameProps) {
  const c = useFrontier(props)
  const [help, setHelp] = useState(false)
  const [winLineIndex, setWinLineIndex] = useState(0)
  const helpTrigger = useRef<HTMLButtonElement>(null)
  const cells = useRef(new Map<string, HTMLButtonElement>())
  const state = c.state
  const hand = c.activeHand
  const win = c.loading || c.busy || c.retryCommand ? null : c.win
  const activeLine = win?.wins.length ? win.wins[winLineIndex % win.wins.length] : null
  const choosing = hand?.phase === 'choose'
  const complete = Boolean(state?.handRound?.phase === 'complete' && win?.kind === 'hand')
  const dealing = c.busyKind !== null && c.busyKind !== 'confirm'
  const pointCents = state?.wagering.pointValueInCents ?? FRONTIER_RULES.pointValueInCents
  const formatPoints = (points: number) => money(points * pointCents)
  const stake = hand?.wagerPoints ?? (state?.ante.ready ? state.ante.fundedWagerPoints : c.wagerPoints)
  const balanceCents = c.confirmedBalanceCents ?? Math.round((props.account?.balances.slotsCredits ?? 0) * 100)
  const wagerOptions = state ? Array.from({ length: (state.wagering.maxWagerPoints - state.wagering.minWagerPoints) / state.wagering.stepPoints + 1 }, (_, index) => state.wagering.minWagerPoints + index * state.wagering.stepPoints) : [FRONTIER_RULES.minWagerPoints]
  const wagerIndex = Math.max(0, wagerOptions.indexOf(c.wagerPoints))
  const disabled = !state || c.loading || c.busy || Boolean(c.retryCommand) || Boolean(c.error && !state)
  const lineCells = new Set(win?.kind === 'lines' ? win.wins.flatMap(line => line.cells.map(cell => `${cell.column}-${cell.row}`)) : [])
  const required = hand?.hand.filter(card => !card.held).length ?? 0
  const picked = hand?.hand.filter((card, index) => !card.held && c.selectedRows[index] !== null).length ?? 0
  const instruction = c.loading ? 'Saddlin’ up…' : c.retryCommand ? 'Recover the earlier action before ridin’ on.'
    : c.busy ? dealing ? 'Dealin’…' : 'Settin’ your hand…'
      : choosing ? required === 0 ? 'All cards held · Confirm your hand' : `Pick a card per open reel · ${picked}/${required}`
        : hand?.phase === 'hold' ? `Hold cards, then Spin · ${hand.dealsRemaining} ${hand.dealsRemaining === 1 ? 'deal' : 'deals'} left`
          : state?.ante.ready ? 'Ante full · Spin to deal in'
            : complete ? `${state?.handRound?.category ? HAND_NAMES[state.handRound.category] : 'Hand complete'} · ${formatPoints(state?.handRound?.awardPoints ?? 0)} banked`
              : win ? `Paid out ${formatPoints(win.points)}` : 'WANTED fills your saved ante.'

  const openHelp = () => { c.stopAuto(); setHelp(true) }
  useEffect(() => {
    setWinLineIndex(0)
    if (!win || win.wins.length < 2 || c.reducedMotion || help) return
    const count = win.wins.length
    const timer = window.setInterval(() => setWinLineIndex(index => (index + 1) % count), 1800)
    return () => window.clearInterval(timer)
  }, [win?.id, win?.wins.length, c.reducedMotion, help])
  const closeHelp = useCallback(() => {
    setHelp(false)
    window.requestAnimationFrame(() => helpTrigger.current?.focus({ preventScroll: true }))
  }, [])
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (event.code !== 'Space' || event.repeat || help || target?.closest('button,input,select,textarea,a,[contenteditable="true"]')) return
      event.preventDefault(); if (!disabled) c.primary()
    }
    window.addEventListener('keydown', handle); return () => window.removeEventListener('keydown', handle)
  }, [c.primary, disabled, help])

  const content = <main className="frontier" data-phase={hand?.phase ?? (state?.ante.ready ? 'ready' : 'base')}
    data-spinning={dealing} data-reduced-motion={c.reducedMotion}
    style={{ '--frontier-scene': `url("${ART.town}")`,
      '--cabinet-height-ratio': CABINET_GEOMETRY.imageHeight / CABINET_GEOMETRY.imageWidth,
      '--cabinet-aspect': CABINET_GEOMETRY.imageWidth / CABINET_GEOMETRY.imageHeight,
      '--aperture-top': `${CABINET_GEOMETRY.insetPercent.top}%`, '--aperture-right': `${CABINET_GEOMETRY.insetPercent.right}%`,
      '--aperture-bottom': `${CABINET_GEOMETRY.insetPercent.bottom}%`, '--aperture-left': `${CABINET_GEOMETRY.insetPercent.left}%`,
    } as CSSProperties}>
    <header className="frontier-header"><h1><img src={ART.title} alt="Frontier Fortune" /></h1>
      <p className="frontier-status" aria-live="polite" aria-atomic="true">{instruction}</p>
      <button ref={helpTrigger} className="frontier-help" type="button" onClick={openHelp} aria-label="How to win at Frontier Fortune" title="How to Win" aria-haspopup="dialog">?</button>
    </header>
    <div className="frontier-stage">
      <section className="frontier-cabinet" aria-label="Five reels and three rows" aria-busy={dealing}>
        <img className="frontier-cabinet__frame" src={ART.cabinet} alt="" draggable={false} />
        <div className="frontier-reels">
          {(state?.reels ?? FRONTIER_RULES.paylines.slice(0, 5).map(() => ['J', 'Q', 'K'])).map((column, columnIndex) => <div className="frontier-reel" key={columnIndex} data-held={Boolean(choosing && hand?.hand[columnIndex]?.held)}>
            <div className="frontier-reel__strip" style={{ '--reel-delay': `${columnIndex * 40}ms` } as CSSProperties}>
              {(dealing ? [...column, ...column, ...column] : column).map((symbol, rowIndex) => {
                const pickable = Boolean(choosing && !hand?.hand[columnIndex]?.held && !disabled)
                const selected = Boolean(choosing && c.selectedRows[columnIndex] === rowIndex)
                return <button type="button" className="frontier-symbol" key={rowIndex}
                  ref={element => { const key = `${columnIndex}-${rowIndex}`; if (element) cells.current.set(key, element); else cells.current.delete(key) }}
                  data-selected={selected} data-pickable={pickable} data-winning={!dealing && lineCells.has(`${columnIndex}-${rowIndex}`)}
                  disabled={!pickable} aria-pressed={pickable ? selected : undefined}
                  aria-label={`${FRONTIER_SYMBOLS.find(item => item.id === symbol)?.label ?? symbol}, reel ${columnIndex + 1}, row ${rowIndex % 3 + 1}${selected ? ', selected' : ''}`}
                  onClick={() => c.selectCard(columnIndex, rowIndex)}
                  onKeyDown={event => {
                    const nextRow = event.key === 'ArrowUp' ? (rowIndex + 2) % 3 : event.key === 'ArrowDown' ? (rowIndex + 1) % 3 : null
                    if (nextRow !== null) { event.preventDefault(); cells.current.get(`${columnIndex}-${nextRow}`)?.focus() }
                  }}>
                  <img src={ART.symbols[symbol as SymbolId]} alt="" draggable={false} />
                  {selected && <span className="frontier-symbol__picked" aria-hidden="true">PICKED</span>}
                </button>
              })}
            </div>
            {choosing && hand?.hand[columnIndex]?.held && <span className="frontier-reel__held">HELD</span>}
          </div>)}
          {win?.kind === 'lines' && <PaylineTracers wins={win.wins} activeLine={activeLine?.line ?? null} />}
          {win && <FrontierWinDisplay key={win.id} amount={formatPoints(win.points)}
            label={win.kind === 'hand' && win.category ? HAND_NAMES[win.category] : 'TOTAL WIN'}
            detail={activeLine ? `Line ${activeLine.line} · ${formatPoints(activeLine.awardPoints)}${win.wins.length > 1 ? ` · ${winLineIndex % win.wins.length + 1}/${win.wins.length} winning lines` : ''}` : 'Outlaw’s Hand'} />}
        </div>
      </section>
      {state && <HandRail ante={state.ante} round={state.handRound} reels={state.reels} selectedRows={c.selectedRows} held={c.held}
        busy={c.busy || Boolean(c.retryCommand)} showComplete={complete} formatPoints={formatPoints} onToggle={c.toggleHold} />}
      <div className="frontier-controls">
        <SidecarSpinControls activeWager={stake * pointCents / 100} disabled={disabled || choosing || (!hand && !state?.ante.ready && balanceCents < stake * pointCents)}
          freeSpinsRemaining={0} isAutoSpinning={c.auto} isFreeSpinBadgePopping={false} isSpinning={dealing} isStopRequested={false} allowStop={false}
          onAutoSpinChange={c.setAutoSpinning} onSelectWager={(index: number) => c.changeWager(wagerOptions[index])} onSpin={c.primary}
          pointValueInCents={pointCents} showFreeSpinBadge={false} specialControlsActive={false} specialRoundLabel={null}
          wagerLocked={Boolean(hand || state?.ante.ready || c.retryCommand)} wagerIndex={wagerIndex} wagerOptions={wagerOptions}
          autoSpinReplacement={choosing ? <button className="frontier-confirm" type="button" disabled={disabled || !c.canConfirm} onClick={c.primary}>
            {c.busy ? 'Settin’…' : !c.canConfirm ? `Pick ${picked}/${required}` : hand.dealsRemaining === 0 ? 'Show hand' : 'Keep cards'}
          </button> : hand || state?.ante.ready ? <span className="frontier-deal-count" aria-label={hand ? `${hand.dealsRemaining} included deals remain` : 'Ante full'}>{hand ? hand.dealsRemaining : state?.ante.requiredChips}<small>{hand ? 'DEALS' : 'ANTE'}</small></span> : undefined}
          wagerReadout={hand || state?.ante.ready ? <>HAND <strong>{formatPoints(stake)}</strong></> : undefined} />
      </div>
    </div>
    {c.error && <div className="frontier-error" role="alert"><p>{c.error}</p><button type="button" disabled={c.busy} onClick={c.retry}>{c.retryCommand ? 'Recover action' : 'Reload table'}</button></div>}
    {props.paymentAlerts}
    <FrontierHowToWin open={help} onClose={closeHelp} formatPoints={formatPoints} />
  </main>
  return props.renderShell({ balance: balanceCents / 100, onNavigate: c.stopAuto, children: content })
}
