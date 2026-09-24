import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react'
import { SnakeGatewayError } from './httpSnakeGateway'
import type { SnakeDirection, SnakeGameState, SnakeGateway } from './contracts'
import { cellClass, directionLabel, pointKey } from './snakeHelpers'

export type SnakeGameProps = Readonly<{ gateway: SnakeGateway }>

const directions: readonly SnakeDirection[] = ['up', 'left', 'down', 'right']
const speedOptions = { relaxed: 170, classic: 115, turbo: 78 } as const

export function SnakeGame({ gateway }: SnakeGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<SnakeGateway['getStatus']>> | null>(null)
  const [game, setGame] = useState<SnakeGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [started, setStarted] = useState(false)
  const [paused, setPaused] = useState(false)
  const [speed, setSpeed] = useState<keyof typeof speedOptions>('classic')
  const [boardSize, setBoardSize] = useState(20)
  const [error, setError] = useState<string | null>(null)
  const tickInFlight = useRef(false)
  const turnInFlight = useRef(false)
  const queuedTurn = useRef<SnakeDirection | null>(null)
  const swipeStart = useRef<{ x: number; y: number; pointerId: number } | null>(null)

  const run = useCallback(async (action: () => Promise<SnakeGameState>) => {
    if (busy) return null
    setBusy(true)
    setError(null)
    try {
      const nextGame = await action()
      setGame(nextGame)
      return nextGame
    } catch (reason) {
      setError(messageForError(reason))
      return null
    } finally {
      setBusy(false)
    }
  }, [busy])

  const newGame = useCallback((nextSize = boardSize) => {
    void run(() => game ? gateway.reset(game.gameId, undefined, undefined, { width: nextSize, height: nextSize }) : gateway.startGame(undefined, undefined, { width: nextSize, height: nextSize })).then(nextGame => {
      if (nextGame) { setStarted(false); setPaused(false) }
    })
  }, [boardSize, game, gateway, run])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(nextStatus => {
        setStatus(nextStatus)
        if (!nextStatus.available) throw new Error('Snake is unavailable right now.')
        return gateway.startGame(undefined, controller.signal)
      })
      .then(nextGame => { setGame(nextGame); setStarted(false) })
      .catch((reason: unknown) => {
        if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason))
      })
    return () => controller.abort()
  }, [gateway])

  const turn = useCallback((direction: SnakeDirection, begin = true) => {
    if (!game || game.phase !== 'playing') return
    if (turnInFlight.current) { queuedTurn.current = direction; return }
    void (async () => {
      turnInFlight.current = true
      setError(null)
      let current = game
      let requested: SnakeDirection | null = direction
      try {
        while (requested && current.phase === 'playing') {
          current = await gateway.turn(current.gameId, requested)
          setGame(current)
          if (begin) { setStarted(true); setPaused(false) }
          requested = queuedTurn.current
          queuedTurn.current = null
        }
      } catch (reason) { setError(messageForError(reason)) }
      finally { turnInFlight.current = false }
    })()
  }, [game, gateway])

  useEffect(() => {
    if (!game || game.phase !== 'playing' || !status || !started || paused) return
    const timer = window.setInterval(() => {
      if (busy || tickInFlight.current) return
      tickInFlight.current = true
      void gateway.tick(game.gameId)
        .then(setGame)
        .catch(reason => setError(messageForError(reason)))
        .finally(() => { tickInFlight.current = false })
    }, speedOptions[speed])
    return () => window.clearInterval(timer)
  }, [busy, game, gateway, paused, speed, started, status])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.key.toLowerCase() === 'p' || event.key === 'Escape') && started && game?.phase === 'playing') {
        event.preventDefault()
        setPaused(value => !value)
        return
      }
      const direction = directionForKey(event.key)
      if (!direction || !game || game.phase !== 'playing' || busy) return
      event.preventDefault()
      turn(direction)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [busy, game, started, turn])

  useEffect(() => {
    if (!game || game.phase !== 'playing') return
    let frame = 0
    let previousInput = ''
    const poll = () => {
      const pad = navigator.getGamepads?.()[0]
      const direction = pad ? gamepadDirection(pad) : null
      const pausePressed = Boolean(pad?.buttons[9]?.pressed)
      const input = `${direction ?? ''}:${pausePressed}`
      if (input !== previousInput) {
        if (direction) turn(direction)
        if (pausePressed && started) setPaused(value => !value)
        previousInput = input
      }
      frame = window.requestAnimationFrame(poll)
    }
    frame = window.requestAnimationFrame(poll)
    return () => window.cancelAnimationFrame(frame)
  }, [game, started, turn])

  const beginSwipe = (event: ReactPointerEvent<HTMLElement>) => { swipeStart.current = { x: event.clientX, y: event.clientY, pointerId: event.pointerId }; event.currentTarget.setPointerCapture?.(event.pointerId) }
  const finishSwipe = (event: ReactPointerEvent<HTMLElement>) => {
    const start = swipeStart.current
    swipeStart.current = null
    if (!start || start.pointerId !== event.pointerId) return
    const x = event.clientX - start.x
    const y = event.clientY - start.y
    if (Math.max(Math.abs(x), Math.abs(y)) < 24) return
    turn(Math.abs(x) > Math.abs(y) ? (x > 0 ? 'right' : 'left') : (y > 0 ? 'down' : 'up'))
  }

  if (game === null) return <div className="ff-snake ff-snake--loading" role="status">{error ?? 'Preparing Snake…'}</div>

  const bodyKeys = new Set(game.body.map(pointKey))
  const headKey = pointKey(game.body[0]!)
  const foodKey = game.food ? pointKey(game.food) : ''
  const gameEnded = game.phase !== 'playing'

  return <main className="ff-snake">
    <header className="ff-snake__top">
      <div><p>Arcade game</p><h1>Snake</h1></div>
      <div className="ff-snake__top-actions"><span>Swipe, controller, arrows, or WASD</span>{started && !gameEnded && <button type="button" className="ff-snake-pause" onClick={() => setPaused(value => !value)}>{paused ? 'Resume' : 'Pause'}</button>}<button type="button" onClick={() => newGame()} disabled={busy}>{busy ? 'Setting up…' : 'New run'}</button></div>
    </header>
    <div className="ff-snake__layout">
      <section className="ff-snake-board-wrap" aria-label="Snake board. Swipe to steer." onPointerDown={beginSwipe} onPointerUp={finishSwipe} onPointerCancel={() => { swipeStart.current = null }}>
        <div className="ff-snake-board-stage">
          <div className="ff-snake-board" style={{ '--board-columns': game.width, '--board-rows': game.height } as CSSProperties}>
            {Array.from({ length: game.width * game.height }, (_, index) => {
              const point = { x: index % game.width, y: Math.floor(index / game.width) }
              const key = pointKey(point)
              const isFood = key === foodKey
              return <div className={`ff-snake-cell ${cellClass(key === headKey, bodyKeys.has(key), isFood)}`} key={key} aria-label={key === headKey ? 'Snake head' : isFood ? 'Fruit' : bodyKeys.has(key) ? 'Snake body' : undefined}>{isFood && <i aria-hidden="true" />}</div>
            })}
          </div>
          {(!started || gameEnded || paused) && <div className="ff-snake-overlay">
            <small>{paused ? 'Run paused' : gameEnded ? (game.phase === 'won' ? 'Board cleared' : 'Run ended') : 'Snake is ready'}</small>
            <strong>{paused ? 'Take a breath' : gameEnded ? (game.phase === 'won' ? 'You win!' : 'Snake crashed') : 'Play'}</strong>
            <span>{paused ? 'Press P, Start, or Resume when you are ready.' : gameEnded ? game.message : 'Swipe, use a controller, arrow keys, WASD, or the controls.'}</span>
            <button type="button" onClick={() => paused ? setPaused(false) : gameEnded ? newGame() : turn(game.direction)} disabled={busy}>{paused ? 'Resume' : gameEnded ? 'Play again' : 'Play'}</button>
          </div>}
        </div>
      </section>
      <aside className="ff-snake__panel">
        <section className="ff-snake-stats" aria-label="Run statistics" aria-live="polite">
          <div><small>Score</small><strong>{game.score}</strong></div>
          <div><small>Best</small><strong>{game.bestScore}</strong></div>
          <div><small>Length</small><strong>{game.length}</strong></div>
          <div><small>Heading</small><strong>{directionLabel(game.direction)}</strong></div>
        </section>
        <p className="ff-snake-message" aria-live="polite">{error ?? game.message}</p>
        <section className="ff-snake-options" aria-label="Run options">
          <label>Speed<select value={speed} onChange={event => setSpeed(event.target.value as keyof typeof speedOptions)}><option value="relaxed">Relaxed</option><option value="classic">Classic</option><option value="turbo">Turbo</option></select></label>
          <label>Board<select value={boardSize} onChange={event => { const size = Number(event.target.value); setBoardSize(size); newGame(size) }} disabled={busy}><option value="16">16 × 16</option><option value="20">20 × 20</option><option value="24">24 × 24</option></select></label>
          <span><i aria-hidden="true" /> Fruit appears only in an open cell</span>
        </section>
        <section className="ff-snake-controls" aria-label="Snake direction controls">
          {directions.map(direction => <button type="button" key={direction} onClick={() => turn(direction)} disabled={busy || gameEnded} aria-label={`Turn ${directionLabel(direction)}`}><b aria-hidden="true">{direction === 'up' ? '↑' : direction === 'right' ? '→' : direction === 'down' ? '↓' : '←'}</b><span>{directionLabel(direction)}</span></button>)}
        </section>
        {error && <button className="ff-snake-retry" type="button" onClick={() => newGame()} disabled={busy}>Try again</button>}
      </aside>
    </div>
  </main>
}

function directionForKey(key: string): SnakeDirection | null {
  const normalized = key.toLowerCase()
  return key === 'ArrowUp' || normalized === 'w' ? 'up'
    : key === 'ArrowRight' || normalized === 'd' ? 'right'
      : key === 'ArrowDown' || normalized === 's' ? 'down'
        : key === 'ArrowLeft' || normalized === 'a' ? 'left' : null
}

function gamepadDirection(pad: Gamepad): SnakeDirection | null {
  if (pad.buttons[12]?.pressed || pad.axes[1] < -.55) return 'up'
  if (pad.buttons[15]?.pressed || pad.axes[0] > .55) return 'right'
  if (pad.buttons[13]?.pressed || pad.axes[1] > .55) return 'down'
  if (pad.buttons[14]?.pressed || pad.axes[0] < -.55) return 'left'
  return null
}

function messageForError(reason: unknown): string {
  return reason instanceof SnakeGatewayError ? reason.message : 'Snake could not be started. Try again.'
}
