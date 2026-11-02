import { useCallback, useEffect, useRef, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react'
import type { SnakeDirection, SnakeGateway } from './contracts'
import { cellClass, directionLabel, pointKey } from './snakeHelpers'
import { snakeSpeeds } from './snakeRunController'
import { useSnakeRun } from './useSnakeRun'

export type SnakeGameProps = Readonly<{ gateway: SnakeGateway }>

const directions: readonly SnakeDirection[] = ['up', 'left', 'down', 'right']
const speedOptions = snakeSpeeds

export function SnakeGame({ gateway }: SnakeGameProps) {
  const { run, controller } = useSnakeRun(gateway)
  const { game, busy, error, speed, boardSize } = run
  const started = run.phase !== 'ready' && run.phase !== 'loading'
  const paused = run.phase === 'paused'
  const movementAvailable = game?.phase === 'playing'
  const cabinet = useRef<HTMLElement>(null)
  const board = useRef<HTMLElement>(null)
  const swipeStart = useRef<{ x: number; y: number; pointerId: number } | null>(null)
  const newGame = useCallback((nextSize = boardSize) => controller.newGame(false, speed, nextSize), [boardSize, controller, speed])
  const turn = useCallback((direction: SnakeDirection) => { controller.turn(direction); board.current?.focus() }, [controller])
  const play = () => { controller.play(); board.current?.focus() }
  const resume = () => { controller.resume(); board.current?.focus() }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.repeat || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey ||
        !(event.target instanceof Node) || !cabinet.current?.contains(event.target) || isControlTarget(event.target)) return
      if ((event.key.toLowerCase() === 'p' || event.key === 'Escape') && started && game?.phase === 'playing') {
        event.preventDefault(); controller.togglePause(); return
      }
      const direction = directionForKey(event.key)
      if (!direction || !game || game.phase !== 'playing' || busy) return
      event.preventDefault(); turn(direction)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [busy, controller, game, started, turn])

  useEffect(() => {
    if (!movementAvailable) return
    let frame = 0, previousDirection: SnakeDirection | null = null, previousPause = false
    const poll = () => {
      const pad = navigator.getGamepads?.()[0]
      const direction = pad ? gamepadDirection(pad) : null
      const pausePressed = Boolean(pad?.buttons[9]?.pressed)
      const focused = document.hasFocus() && !document.hidden && cabinet.current?.contains(document.activeElement) && !isControlTarget(document.activeElement)
      if (focused) {
        if (pausePressed && !previousPause && started) controller.togglePause()
        if (direction && direction !== previousDirection && !pausePressed) turn(direction)
      }
      previousDirection = direction; previousPause = pausePressed
      frame = window.requestAnimationFrame(poll)
    }
    frame = window.requestAnimationFrame(poll)
    return () => window.cancelAnimationFrame(frame)
  }, [controller, movementAvailable, started, turn])

  const beginSwipe = (event: ReactPointerEvent<HTMLElement>) => { if (!event.isPrimary || event.button !== 0 || busy) return; board.current?.focus(); swipeStart.current = { x: event.clientX, y: event.clientY, pointerId: event.pointerId }; event.currentTarget.setPointerCapture?.(event.pointerId) }
  const finishSwipe = (event: ReactPointerEvent<HTMLElement>) => {
    const start = swipeStart.current
    swipeStart.current = null
    if (!start || start.pointerId !== event.pointerId) return
    const x = event.clientX - start.x
    const y = event.clientY - start.y
    if (Math.max(Math.abs(x), Math.abs(y)) < 24) return
    turn(Math.abs(x) > Math.abs(y) ? (x > 0 ? 'right' : 'left') : (y > 0 ? 'down' : 'up'))
  }

  if (game === null) return <div className="ff-snake ff-snake--loading" role={error ? 'alert' : 'status'}>{error ?? 'Preparing Snake…'}{error && <button type="button" onClick={() => controller.retry()} disabled={busy}>Try again</button>}</div>

  const bodyKeys = new Set(game.body.map(pointKey))
  const headKey = pointKey(game.body[0]!)
  const foodKey = game.food ? pointKey(game.food) : ''
  const gameEnded = game.phase !== 'playing' || run.phase === 'error'

  return <main className="ff-snake" ref={cabinet} data-phase={run.phase} data-moves={game.moves}>
    <header className="ff-snake__top">
      <div><p>Arcade game</p><h1>Snake</h1></div>
      <div className="ff-snake__top-actions"><span>Swipe, controller, arrows, or WASD</span>{started && !gameEnded && <button type="button" className="ff-snake-pause" onClick={() => controller.togglePause()}>{paused ? 'Resume' : 'Pause'}</button>}<button type="button" onClick={() => newGame()} disabled={busy}>{busy ? 'Setting up…' : 'New run'}</button></div>
    </header>
    <div className="ff-snake__layout">
      <section className="ff-snake-board-wrap" ref={board} tabIndex={0} aria-label="Snake board. Swipe to steer." onPointerDown={beginSwipe} onPointerUp={finishSwipe} onPointerCancel={() => { swipeStart.current = null }}>
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
            <small>{paused ? 'Run paused' : run.phase === 'error' ? 'Run interrupted' : gameEnded ? (game.phase === 'won' ? 'Board cleared' : 'Run ended') : 'Snake is ready'}</small>
            <strong>{paused ? 'Take a breath' : run.phase === 'error' ? 'Start a new run' : gameEnded ? (game.phase === 'won' ? 'You win!' : 'Snake crashed') : 'Play'}</strong>
            <span>{paused ? 'Press P, Start, or Resume when you are ready.' : gameEnded ? game.message : 'Swipe, use a controller, arrow keys, WASD, or the controls.'}</span>
            <button type="button" onClick={() => paused ? resume() : gameEnded ? newGame() : play()} disabled={busy}>{paused ? 'Resume' : gameEnded ? 'Play again' : 'Play'}</button>
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
          <label>Speed<select value={speed} onChange={event => controller.configure(event.target.value as keyof typeof speedOptions, boardSize)}><option value="relaxed">Relaxed</option><option value="classic">Classic</option><option value="turbo">Turbo</option></select></label>
          <label>Board<select value={boardSize} onChange={event => { const size = Number(event.target.value); newGame(size) }} disabled={busy}><option value="16">16 × 16</option><option value="20">20 × 20</option><option value="24">24 × 24</option></select></label>
          <span><i aria-hidden="true" /> Fruit appears only in an open cell</span>
        </section>
        <section className="ff-snake-controls" aria-label="Snake direction controls">
          {directions.map(direction => <button type="button" key={direction} onClick={() => turn(direction)} disabled={busy || gameEnded || paused} aria-label={`Turn ${directionLabel(direction)}`}><b aria-hidden="true">{direction === 'up' ? '↑' : direction === 'right' ? '→' : direction === 'down' ? '↓' : '←'}</b><span>{directionLabel(direction)}</span></button>)}
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

function isControlTarget(target: EventTarget | null): boolean {
  return target instanceof Element && Boolean(target.closest('input, select, textarea, button, a, [contenteditable="true"], [role="dialog"]'))
}
