import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type DragEvent as ReactDragEvent,
  type PointerEvent as ReactPointerEvent,
} from 'react'
import { cardLabel } from '../shared/cards'
import {
  canApplyLocalSolitaireCommand,
  firstLegalFoundation,
} from './solitaireEngine'
import type {
  SolitaireCard,
  SolitaireCommand,
  SolitaireGame,
  SolitairePileReference,
} from './solitaireTypes'

type Selection = Readonly<{
  from: SolitairePileReference
  startIndex: number
  cardId: string
  label: string
}>

type DragState = Readonly<{
  source: Selection
  pointerId: number
  startX: number
  startY: number
  x: number
  y: number
  active: boolean
}>

type AutoWinFlight = Readonly<{
  card: Extract<SolitaireCard, { isFaceUp: true }>
  left: number
  top: number
  width: number
  height: number
  fromX: number
  fromY: number
}>

const suitSymbols = { clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const
const rankSymbols = {
  1: 'A', 2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7',
  8: '8', 9: '9', 10: '10', 11: 'J', 12: 'Q', 13: 'K',
} as const

export function SolitaireBoard({
  game,
  busy,
  autoWinning = false,
  onCommand,
}: {
  game: SolitaireGame
  busy: boolean
  autoWinning?: boolean
  onCommand: (command: SolitaireCommand) => void
}) {
  const [selection, setSelection] = useState<Selection | null>(null)
  const [drag, setDrag] = useState<DragState | null>(null)
  const [autoWinFlight, setAutoWinFlight] = useState<AutoWinFlight | null>(null)
  const compactCards = useCompactCardFaces()
  const boardRef = useRef<HTMLElement | null>(null)
  const dragRef = useRef<DragState | null>(null)
  const suppressClickRef = useRef(false)
  const previousGameRef = useRef<SolitaireGame | null>(null)
  const cardRectsRef = useRef<Map<string, DOMRect>>(new Map())
  const flightTimerRef = useRef<number | null>(null)

  useEffect(() => {
    setSelection(null)
    setDrag(null)
    dragRef.current = null
  }, [game])

  useLayoutEffect(() => {
    const board = boardRef.current
    if (board === null) return
    const previousGame = previousGameRef.current
    if (autoWinning && previousGame !== null) {
      const previousFoundationIds = new Set(
        previousGame.foundations.flatMap((pile) => pile.flatMap((card) => card.isFaceUp ? [card.id] : [])),
      )
      const movedCard = game.foundations
        .flatMap((pile) => pile)
        .find((card): card is Extract<SolitaireCard, { isFaceUp: true }> => (
          card.isFaceUp && !previousFoundationIds.has(card.id)
        ))
      if (movedCard !== undefined) {
        const from = cardRectsRef.current.get(movedCard.id)
        const target = [...board.querySelectorAll<HTMLElement>('[data-solitaire-card-id]')]
          .find((element) => element.dataset.solitaireCardId === movedCard.id)
        if (from !== undefined && target !== undefined) {
          const to = target.getBoundingClientRect()
          const bounds = board.getBoundingClientRect()
          if (flightTimerRef.current !== null) window.clearTimeout(flightTimerRef.current)
          setAutoWinFlight({
            card: movedCard,
            left: to.left - bounds.left,
            top: to.top - bounds.top,
            width: to.width,
            height: to.height,
            fromX: from.left - to.left,
            fromY: from.top - to.top,
          })
          flightTimerRef.current = window.setTimeout(() => {
            setAutoWinFlight(null)
            flightTimerRef.current = null
          }, 115)
        }
      }
    }
    const nextRects = new Map<string, DOMRect>()
    for (const element of board.querySelectorAll<HTMLElement>('[data-solitaire-card-id]')) {
      const id = element.dataset.solitaireCardId
      if (id) nextRects.set(id, element.getBoundingClientRect())
    }
    cardRectsRef.current = nextRects
    previousGameRef.current = game
  }, [autoWinning, compactCards, game])

  useEffect(() => () => {
    if (flightTimerRef.current !== null) window.clearTimeout(flightTimerRef.current)
  }, [])

  const choose = (
    from: SolitairePileReference,
    startIndex: number,
    card: SolitaireCard,
  ) => {
    if (busy || !card.isFaceUp) return
    if (selection === null) {
      if (card.rank === 1 && from.zone !== 'foundation') {
        const foundation = firstLegalFoundation(game, from, startIndex)
        if (foundation !== null) {
          onCommand({
            type: 'move',
            from,
            startIndex,
            to: { zone: 'foundation', index: foundation },
          })
          return
        }
      }
      setSelection({ from, startIndex, cardId: card.id, label: cardLabel(card) })
      return
    }
    if (samePile(selection.from, from) && selection.startIndex === startIndex) {
      setSelection(null)
      return
    }
    const command = { type: 'move', from: selection.from, startIndex: selection.startIndex, to: from } as const
    if (canApplyLocalSolitaireCommand(game, command)) {
      onCommand(command)
      return
    }
    setSelection({ from, startIndex, cardId: card.id, label: cardLabel(card) })
  }

  const moveTo = (to: SolitairePileReference) => {
    if (busy || selection === null) return
    const command = { type: 'move', from: selection.from, startIndex: selection.startIndex, to } as const
    if (canApplyLocalSolitaireCommand(game, command)) onCommand(command)
    else setSelection(null)
  }

  const moveHome = (source: Selection) => {
    if (busy) return
    const foundation = firstLegalFoundation(game, source.from, source.startIndex)
    if (foundation === null) return
    setSelection(null)
    onCommand({
      type: 'move',
      from: source.from,
      startIndex: source.startIndex,
      to: { zone: 'foundation', index: foundation },
    })
  }

  const beginDrag = (
    event: ReactPointerEvent<HTMLButtonElement>,
    source: Selection,
  ) => {
    if (busy || event.button !== 0) return
    event.currentTarget.setPointerCapture(event.pointerId)
    const next: DragState = {
      source,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      x: event.clientX,
      y: event.clientY,
      active: false,
    }
    dragRef.current = next
  }

  const updateDrag = (event: ReactPointerEvent<HTMLButtonElement>) => {
    const current = dragRef.current
    if (current === null || current.pointerId !== event.pointerId) return
    const active = current.active
      || Math.hypot(event.clientX - current.startX, event.clientY - current.startY) >= 7
    if (active) event.preventDefault()
    const next: DragState = {
      ...current,
      x: event.clientX,
      y: event.clientY,
      active,
    }
    dragRef.current = next
    if (active) {
      boardRef.current?.style.setProperty('--solitaire-drag-x', `${event.clientX - current.startX}px`)
      boardRef.current?.style.setProperty('--solitaire-drag-y', `${event.clientY - current.startY}px`)
      if (!current.active) setDrag(next)
    }
  }

  const resetDrag = () => {
    boardRef.current?.style.removeProperty('--solitaire-drag-x')
    boardRef.current?.style.removeProperty('--solitaire-drag-y')
    dragRef.current = null
    setDrag(null)
  }

  const clearDragStyle = (element: HTMLButtonElement) => {
    element.style.removeProperty('--solitaire-drag-x')
    element.style.removeProperty('--solitaire-drag-y')
  }

  const commitDrag = (source: Selection, clientX: number, clientY: number) => {
    const target = findDropTarget(boardRef.current, clientX, clientY)
    suppressClickRef.current = true
    window.setTimeout(() => {
      suppressClickRef.current = false
    }, 0)
    if (target !== null && !samePile(source.from, target)) {
      const command = { type: 'move', from: source.from, startIndex: source.startIndex, to: target } as const
      if (canApplyLocalSolitaireCommand(game, command)) onCommand(command)
    }
  }

  const finishDrag = (event: ReactPointerEvent<HTMLButtonElement>) => {
    const current = dragRef.current
    if (current === null || current.pointerId !== event.pointerId) return
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId)
    }
    if (current.active) {
      event.preventDefault()
      commitDrag(current.source, event.clientX, event.clientY)
    }
    clearDragStyle(event.currentTarget)
    resetDrag()
  }

  const cancelDrag = (event: ReactPointerEvent<HTMLButtonElement>) => {
    clearDragStyle(event.currentTarget)
    resetDrag()
  }

  const beginNativeDrag = (event: ReactDragEvent<HTMLButtonElement>, source: Selection) => {
    if (busy) {
      event.preventDefault()
      return
    }
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', source.cardId)
    const next: DragState = {
      source,
      pointerId: -1,
      startX: event.clientX,
      startY: event.clientY,
      x: event.clientX,
      y: event.clientY,
      active: true,
    }
    dragRef.current = next
    setDrag(next)
  }

  const updateNativeDrag = (event: ReactDragEvent<HTMLButtonElement>) => {
    const current = dragRef.current
    if (current === null || current.pointerId !== -1 || (event.clientX === 0 && event.clientY === 0)) return
    const next: DragState = {
      ...current,
      x: event.clientX,
      y: event.clientY,
    }
    dragRef.current = next
  }

  const finishNativeDrag = (event: ReactDragEvent<HTMLButtonElement>) => {
    const current = dragRef.current
    if (current === null || current.pointerId !== -1) return
    const clientX = event.clientX === 0 && event.clientY === 0 ? current.x : event.clientX
    const clientY = event.clientX === 0 && event.clientY === 0 ? current.y : event.clientY
    commitDrag(current.source, clientX, clientY)
    resetDrag()
  }

  const dropNativeDrag = (event: ReactDragEvent<HTMLElement>) => {
    const current = dragRef.current
    if (current === null || current.pointerId !== -1) return
    event.preventDefault()
    commitDrag(current.source, event.clientX, event.clientY)
    resetDrag()
  }

  const activate = (action: () => void) => {
    if (suppressClickRef.current) {
      suppressClickRef.current = false
      return
    }
    action()
  }

  const dragHandlers = (source: Selection) => ({
    dragSource: source,
    dragState: drag,
    onPointerDown: (event: ReactPointerEvent<HTMLButtonElement>) => beginDrag(event, source),
    onPointerMove: updateDrag,
    onPointerUp: finishDrag,
    onPointerCancel: cancelDrag,
    onDragStart: (event: ReactDragEvent<HTMLButtonElement>) => beginNativeDrag(event, source),
    onDrag: updateNativeDrag,
    onDragEnd: finishNativeDrag,
  })

  return (
    <section
      ref={boardRef}
      className={`solitaire-board${autoWinning ? ' is-auto-winning' : ''}`}
      aria-label="Klondike game board"
      onDragOver={(event) => event.preventDefault()}
      onDrop={dropNativeDrag}
    >
      {autoWinning && (
        <div className="solitaire-auto-win" role="status" aria-live="assertive">
          <strong>Deck completed!</strong>
          <span>Nice! Sending every card home.</span>
        </div>
      )}
      {autoWinFlight !== null && (
        <span
          className="solitaire-auto-flight"
          aria-hidden="true"
          style={{
            left: autoWinFlight.left,
            top: autoWinFlight.top,
            width: autoWinFlight.width,
            height: autoWinFlight.height,
            '--solitaire-flight-x': `${autoWinFlight.fromX}px`,
            '--solitaire-flight-y': `${autoWinFlight.fromY}px`,
          } as CSSProperties}
        >
          <SolitaireFaceCard card={autoWinFlight.card} compact={compactCards} />
        </span>
      )}
      <div className="solitaire-board__top-row">
        <div className="solitaire-stock-group">
          <button
            className="solitaire-pile solitaire-stock"
            type="button"
            disabled={busy || (game.stock.length === 0 && game.waste.length === 0)}
            aria-label={game.stock.length > 0
              ? `Draw ${game.drawCount === 1 ? 'one card' : 'three cards'}, ${game.stock.length} remaining`
              : 'Recycle the waste pile'}
            onClick={() => onCommand({ type: 'draw' })}
          >
            {game.stock.length > 0
              ? <SolitaireCardBack />
              : <span className="solitaire-pile__watermark" aria-hidden="true">↻</span>}
          </button>

          <div className="solitaire-pile solitaire-waste" aria-label="Waste pile">
            {game.waste.length === 0 ? (
              <span className="solitaire-pile__label">Waste</span>
            ) : (
              game.waste.slice(-game.drawCount).map((card, visibleIndex, visibleCards) => {
                const isTop = visibleIndex === visibleCards.length - 1
                const source: Selection | null = card.isFaceUp && isTop ? {
                  from: { zone: 'waste', index: 0 },
                  startIndex: game.waste.length - 1,
                  cardId: card.id,
                  label: cardLabel(card),
                } : null
                return (
                  <CardButton
                    {...(source === null ? {} : dragHandlers(source))}
                    card={card}
                    className="solitaire-waste-card"
                    compact={compactCards}
                    selected={source !== null && selection?.cardId === card.id}
                    disabled={busy || source === null}
                    key={card.isFaceUp ? card.id : `waste-${visibleIndex}`}
                    style={{
                      '--solitaire-waste-left': visibleCards.length === 1
                        ? '0%'
                        : `${visibleIndex * 50 / (visibleCards.length - 1)}%`,
                    } as CSSProperties}
                    onClick={() => {
                      if (source !== null) activate(() => choose(source.from, source.startIndex, card))
                    }}
                    onDoubleClick={() => {
                      if (source !== null) moveHome(source)
                    }}
                  />
                )
              })
            )}
          </div>
        </div>

        <div className="solitaire-foundations" aria-label="Foundation piles">
          {game.foundations.map((pile, index) => {
            const top = pile[pile.length - 1]
            const target = { zone: 'foundation', index } as const
            return (
              <div
                className="solitaire-pile"
                data-solitaire-drop-zone={target.zone}
                data-solitaire-drop-index={target.index}
                key={`foundation-${index}`}
              >
                {top ? (
                  (() => {
                    const source: Selection | null = top.isFaceUp ? {
                      from: target,
                      startIndex: pile.length - 1,
                      cardId: top.id,
                      label: cardLabel(top),
                    } : null
                    return (
                      <CardButton
                        {...(source === null ? {} : dragHandlers(source))}
                        card={top}
                        className={autoWinFlight?.card.id === top.id ? 'solitaire-flight-target' : ''}
                        compact={compactCards}
                        key={top.id}
                        selected={source !== null && selection?.cardId === top.id}
                        disabled={busy || !top.isFaceUp}
                        onClick={() => {
                          if (source !== null) activate(() => choose(target, source.startIndex, top))
                        }}
                      />
                    )
                  })()
                ) : (
                  <button
                    className="solitaire-empty-target"
                    type="button"
                    disabled={busy || selection === null}
                    aria-label={`Move selected card to foundation ${index + 1}`}
                    onClick={() => moveTo(target)}
                  >
                    <span aria-hidden="true">A</span>
                  </button>
                )}
              </div>
            )
          })}
        </div>
      </div>

      <div className="solitaire-tableau" aria-label="Tableau">
        {game.tableau.map((pile, column) => {
          const target = { zone: 'tableau', index: column } as const
          let faceDownBefore = 0
          let faceUpBefore = 0
          return (
            <div
              className="solitaire-tableau__pile"
              data-solitaire-drop-zone={target.zone}
              data-solitaire-drop-index={target.index}
              key={column}
            >
              {pile.length === 0 && (
                <button
                  className="solitaire-empty-target solitaire-empty-target--tableau"
                  type="button"
                  disabled={busy || selection === null}
                  aria-label={`Move selected cards to empty tableau column ${column + 1}`}
                  onClick={() => moveTo(target)}
                >
                  <span aria-hidden="true">K</span>
                </button>
              )}
              {pile.map((card, index) => {
                const isTop = index === pile.length - 1
                const downOffset = faceDownBefore
                const upOffset = faceUpBefore
                if (!isTop) {
                  if (card.isFaceUp) faceUpBefore += 1
                  else faceDownBefore += 1
                }
                const source: Selection | null = card.isFaceUp ? {
                  from: target,
                  startIndex: index,
                  cardId: card.id,
                  label: cardLabel(card),
                } : null
                return (
                  <CardButton
                    {...(source === null ? {} : dragHandlers(source))}
                    card={card}
                    compact={compactCards}
                    className="solitaire-tableau-card"
                    disabled={busy || (!card.isFaceUp && !isTop)}
                    key={`${column}-${index}`}
                    selected={card.isFaceUp && selection?.cardId === card.id}
                    style={{
                      '--solitaire-down-before': downOffset,
                      '--solitaire-up-before': upOffset,
                      '--solitaire-stack-top': `${index / Math.max(1, pile.length - 1) * 64}%`,
                      zIndex: index + 1,
                    } as CSSProperties}
                    onClick={() => {
                      activate(() => {
                        if (!card.isFaceUp && isTop) {
                          onCommand({ type: 'flip', column })
                          return
                        }
                        if (card.isFaceUp) choose(target, index, card)
                      })
                    }}
                    onDoubleClick={() => {
                      if (source !== null) moveHome(source)
                    }}
                  />
                )
              })}
            </div>
          )
        })}
      </div>

      <p className="solitaire-board__hint" aria-live="polite">
        {selection === null
          ? 'Drag a face-up card or run to its destination. Tap-to-select also works.'
          : `Selected ${selection.label}. Tap a tableau or foundation pile, or drag the card.`}
      </p>
    </section>
  )
}

