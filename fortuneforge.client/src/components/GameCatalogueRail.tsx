import { Children, useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import './GameCatalogueRail.css'

export type GameCatalogueRailProps = {
  children: ReactNode
  label: string
  className?: string
  /** Reset the row when a filter changes but its cards keep the same keys. */
  resetKey?: string
}

type Drag = { pointerId: number; startX: number; startY: number; startLeft: number; moved: boolean }
const edgeTolerance = 2

export function GameCatalogueRail({ children, label, className = '', resetKey = '' }: GameCatalogueRailProps) {
  const viewport = useRef<HTMLDivElement>(null)
  const track = useRef<HTMLDivElement>(null)
  const drag = useRef<Drag | null>(null)
  const suppressClick = useRef(false)
  const [dragging, setDragging] = useState(false)
  const [edges, setEdges] = useState({ previous: false, next: false })
  const viewportId = useId()
  const instructionId = useId()
  const cards = Children.toArray(children)
  const contentKey = cards.map((card, index) => typeof card === 'object' && card !== null && 'key' in card ? card.key : index).join('|')

  const updateEdges = useCallback(() => {
    const element = viewport.current
    if (!element) return
    const max = Math.max(0, element.scrollWidth - element.clientWidth)
    const next = { previous: element.scrollLeft > edgeTolerance, next: element.scrollLeft < max - edgeTolerance }
    setEdges(previous => previous.previous === next.previous && previous.next === next.next ? previous : next)
  }, [])

  useEffect(() => {
    const element = viewport.current
    if (!element) return
    element.scrollLeft = 0
    updateEdges()
  }, [contentKey, resetKey, updateEdges])

  useEffect(() => {
    updateEdges()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(updateEdges)
    if (viewport.current) observer?.observe(viewport.current)
    if (track.current) observer?.observe(track.current)
    window.addEventListener('resize', updateEdges)
    return () => { observer?.disconnect(); window.removeEventListener('resize', updateEdges) }
  }, [updateEdges])

  function moveTo(left: number) {
    const element = viewport.current
    if (!element) return
    const target = Math.min(Math.max(0, left), Math.max(0, element.scrollWidth - element.clientWidth))
    const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    element.scrollTo({ left: target, behavior: reducedMotion ? 'instant' : 'smooth' })
  }

  function move(direction: -1 | 1) {
    const element = viewport.current
    if (element) moveTo(element.scrollLeft + direction * Math.max(1, element.clientWidth * .85))
  }

  function handleKey(event: KeyboardEvent<HTMLDivElement>) {
    // Preserve keys belonging to controls supplied inside a card.
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey
      || (event.target instanceof HTMLElement && event.target.matches('input, textarea, select, [contenteditable="true"]'))) return
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault()
      move(event.key === 'ArrowLeft' ? -1 : 1)
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault()
      moveTo(event.key === 'Home' ? 0 : Number.POSITIVE_INFINITY)
    }
  }

  function startDrag(event: PointerEvent<HTMLDivElement>) {
    suppressClick.current = false
    // Let the browser handle touch momentum, vertical page scrolling, and zoom.
    if (event.pointerType === 'touch' || event.button !== 0 || event.isPrimary === false) return
    drag.current = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, startLeft: event.currentTarget.scrollLeft, moved: false }
  }

  function moveDrag(event: PointerEvent<HTMLDivElement>) {
    const current = drag.current
    if (!current || current.pointerId !== event.pointerId) return
    const deltaX = event.clientX - current.startX
    const deltaY = event.clientY - current.startY
    if (!current.moved) {
      if (Math.abs(deltaX) < 7 || Math.abs(deltaX) <= Math.abs(deltaY)) return
      current.moved = true
      suppressClick.current = true
      setDragging(true)
      event.currentTarget.setPointerCapture?.(event.pointerId)
    }
    event.preventDefault()
    const element = event.currentTarget
    element.scrollLeft = Math.min(Math.max(0, current.startLeft - deltaX), Math.max(0, element.scrollWidth - element.clientWidth))
    updateEdges()
  }

  function endDrag(event: PointerEvent<HTMLDivElement>) {
    if (drag.current?.pointerId !== event.pointerId) return
    drag.current = null
    setDragging(false)
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
    updateEdges()
  }

  return (
    <div className={`game-catalogue-rail ${className}`.trim()} data-dragging={dragging || undefined}>
      <span id={instructionId} className="game-catalogue-rail__instructions">Swipe, drag, or use the arrow keys to browse games.</span>
      <button type="button" className="game-catalogue-rail__arrow game-catalogue-rail__arrow--previous"
        aria-label={`Previous games in ${label}`} aria-controls={viewportId} disabled={!edges.previous} onClick={() => move(-1)}>
        <RailArrow direction="previous" />
      </button>
      <div id={viewportId} ref={viewport} className="game-catalogue-rail__viewport" role="region" aria-label={label}
        aria-describedby={instructionId} tabIndex={0} onScroll={updateEdges} onKeyDown={handleKey}
        onPointerDown={startDrag} onPointerMove={moveDrag} onPointerUp={endDrag} onPointerCancel={endDrag}
        onPointerLeave={event => { if (!drag.current?.moved) endDrag(event) }}
        onLostPointerCapture={endDrag} onDragStart={event => event.preventDefault()}
        onClickCapture={event => {
          if (suppressClick.current && event.detail !== 0) { event.preventDefault(); event.stopPropagation() }
          suppressClick.current = false
        }}>
        <div ref={track} className="game-catalogue-rail__track" role="list">
          {cards.map((card, index) => <div className="game-catalogue-rail__item" role="listitem"
            key={typeof card === 'object' && card !== null && 'key' in card ? card.key : index}>{card}</div>)}
        </div>
      </div>
      <button type="button" className="game-catalogue-rail__arrow game-catalogue-rail__arrow--next"
        aria-label={`Next games in ${label}`} aria-controls={viewportId} disabled={!edges.next} onClick={() => move(1)}>
        <RailArrow direction="next" />
      </button>
    </div>
  )
}

function RailArrow({ direction }: { direction: 'previous' | 'next' }) {
  return <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={direction === 'previous' ? 'm14 6-6 6 6 6' : 'm10 6 6 6-6 6'} /></svg>
}
