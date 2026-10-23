import { useEffect, useId, useRef, useState, type RefObject } from 'react'
import type { HeartsCard } from './contracts'
import { cardName } from './heartsHelpers'
import type { getTrialCoach } from './heartsTrialCoach'

type Coach = ReturnType<typeof getTrialCoach>
type Point = Readonly<{ x: number; y: number }>
type Bounds = Readonly<{ left: number; top: number; right: number; bottom: number; width: number; height: number }>
type CoachArrow = Readonly<{ path: string; start: Point; end: Point }>

/** Use an actually exposed piece of a card, including cards raised above their neighbours. */
function exposedCardBounds(target: HTMLButtonElement, cards: HTMLButtonElement[], board: Bounds): Bounds | null {
  const targetBounds = target.getBoundingClientRect()
  const hand = target.closest('.ff-hearts-hand')?.getBoundingClientRect() ?? board
  const left = Math.max(targetBounds.left, hand.left, board.left)
  const right = Math.min(targetBounds.right, hand.right, board.right)
  const top = Math.max(targetBounds.top, hand.top, board.top)
  const bottom = Math.min(targetBounds.bottom, hand.bottom, board.bottom)
  if (right <= left || bottom <= top) return null
  const anchorY = top + Math.min(3, (bottom - top) / 2)
  const index = cards.indexOf(target)
  const layer = Number.parseInt(getComputedStyle(target).zIndex, 10) || 0
  let intervals = [{ left, right }]
  cards.forEach((card, otherIndex) => {
    if (card === target) return
    const otherLayer = Number.parseInt(getComputedStyle(card).zIndex, 10) || 0
    if (otherLayer < layer || otherLayer === layer && otherIndex < index) return
    const bounds = card.getBoundingClientRect()
    if (bounds.top > anchorY || bounds.bottom <= anchorY) return
    intervals = intervals.flatMap(interval => bounds.left >= interval.right || bounds.right <= interval.left
      ? [interval]
      : [
        ...(bounds.left > interval.left ? [{ left: interval.left, right: bounds.left }] : []),
        ...(bounds.right < interval.right ? [{ left: bounds.right, right: interval.right }] : []),
      ])
  })
  const exposed = intervals.sort((a, b) => (b.right - b.left) - (a.right - a.left))[0]
  return exposed ? { ...exposed, top, bottom, width: exposed.right - exposed.left, height: bottom - top } : null
}

/** The pointer terminates on the exposed part of the actual card, not its hidden center. */
export function trialCoachArrow(board: Bounds, bubble: Bounds, target: Bounds, exposedWidth = target.width): CoachArrow {
  const visible = Math.max(0, Math.min(target.width, exposedWidth))
  const end = { x: target.left - board.left + visible / 2, y: target.top - board.top + Math.min(3, target.height / 2) }
  const below = target.top >= bubble.bottom
  const start = below
    ? { x: Math.max(bubble.left + 13, Math.min(bubble.right - 13, target.left + visible / 2)) - board.left, y: bubble.bottom - board.top - 1 }
    : { x: target.left < bubble.left ? bubble.left - board.left : bubble.right - board.left, y: bubble.top - board.top + bubble.height / 2 }
  const bend = Math.max(16, Math.min(65, Math.abs(end.y - start.y) * .45))
  const path = `M ${start.x} ${start.y} C ${start.x} ${start.y + bend}, ${end.x} ${end.y - bend}, ${end.x} ${end.y}`
  return { path, start, end }
}

export function HeartsTrialCoachBubble({ coach, boardRef, cardRefs, passRef, playRef, mobileHand = false, selectedPlayCard, layoutKey, onExplain }: {
  coach: Coach
  boardRef: RefObject<HTMLElement | null>
  cardRefs: RefObject<Map<string, HTMLButtonElement>>
  passRef: RefObject<HTMLButtonElement | null>
  playRef?: RefObject<HTMLButtonElement | null>
  mobileHand?: boolean
  selectedPlayCard?: HeartsCard
  layoutKey: string
  onExplain?: () => void
}) {
  const bubbleRef = useRef<HTMLDivElement>(null)
  const marker = useId().replace(/:/g, '')
  const [geometry, setGeometry] = useState<{ width: number; height: number; arrows: CoachArrow[] }>({ width: 0, height: 0, arrows: [] })
  const codesKey = coach.suggestedCodes.join(',')
  const mobilePlay = coach.kind === 'play' && mobileHand
  const confirmPlay = mobilePlay && !!selectedPlayCard
  const title = confirmPlay ? 'Ready to play?' : coach.title
  const message = mobilePlay
    ? selectedPlayCard
      ? `Tap Play to play your ${cardName(selectedPlayCard)}. You can tap another legal card to change your choice.`
      : 'Tap a marked card to select it, then tap Play. You can choose a different legal card before confirming.'
    : coach.message
  useEffect(() => {
    const board = boardRef.current, bubble = bubbleRef.current
    if (!board || !bubble) return
    const targets = confirmPlay
      ? playRef?.current ? [playRef.current] : []
      : coach.kind === 'confirm-pass' ? passRef.current ? [passRef.current] : [] : coach.suggestedCodes.map(code => cardRefs.current.get(code)).filter((card): card is HTMLButtonElement => !!card)
    const measure = () => {
      const boardBounds = board.getBoundingClientRect(), bubbleBounds = bubble.getBoundingClientRect()
      if (!boardBounds.width || !boardBounds.height) return
      const cards = [...cardRefs.current.values()]
      const arrows = targets.flatMap(target => {
        const bounds = cards.includes(target) ? exposedCardBounds(target, cards, boardBounds) : target.getBoundingClientRect()
        return bounds && bounds.width > 0 && bounds.height > 0 ? [trialCoachArrow(boardBounds, bubbleBounds, bounds)] : []
      })
      setGeometry({ width: boardBounds.width, height: boardBounds.height, arrows })
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(board); observer?.observe(bubble); targets.forEach(target => observer?.observe(target))
    window.addEventListener('resize', measure)
    board.addEventListener('scroll', measure, true)
    board.addEventListener('transitionend', measure)
    return () => { observer?.disconnect(); window.removeEventListener('resize', measure); board.removeEventListener('scroll', measure, true); board.removeEventListener('transitionend', measure) }
  }, [coach.kind, codesKey, title, message, confirmPlay, boardRef, cardRefs, passRef, playRef, layoutKey])

  if (coach.kind === 'complete') return null
  return <>
    <div className={`ff-hearts-trial-coach kind-${coach.kind}`} id="hearts-trial-coach" role="status" aria-live="polite" ref={bubbleRef}>
      <strong>{title}</strong><span>{message}</span>
      {coach.why && <details className="ff-hearts-trial-coach__why" key={coach.kind}><summary>Why?</summary><div><p>{coach.why}</p>{onExplain && <button type="button" onClick={onExplain}>All rules & tips</button>}</div></details>}
    </div>
    {geometry.arrows.length > 0 && <svg className="ff-hearts-coach-arrows" aria-hidden="true" viewBox={`0 0 ${geometry.width} ${geometry.height}`} preserveAspectRatio="none"><defs><marker id={marker} markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path d="M 0 0 L 7 3.5 L 0 7" /></marker></defs>{geometry.arrows.map((arrow, index) => <path key={index} d={arrow.path} markerEnd={`url(#${marker})`} data-target-x={arrow.end.x} data-target-y={arrow.end.y} />)}</svg>}
  </>
}