function CardButton({
  card,
  compact,
  className = '',
  disabled,
  selected,
  style,
  dragSource,
  dragState,
  onPointerDown,
  onPointerMove,
  onPointerUp,
  onPointerCancel,
  onDragStart,
  onDrag,
  onDragEnd,
  onClick,
  onDoubleClick,
}: {
  card: SolitaireCard
  compact: boolean
  className?: string
  disabled: boolean
  selected: boolean
  style?: CSSProperties
  dragSource?: Selection
  dragState?: DragState | null
  onPointerDown?: (event: ReactPointerEvent<HTMLButtonElement>) => void
  onPointerMove?: (event: ReactPointerEvent<HTMLButtonElement>) => void
  onPointerUp?: (event: ReactPointerEvent<HTMLButtonElement>) => void
  onPointerCancel?: (event: ReactPointerEvent<HTMLButtonElement>) => void
  onDragStart?: (event: ReactDragEvent<HTMLButtonElement>) => void
  onDrag?: (event: ReactDragEvent<HTMLButtonElement>) => void
  onDragEnd?: (event: ReactDragEvent<HTMLButtonElement>) => void
  onClick: () => void
  onDoubleClick?: () => void
}) {
  const dragging = dragState?.active === true && dragSource !== undefined
    && samePile(dragState.source.from, dragSource.from)
    && (dragState.source.from.zone === 'tableau'
      ? dragSource.startIndex >= dragState.source.startIndex
      : dragState.source.cardId === card.id)
  return (
    <button
      className={`solitaire-card-button ${className}${selected ? ' is-selected' : ''}${dragging ? ' is-dragging' : ''}`}
      type="button"
      disabled={disabled}
      draggable={false}
      style={style}
      data-solitaire-drag-source={dragSource === undefined ? undefined : 'true'}
      data-solitaire-card-id={card.isFaceUp ? card.id : undefined}
      aria-pressed={card.isFaceUp ? selected : undefined}
      aria-label={card.isFaceUp ? `Select ${cardLabel(card)}` : 'Flip face-down card'}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerCancel}
      onDragStart={onDragStart}
      onDrag={onDrag}
      onDragEnd={onDragEnd}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
    >
      {card.isFaceUp
        ? <SolitaireFaceCard card={card} compact={compact} />
        : <SolitaireCardBack />}
    </button>
  )
}

