import { useEffect, useMemo, useState, type ReactNode } from 'react'
import type { HoldemCard, HoldemSeat, HoldemTable } from './contracts'
import chip1 from './assets/chip-1.png'
import chipHalf from './assets/chip-050.png'
import chip2 from './assets/chip-2.png'
import chip5 from './assets/chip-5.png'
import chip10 from './assets/chip-10.png'
import chip25 from './assets/chip-25.png'
import chip50 from './assets/chip-50.png'

export type HoldemCardRenderer = (card: HoldemCard, index: number, scope: string) => ReactNode

/** A view of server state only. This package never draws cards or decides a winner. */
export function HoldemTableSurface({ table, renderCard, revealDelay = 150 }: {
  table: HoldemTable
  renderCard: HoldemCardRenderer
  revealDelay?: number
}) {
  const handKey = `${table.matchId}:${table.handNumber}`
  const [reveal, setReveal] = useState({ handKey, count: table.communityCards.length })
  const visibleCount = reveal.handKey === handKey ? reveal.count : 0
  useEffect(() => {
    if (visibleCount >= table.communityCards.length) return
    if (revealDelay <= 0) {
      setReveal({ handKey, count: table.communityCards.length })
      return
    }
    const timer = window.setTimeout(() => setReveal({ handKey, count: visibleCount + 1 }), revealDelay)
    return () => window.clearTimeout(timer)
  }, [handKey, revealDelay, table.communityCards.length, visibleCount])
  const seats = useMemo(() => arrangeSeats(table.seats), [table.seats])
  const occupied = new Set(seats.map(entry => entry.position))
  const actor = table.seats.find(seat => seat.seat === table.activeSeat)
  const potUnit = Math.floor(table.pot / 150) * 50
  const potStacks = potUnit > 0 ? [potUnit, potUnit, table.pot - 2 * potUnit] : [table.pot]
  const completed = table.status !== 'active'
  const turnText = completed ? 'Showdown' : actor?.isCurrentPlayer ? 'Your turn' : actor ? `${actor.displayName} to act` : 'Waiting for the table'
  return <div className={`ff-holdem-table ff-holdem-table--${seats.length}-seats`} aria-label="Texas Hold’em table">
    <div className="ff-holdem-table-meta">
      <span>{table.tableRule?.name ?? 'Standard'} <i>•</i> No limit</span>
      <span>R{(table.tableRule?.smallBlindCredits ?? 0.5).toFixed(2)} / R{(table.tableRule?.bigBlindCredits ?? 1).toFixed(2)} <i>•</i> Hand {table.handNumber} <i>•</i> {table.street.replaceAll('-', ' ')}</span>
    </div>
    <div className="ff-holdem-stage">
      <div className="ff-holdem-board">
        <div className="ff-holdem-pot" aria-label={`Total pot R${money(table.pot)}`}>
          {table.pot > 0 && <div className="ff-holdem-pot-chips" aria-hidden="true">{potStacks.map((amount, index) => <ChipPile amount={amount} key={index}/>)}</div>}
          <div><span>Pot</span><strong>R{money(table.pot)}</strong></div>
        </div>
        <div className="ff-holdem-community" aria-label="Community cards">
          {Array.from({ length: 5 }, (_, index) => <div className="ff-holdem-card" key={index}>
            {index < visibleCount && table.communityCards[index]
              ? renderCard(table.communityCards[index], index, `${handKey}-board`)
              : <span className="ff-holdem-card-empty" aria-label="Undealt community card" />}
          </div>)}
        </div>
      </div>
      {seats.map(({ seat, position }) => <Seat key={seat.seatId} seat={seat} position={position}
        renderCard={renderCard} dealer={seat.seat === table.dealerSeat}
        active={!completed && seat.seat === table.activeSeat}
        winner={table.winningSeatIds.includes(seat.seatId)} />)}
      {[0, 1, 2, 3, 4].filter(position => !occupied.has(position)).map(position =>
        <div className={`ff-holdem-open-seat seat-pos-${position}`} key={position} aria-label="Open seat"><span>♧</span><small>Open seat</small></div>)}
      <div className={`ff-holdem-turn-status${actor?.isCurrentPlayer && !completed ? ' is-your-turn' : ''}`} role="status">
        <span aria-hidden="true" />{turnText}
      </div>
    </div>
  </div>
}

