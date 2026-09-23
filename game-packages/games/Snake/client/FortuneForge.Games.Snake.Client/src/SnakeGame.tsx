import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import { SnakeGatewayError } from './httpSnakeGateway'
import type { SnakeDirection, SnakeGameState, SnakeGateway } from './contracts'
import { cellClass, directionLabel, pointKey } from './snakeHelpers'

export type SnakeGameProps = Readonly<{ gateway: SnakeGateway }>

const directions: readonly SnakeDirection[] = ['up', 'left', 'down', 'right']

export function SnakeGame({ gateway }: SnakeGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<SnakeGateway['getStatus']>> | null>(null)
  const [game, setGame] = useState<SnakeGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [started, setStarted] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tickInFlight = useRef(false)

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

  const newGame = useCallback(() => {
    void run(() => game ? gateway.reset(game.gameId) : gateway.startGame()).then(nextGame => {
      if (nextGame) setStarted(false)
    })
  }, [game, gateway, run])

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
    void run(() => gateway.turn(game.gameId, direction)).then(nextGame => {
      if (nextGame && begin) setStarted(true)
    })
  }, [game, gateway, run])

  useEffect(() => {
    if (!game || game.phase !== 'playing' || !status || !started) return
    const timer = window.setInterval(() => {
      if (busy || tickInFlight.current) return
      tickInFlight.current = true
      void gateway.tick(game.gameId)
        .then(setGame)
        .catch(reason => setError(messageForError(reason)))
        .finally(() => { tickInFlight.current = false })
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [busy, game, gateway, started, status])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const direction = directionForKey(event.key)
      if (!direction || !game || game.phase !== 'playing' || busy) return
      event.preventDefault()
      turn(direction)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [busy, game, turn])

  if (game === null) return <div className="ff-snake ff-snake--loading" role="status">{error ?? 'Preparing Snake…'}</div>

  const bodyKeys = new Set(game.body.map(pointKey))
  const headKey = pointKey(game.body[0]!)
  const foodKey = game.food ? pointKey(game.food) : ''
  const gameEnded = game.phase !== 'playing'

  return <main className="ff-snake">
    <header className="ff-snake__top">
      <div><p>Arcade game</p><h1>Snake</h1></div>
      <div className="ff-snake__top-actions"><span>Arrow keys or WASD to steer</span><button type="button" onClick={newGame} disabled={busy}>{busy ? 'Setting up…' : 'New run'}</button></div>
    </header>
    <div className="ff-snake__layout">
      <section className="ff-snake-board-wrap" aria-label="Snake board">
        <div className="ff-snake-board-stage">
          <div className="ff-snake-board" style={{ '--board-columns': game.width, '--board-rows': game.height } as CSSProperties}>
            {Array.from({ length: game.width * game.height }, (_, index) => {
              const point = { x: index % game.width, y: Math.floor(index / game.width) }
              const key = pointKey(point)
              const isFood = key === foodKey
              return <div className={`ff-snake-cell ${cellClass(key === headKey, bodyKeys.has(key), isFood)}`} key={key} aria-label={key === headKey ? 'Snake head' : isFood ? 'Fruit' : bodyKeys.has(key) ? 'Snake body' : undefined}>{isFood && <i aria-hidden="true" />}</div>
            })}
          </div>
          {(!started || gameEnded) && <div className="ff-snake-overlay">
            <small>{gameEnded ? (game.phase === 'won' ? 'Board cleared' : 'Run ended') : 'Snake is ready'}</small>
            <strong>{gameEnded ? (game.phase === 'won' ? 'You win!' : 'Snake crashed') : 'Choose a direction'}</strong>
            <span>{gameEnded ? game.message : 'Steer with the arrow keys, WASD, or the controls.'}</span>
            <button type="button" onClick={() => gameEnded ? newGame() : turn(game.direction)} disabled={busy}>{gameEnded ? 'Play again' : 'Start moving'}</button>
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
        <section className="ff-snake-controls" aria-label="Snake direction controls">
          {directions.map(direction => <button type="button" key={direction} onClick={() => turn(direction)} disabled={busy || gameEnded} aria-label={`Turn ${directionLabel(direction)}`}><b aria-hidden="true">{direction === 'up' ? '↑' : direction === 'right' ? '→' : direction === 'down' ? '↓' : '←'}</b><span>{directionLabel(direction)}</span></button>)}
        </section>
        {error && <button className="ff-snake-retry" type="button" onClick={newGame} disabled={busy}>Try again</button>}
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

function messageForError(reason: unknown): string {
  return reason instanceof SnakeGatewayError ? reason.message : 'Snake could not be started. Try again.'
}