function useCompactCardFaces(): boolean {
  const [compact, setCompact] = useState(() => (
    typeof window === 'undefined'
    || typeof window.matchMedia !== 'function'
    || window.matchMedia('(max-width: 560px)').matches
  ))

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const query = window.matchMedia('(max-width: 560px)')
    const update = () => setCompact(query.matches)
    update()
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])

  return compact
}

function SolitaireFaceCard({
  card,
  compact,
}: {
  card: Extract<SolitaireCard, { isFaceUp: true }>
  compact: boolean
}) {
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return (
    <span className={`solitaire-card-face${compact ? ' solitaire-card-face--compact' : ''}${red ? ' solitaire-card-face--red' : ''}`} aria-hidden="true">
      <span className="solitaire-card-face__corner">
        <b>{rankSymbols[card.rank]}</b><i>{suitSymbols[card.suit]}</i>
      </span>
      <i className="solitaire-card-face__pip">{suitSymbols[card.suit]}</i>
      <span className="solitaire-card-face__corner solitaire-card-face__corner--bottom">
        <b>{rankSymbols[card.rank]}</b><i>{suitSymbols[card.suit]}</i>
      </span>
    </span>
  )
}

function SolitaireCardBack() {
  return (
    <span className="solitaire-card-back" aria-hidden="true"><b>FF</b></span>
  )
}

function samePile(left: SolitairePileReference, right: SolitairePileReference): boolean {
  return left.zone === right.zone && left.index === right.index
}

function findDropTarget(
  board: HTMLElement | null,
  clientX: number,
  clientY: number,
): SolitairePileReference | null {
  if (board === null) return null
  const targets = board.querySelectorAll<HTMLElement>('[data-solitaire-drop-zone]')
  for (const target of targets) {
    const bounds = target.getBoundingClientRect()
    if (clientX < bounds.left || clientX > bounds.right
      || clientY < bounds.top || clientY > bounds.bottom) continue
    const zone = target.dataset.solitaireDropZone
    const index = Number(target.dataset.solitaireDropIndex)
    if ((zone === 'foundation' || zone === 'tableau') && Number.isInteger(index)) {
      return { zone, index }
    }
  }
  return null
}