function Seat({ seat, position, renderCard, dealer, active, winner }: {
  seat: HoldemSeat; position: number; renderCard: HoldemCardRenderer
  dealer: boolean; active: boolean; winner: boolean
}) {
  const action = seat.lastAction ?? (['folded', 'all-in', 'sitting-out'].includes(seat.status) ? seat.status : null)
  const handName = seat.isCurrentPlayer ? seat.handName : null
  const folded = seat.status === 'folded'
  return <article className={`ff-holdem-seat seat-pos-${position}${seat.isCurrentPlayer ? ' is-current' : ''}${active ? ' is-active' : ''}${winner ? ' is-winner' : ''}${folded ? ' is-folded' : ''}`} aria-label={`${seat.displayName}${seat.isCurrentPlayer ? ', you' : ''}${active ? ', to act' : ''}`}>
    <div className="ff-holdem-seat-cards" aria-label={`${seat.displayName}’s cards`}>
      {seat.holeCards.map((card, index) => <span className="ff-holdem-card" key={index}>{renderCard(card, index, seat.seatId)}</span>)}
    </div>
    <div className="ff-holdem-seat-plaque">
      <div className="ff-holdem-seat-identity"><strong title={seat.displayName}>{seat.displayName}</strong><span aria-label={`${seat.isCurrentPlayer ? 'Your' : `${seat.displayName}’s`} remaining table chips R${money(seat.stack)}`} title={seat.isCurrentPlayer ? 'Your remaining table chips, not your account balance.' : undefined}>{seat.isCurrentPlayer ? 'Table chips ' : ''}R{money(seat.stack)}</span></div>
      {dealer && <span className="ff-holdem-dealer" title="Dealer button" aria-label="Dealer">D</span>}
      {active && <span className="ff-holdem-seat-active" aria-hidden="true" />}
    </div>
    <div className="ff-holdem-seat-action">{winner ? <strong>Winner</strong> : !handName && (action ? <span>{action.replaceAll('-', ' ')}</span> : <span>&nbsp;</span>)}{handName && <span className="ff-holdem-seat-hand">{handName}</span>}</div>
    {seat.committedRound > 0 && <div className="ff-holdem-seat-bet" aria-label={`${seat.displayName} bet R${money(seat.committedRound)}`}>
      <ChipPile amount={seat.committedRound} /><span>R{money(seat.committedRound)}</span>
    </div>}
  </article>
}

/** Real chip artwork, tight vertical offsets; amounts remain exact even for fractional chips. */
export function ChipPile({ amount }: { amount: number }) {
  const values: [number, string][] = [[5000, chip50], [2500, chip25], [1000, chip10], [500, chip5], [200, chip2], [100, chip1], [50, chipHalf]]
  let remaining = Math.max(0, Math.round(amount))
  const images: string[] = []
  for (const [value, src] of values) {
    const count = Math.min(4 - images.length, Math.floor(remaining / value))
    for (let i = 0; i < count; i++) images.push(src)
    remaining -= Math.floor(remaining / value) * value
    if (images.length >= 4) break
  }
  if (!images.length && amount > 0) images.push(chipHalf)
  return <span className="ff-holdem-chip-pile" aria-hidden="true">{images.map((src, index) =>
    <img src={src} alt="" key={index} style={{ transform: `translateY(${-index * 3}px)` }} draggable={false} />)}</span>
}

export function arrangeSeats(seats: readonly HoldemSeat[]): { seat: HoldemSeat; position: number }[] {
  const current = seats.find(seat => seat.isCurrentPlayer) ?? seats[0]
  if (!current) return []
  const ordered = [...seats].sort((a, b) => a.seat - b.seat)
  const start = ordered.findIndex(seat => seat.seatId === current.seatId)
  const rotated = [...ordered.slice(start), ...ordered.slice(0, start)]
  const positions: Record<number, number[]> = { 1: [0], 2: [0, 2], 3: [0, 2, 3], 4: [0, 1, 2, 4], 5: [0, 1, 2, 3, 4] }
  return rotated.map((seat, index) => ({ seat, position: positions[rotated.length]?.[index] ?? index }))
}

function money(cents: number) { return (cents / 100).toFixed(2) }
