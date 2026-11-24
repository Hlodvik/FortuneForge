import { useEffect, useId, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { CROOKED_ROAD_ARTWORK, CROOKED_ROAD_SYMBOL_IMAGES } from './symbols'
import { decodeRoadFeature, decodeRoadState, ROAD_DESTINATIONS, ROAD_ENTRY_CELL, roadPaths, roadTilePorts, type RoadAction, type RoadBonus,
  type RoadDestination, type RoadTile } from './road'
import './CrookedRoadPanel.css'

const destinations = Object.keys(ROAD_DESTINATIONS) as RoadDestination[]
const tileLabels = { straight: 'Straight', corner: 'Bend', junction: 'Junction' } as const
const currency = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' })
const money = (points: number, pointValue: number) => currency.format(points * pointValue / 100)

function RoadTileDrawing({ tile }: { tile: RoadTile }) {
  const artwork = tile.kind === 'straight' ? CROOKED_ROAD_ARTWORK.roadTiles.straight : CROOKED_ROAD_ARTWORK.roadTiles.junction
  return <span className="crooked-road__tile" aria-hidden="true" data-kind={tile.kind}
    style={{ transform: `rotate(${tile.rotation * 90}deg)`, '--road-tile-texture': `url("${CROOKED_ROAD_ARTWORK.roadTiles.straight}")` } as CSSProperties}>
    <img src={artwork} alt="" style={{ transform: tile.kind === 'straight' ? undefined : 'rotate(180deg)' }} />
    {tile.kind === 'corner' && <span className="crooked-road__corner-inlay" />}
  </span>
}

function RoadBonusExperience({ bonus, completed, blocked, pointValue, submit }: {
  bonus: RoadBonus
  completed: boolean
  blocked: boolean
  pointValue: number
  submit: (action: Omit<RoadAction, 'expectedRevision'>) => void
}) {
  const choiceRequired = !completed && bonus.choice === null && (bonus.kind === 'hill' || bonus.kind === 'well' && bonus.step === 0)
  return <section className={`crooked-road__bonus crooked-road__bonus--${bonus.kind}`} aria-label={ROAD_DESTINATIONS[bonus.kind].label}>
    <div className="crooked-road__bonus-heading">
      <div><span className="crooked-road__eyebrow">{completed ? 'Journey complete' : 'Finbar’s journey'}</span><h3>{ROAD_DESTINATIONS[bonus.kind].label}</h3></div>
      <div className="crooked-road__bonus-total"><span>Journey win</span><strong>{money(bonus.awardPoints, pointValue)}</strong></div>
    </div>
    <p className="crooked-road__funded-stake">Funded stake {money(bonus.fundedWagerPoints, pointValue)} · Journey spins cost no additional wager.</p>
    {bonus.kind === 'well' && <>
      <p>{choiceRequired ? 'Choose the spring where Finbar will cast his first wish.' :
        `Wish ${Math.min(bonus.step + 1, bonus.totalSteps)} of ${bonus.totalSteps} · Wild ripples spread from your chosen spring.`}</p>
      <div className="crooked-road__spring-choices" role="group" aria-label="Choose a Wishing Well spring">
        {['Upper spring', 'Middle spring', 'Lower spring'].map((label, choice) =>
          <button key={label} type="button" disabled={blocked || !choiceRequired} aria-pressed={bonus.choice === choice}
            onClick={() => submit({ action: 'choose-spring', choice })}>
            <span aria-hidden="true">◉</span>{label}
          </button>)}
      </div>
      <p className="crooked-road__board-note">The reels show your wish and its expanding wild ripple.</p>
      {!choiceRequired && !completed && <p className="crooked-road__next-step">Use Spin to reveal the next wish.</p>}
    </>}
    {bonus.kind === 'fort' && <>
      <p>Fairy coins stay put. Every new coin restores three respins.</p>
      <div className="crooked-road__fort-play">
        <div className="crooked-road__coin-grid" aria-label="Fairy Fort held coins">
          {(bonus.held ?? Array<number>(9).fill(0)).map((score, cell) =>
            <span key={cell} className="crooked-road__coin-cell" data-held={score > 0 || undefined}
              aria-label={`Position ${cell + 1}: ${score > 0 ? `${score} coin value held` : 'empty'}`}>
              {score > 0 ? <><img src={CROOKED_ROAD_ARTWORK.goldPot} alt="" /><strong>{score}</strong></> : <span aria-hidden="true">✧</span>}
            </span>)}
        </div>
        <div className="crooked-road__respins"><strong>{bonus.respinsRemaining}</strong><span>respins left</span>
          <div aria-hidden="true">{[0, 1, 2].map(index => <span key={index} data-lit={index < bonus.respinsRemaining || undefined}>☘</span>)}</div>
          <small>Reveal {bonus.step} of at most {bonus.totalSteps}</small>
        </div>
      </div>
      <p className="crooked-road__next-step">{completed ? 'Finbar’s fairy-coin collection is complete.' : 'Use Spin to search the fort again.'}</p>
    </>}
    {bonus.kind === 'hill' && <>
      <p>Three chambers, three choices. Pick a door, then use Spin to reveal its treasure.</p>
      <div className="crooked-road__doors" role="group" aria-label={`Choose a door for chamber ${Math.min(bonus.step + 1, 3)}`}>
        {[0, 1, 2].map(index => {
          const door = bonus.doors.find(entry => entry.index === index)
          return <button key={index} type="button" disabled={blocked || !choiceRequired}
            aria-pressed={bonus.choice === index || completed && door?.selected === true} data-revealed={door?.revealed || undefined}
            aria-label={`Door ${index + 1}${door?.revealed && door.prizePoints !== null ? `, ${money(door.prizePoints, pointValue)}` : ''}`}
            onClick={() => submit({ action: 'choose-door', choice: index })}>
            <img src={CROOKED_ROAD_SYMBOL_IMAGES.FREE} alt="" />
            <span>Door {index + 1}</span>
            {door?.revealed && door.prizePoints !== null && <strong>{money(door.prizePoints, pointValue)}</strong>}
          </button>
        })}
      </div>
      <p className="crooked-road__next-step">{completed ? 'All three chambers explored.' : `Chamber ${Math.min(bonus.step + 1, 3)} of 3${bonus.choice !== null ? ' · Your door is ready. Use Spin to open it.' : ''}`}</p>
    </>}
  </section>
}

export type CrookedRoadPanelProps = {
  state: unknown | null
  feature: unknown | null
  spinKey: number
  isSpinning: boolean
  reducedMotion: boolean
  pointValueInCents: number
  actionPending: boolean
  disabled?: boolean
  error?: string | null
  onAction: (action: unknown) => Promise<void>
  onOpen?: () => void
  onPresentationBusyChange?: (busy: boolean) => void
}

export function CrookedRoadPanel(props: CrookedRoadPanelProps) {
  const { state, isSpinning, reducedMotion, pointValueInCents, actionPending, error, onAction,
    onPresentationBusyChange, disabled = false, onOpen } = props
  const road = useMemo(() => decodeRoadState(state), [state])
  const feature = useMemo(() => decodeRoadFeature(props.feature), [props.feature])
  const paths = useMemo(() => road ? roadPaths(road.board) : {}, [road])
  const [handIndex, setHandIndex] = useState(0)
  const [rotation, setRotation] = useState(0)
  const [selectedCell, setSelectedCell] = useState<number | null>(null)
  const [localPending, setLocalPending] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const [travelling, setTravelling] = useState(false)
  const [finbarCell, setFinbarCell] = useState(ROAD_ENTRY_CELL)
  const [dismissedBonusId, setDismissedBonusId] = useState<string | null>(null)
  const cells = useRef<(HTMLButtonElement | null)[]>([])
  const actionGuard = useRef(false)
  const lastJourney = useRef<string | null>(null)
  const headingId = useId()
  const instructionsId = useId()
  const activeJourney = road?.activeBonus?.id ?? null
  const travelPath = road?.lastTravel?.path.join(',') ?? ''
  const activeBonus = road?.activeBonus ?? null
  const bonus = activeBonus ?? (feature?.completed && feature.bonus.id !== dismissedBonusId ? feature.bonus : null)
  const completed = activeBonus === null && feature?.completed === true
  const editingBlocked = disabled || isSpinning || actionPending || localPending || travelling || activeBonus !== null
  const selectedTile = road?.hand[Math.min(handIndex, Math.max(0, road.hand.length - 1))]
  const placedTile = selectedCell === null ? null : road?.board[selectedCell]

  useEffect(() => {
    if (!activeJourney || !travelPath) {
      setFinbarCell(ROAD_ENTRY_CELL)
      return
    }
    if (lastJourney.current === activeJourney) return
    lastJourney.current = activeJourney
    const path = travelPath.split(',').map(Number)
    if (reducedMotion) {
      setFinbarCell(path[path.length - 1] ?? ROAD_ENTRY_CELL)
      return
    }
    let position = 0
    setFinbarCell(path[0] ?? ROAD_ENTRY_CELL)
    setTravelling(true)
    onPresentationBusyChange?.(true)
    const timer = window.setInterval(() => {
      position++
      setFinbarCell(path[Math.min(position, path.length - 1)] ?? ROAD_ENTRY_CELL)
      if (position >= path.length) {
        window.clearInterval(timer)
        setTravelling(false)
        onPresentationBusyChange?.(false)
      }
    }, 320)
    return () => {
      window.clearInterval(timer)
      setTravelling(false)
      onPresentationBusyChange?.(false)
    }
  }, [activeJourney, travelPath, reducedMotion, onPresentationBusyChange])

  useEffect(() => {
    if (!completed || !bonus) return
    onPresentationBusyChange?.(true)
    return () => onPresentationBusyChange?.(false)
  }, [completed, bonus?.id, onPresentationBusyChange])

  async function perform(action: Omit<RoadAction, 'expectedRevision'>) {
    if (!road || actionGuard.current || disabled || isSpinning || actionPending || travelling) return
    actionGuard.current = true
    setLocalPending(true)
    setLocalError(null)
    try {
      await onAction({ ...action, expectedRevision: road.revision })
      if (action.action === 'place' || action.action === 'remove') setSelectedCell(null)
      if (action.action === 'place') setRotation(0)
    } catch (cause) {
      setLocalError(cause instanceof Error ? cause.message : 'The road could not be updated. Try the action again.')
    } finally {
      actionGuard.current = false
      setLocalPending(false)
    }
  }
  const submit = (action: Omit<RoadAction, 'expectedRevision'>) => { void perform(action) }
  function chooseCell(cell: number) {
    if (!road || editingBlocked) return
    if (road.board[cell]) { setSelectedCell(cell); return }
    if (selectedTile) submit({ action: 'place', handIndex: road.hand.indexOf(selectedTile), cellIndex: cell, rotation })
  }
  function cellKey(event: KeyboardEvent<HTMLButtonElement>, cell: number) {
    const offsets: Readonly<Record<string, number>> = { ArrowUp: -3, ArrowRight: 1, ArrowDown: 3, ArrowLeft: -1 }
    if (event.key in offsets) {
      event.preventDefault()
      const target = cell + offsets[event.key]!
      if (target < 0 || target > 8 || event.key === 'ArrowRight' && cell % 3 === 2 || event.key === 'ArrowLeft' && cell % 3 === 0) return
      cells.current[target]?.focus()
    } else if (!editingBlocked && (event.key.toLowerCase() === 'r' || event.key === 'Delete' || event.key === 'Backspace')) {
      event.preventDefault()
      const tile = road?.board[cell]
      if (tile) submit(event.key.toLowerCase() === 'r'
        ? { action: 'rotate', cellIndex: cell, rotation: (tile.rotation + 1) % 4 }
        : { action: 'remove', cellIndex: cell })
    } else if (event.key === 'Escape') setSelectedCell(null)
  }

  if (!road) return <section className="crooked-road" aria-label="The Crooked Road">
    <p role="status">{state == null ? 'Unfolding Finbar’s road map…' : 'The road map could not be read. Reload the game to restore your journey.'}</p>
  </section>
  const reserveCount = Object.values(road.reserveTileCounts).reduce((total, count) => total + count, 0)
  const availableDestinations = destinations.filter(id => road.reachableDestinations.includes(id) && paths[id])
  const travelDestination = road.lastTravel?.destination
  return <section className="crooked-road" aria-labelledby={headingId} data-travelling={travelling || undefined}
    data-bonus-active={bonus !== null || undefined} data-feature-kind={bonus?.kind}
    onFocusCapture={onOpen} onPointerDownCapture={onOpen}>
    <header className="crooked-road__heading">
      <img className="crooked-road__portrait" src={CROOKED_ROAD_ARTWORK.finbar} alt="Finbar the Wayward" />
      <div><span className="crooked-road__eyebrow">{bonus ? 'Finbar’s Otherworld journey' : 'Build a path. Find the Otherworld.'}</span><h2 id={headingId}>{bonus ? ROAD_DESTINATIONS[bonus.kind].label : 'Finbar’s road map'}</h2></div>
      <span className="crooked-road__journey-count">{road.completedJourneys}<span>journeys</span></span>
    </header>
    <div className="crooked-road__layout">
      <div className="crooked-road__map-wrap" style={{ '--crooked-map-art': `url("${CROOKED_ROAD_ARTWORK.mapBoard}")` } as CSSProperties}>
        {destinations.map(id => <span key={id} className={`crooked-road__map-destination crooked-road__map-destination--${id}`}>{ROAD_DESTINATIONS[id].label}</span>)}
        <div className="crooked-road__map" role="group" aria-label="Three by three road map" aria-describedby={instructionsId}>
          {road.board.map((tile, cell) => <button key={cell} type="button" className="crooked-road__cell"
            ref={element => { cells.current[cell] = element }} disabled={editingBlocked}
            data-selected={selectedCell === cell || undefined}
            data-on-path={availableDestinations.some(id => paths[id]?.includes(cell)) || undefined}
            aria-label={`Row ${Math.floor(cell / 3) + 1}, column ${cell % 3 + 1}: ${tile ? `${tileLabels[tile.kind]} facing ${roadTilePorts(tile).join(' and ')}` : 'empty; place selected tile'}`}
            aria-pressed={selectedCell === cell} onClick={() => chooseCell(cell)} onKeyDown={event => cellKey(event, cell)}>
            {tile ? <RoadTileDrawing tile={tile} /> : <span className="crooked-road__empty" aria-hidden="true">+</span>}
          </button>)}
          <img className="crooked-road__finbar" src={CROOKED_ROAD_ARTWORK.finbar} alt="Finbar" data-idle={!travelling && !activeBonus || undefined}
            style={{ left: `${(finbarCell % 3 + .5) * 100 / 3}%`, top: `${(Math.floor(finbarCell / 3) + .5) * 100 / 3}%` }} />
        </div>
        <span className="crooked-road__entrance">Finbar starts here ↑</span>
      </div>
      <aside className="crooked-road__satchel" aria-label="Road tiles and journey tickets">
        <h3>Ready tiles</h3>
        <div className="crooked-road__hand" role="group" aria-label="Select a road tile">
          {[0, 1].map(index => {
            const tile = road.hand[index]
            const selected = selectedTile?.id === tile?.id && tile !== undefined
            return <button key={index} type="button" disabled={editingBlocked || !tile} aria-pressed={selected}
              aria-label={tile ? `Select ${tileLabels[tile.kind]} tile ${index + 1}` : `Ready slot ${index + 1}, empty`}
              onClick={() => { setHandIndex(index); setRotation(0); setSelectedCell(null) }}>
              {tile ? <><RoadTileDrawing tile={{ ...tile, rotation: selected ? rotation : 0 }} /><span>{tileLabels[tile.kind]}</span></> : <span>Next tile</span>}
            </button>
          })}
        </div>
        <button className="crooked-road__turn-hand" type="button" disabled={editingBlocked || !selectedTile}
          onClick={() => setRotation(current => (current + 1) % 4)}>↻ Turn tile</button>
        <p className="crooked-road__queue">{road.queuedTiles.length + reserveCount} queued{reserveCount > 0 ? ` · ${reserveCount} in reserve` : ''}</p>
        {road.queuedTiles.length > 0 && <div className="crooked-road__queued-tiles" aria-label="Next queued tiles">
          {road.queuedTiles.slice(0, 4).map(tile => <span key={tile.id} title={`${tileLabels[tile.kind]} next`}>
            <RoadTileDrawing tile={tile} /><span className="crooked-road__sr">{tileLabels[tile.kind]}</span>
          </span>)}
        </div>}
        <div className="crooked-road__edit-actions" role="group" aria-label="Edit selected road tile">
          <button type="button" disabled={editingBlocked || !placedTile || selectedCell === null}
            onClick={() => placedTile && selectedCell !== null && submit({ action: 'rotate', cellIndex: selectedCell, rotation: (placedTile.rotation + 1) % 4 })}>↻ Rotate</button>
          <button type="button" disabled={editingBlocked || !placedTile || selectedCell === null}
            onClick={() => selectedCell !== null && submit({ action: 'remove', cellIndex: selectedCell })}>Remove tile</button>
        </div>
        <div className="crooked-road__funding">
          <strong>{road.tickets.length} journey {road.tickets.length === 1 ? 'ticket' : 'tickets'}</strong>
          <label htmlFor={`${headingId}-funding`}>{road.funding.spins} / {road.funding.requiredSpins} to next ticket</label>
          <progress id={`${headingId}-funding`} max={road.funding.requiredSpins} value={road.funding.spins} />
        </div>
      </aside>
    </div>
    <div className="crooked-road__destinations" aria-label="Choose a connected destination">
      {destinations.map(id => {
        const target = ROAD_DESTINATIONS[id]
        const reachable = availableDestinations.includes(id)
        return <article key={id} data-connected={reachable || undefined}>
          <button type="button" disabled={editingBlocked || !reachable || road.tickets.length === 0}
            title={target.description} aria-label={`Set off to ${target.label}`}
            onClick={() => submit({ action: 'set-off', destination: id })}><strong>{target.label}</strong><span>Set off{reachable ? ' →' : ''}</span></button>
          <small>{activeBonus ? 'Journey active' : !reachable ? 'Build a route' : road.tickets.length === 0 ? 'Ticket needed' : 'Connected'}</small>
        </article>
      })}
    </div>
    <details id={instructionsId} className="crooked-road__instructions">
      <summary>How to build your road</summary>
      <p>Choose a ready tile, turn it, then tap an empty square. Tap a placed tile to rotate or remove it. Join the village gate to a destination, then set off with a funded ticket. Your route pieces return for reuse.</p>
      <p>Keyboard: arrow keys move; Enter places or selects; R rotates; Delete removes. Tickets keep their funded stake; three fairy doors can also award one.</p>
    </details>
    <div className="crooked-road__status" role="status" aria-live="polite" aria-atomic="true">
      {error ?? localError ?? (travelling ? `Finbar is off to the ${travelDestination ? ROAD_DESTINATIONS[travelDestination].label : 'next destination'}!` :
        actionPending || localPending ? 'Updating your road…' : availableDestinations.length > 0 && !activeBonus ? 'A road is connected. Choose a destination when you have a journey ticket.' : '')}
    </div>
    {feature?.kind === 'hill' && !feature.completed && activeBonus?.id === feature.bonus.id &&
      <p className="crooked-road__previous-door" role="status">Previous chamber: {feature.bonus.doors.filter(door => door.revealed && door.prizePoints !== null)
        .map(door => `Door ${door.index + 1} revealed ${money(door.prizePoints!, pointValueInCents)}`).join('. ')}</p>}
    {bonus && <RoadBonusExperience bonus={bonus} completed={completed} blocked={disabled || isSpinning || actionPending || localPending || travelling || completed}
      pointValue={pointValueInCents} submit={submit} />}
    {completed && bonus && <button className="crooked-road__return" type="button"
      onClick={() => setDismissedBonusId(bonus.id)}>Return to the road</button>}
  </section>
}
