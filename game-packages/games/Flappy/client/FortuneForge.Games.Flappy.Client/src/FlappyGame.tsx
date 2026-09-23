import { useCallback, useEffect, useRef, useState } from 'react'
import type { FlappyGameState, FlappyGateway, FlappyStatus } from './contracts'
import { keyboardFlapIntent, pointerFlapIntent } from './flappyControls'

export type FlappyGameProps = Readonly<{ gateway: FlappyGateway; backHref?: string }>

export function FlappyGame({ gateway, backHref = '/' }: FlappyGameProps) {
  const [status, setStatus] = useState<FlappyStatus | null>(null)
  const [game, setGame] = useState<FlappyGameState | null>(null)
  const [error, setError] = useState<string | null>(null)
  const gameRef = useRef<FlappyGameState | null>(null)
  const playfieldRef = useRef<HTMLElement | null>(null)
  const flapQueue = useRef(0)
  const tickInFlight = useRef(false)

  const acceptGame = useCallback((next: FlappyGameState) => {
    gameRef.current = next
    setGame(next)
  }, [])

  const start = useCallback(async () => {
    setError(null)
    flapQueue.current = 0
    try {
      const next = gameRef.current
        ? await gateway.reset(gameRef.current.gameId)
        : await gateway.startGame()
      acceptGame(next)
      window.requestAnimationFrame(() => playfieldRef.current?.focus())
    } catch {
      setError('The local Flappy sample is unavailable.')
    }
  }, [acceptGame, gateway])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(next => {
        if (next.tickMilliseconds !== 20) throw new Error('Flappy requires fixed 20ms ticks.')
        setStatus(next)
        return gateway.startGame(undefined, controller.signal)
      })
      .then(acceptGame)
      .catch(reason => {
        if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError('The local Flappy sample is unavailable.')
      })
    return () => controller.abort()
  }, [acceptGame, gateway])

  useEffect(() => {
    if (game?.phase === 'playing') playfieldRef.current?.focus()
  }, [game?.gameId, game?.phase])

  useEffect(() => {
    if (!status) return
    const timer = window.setInterval(() => {
      const current = gameRef.current
      if (!current || current.phase !== 'playing' || tickInFlight.current) return
      tickInFlight.current = true
      const flap = flapQueue.current > 0
      if (flap) flapQueue.current--
      void gateway.step(current.gameId, flap)
        .then(acceptGame)
        .catch(() => setError('The next Flappy tick could not be completed.'))
        .finally(() => { tickInFlight.current = false })
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [acceptGame, gateway, status])

  const queueFlap = () => { flapQueue.current++ }
  const onKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    const intent = keyboardFlapIntent(event.code, event.repeat, game?.phase === 'playing')
    if (intent.preventDefault) event.preventDefault()
    if (intent.flap) queueFlap()
  }
  const onPlayfieldClick = (event: React.MouseEvent<HTMLElement>) => {
    const intent = pointerFlapIntent(event.button, game?.phase === 'playing')
    if (intent.preventDefault) event.preventDefault()
    if (intent.flap) {
      playfieldRef.current?.focus()
      queueFlap()
    }
  }

  return <div className="ff-flappy-page">
    <header className="ff-flappy-header"><a href={backHref}>✦ Fortune Forge</a><span>Local sample</span></header>
    <main className="ff-flappy-main">
      <div className="ff-flappy-title"><div><small>Classic arcade</small><h1>Flappy</h1><p>Thread the bird through each opening for as long as you can.</p></div><button type="button" onClick={() => void start()}>{game ? 'Restart' : 'Start'}</button></div>
      <p className="ff-flappy-instructions"><strong>Space or click/tap to flap.</strong> Gravity is always pulling the bird down.</p>
      {game && <>
        <div className="ff-flappy-stats" aria-live="polite"><span>Score <strong>{game.score}</strong></span><span>Best <strong>{game.bestScore}</strong></span><span>Level <strong>{game.level}</strong></span></div>
        <section
          aria-label="Flappy playfield"
          className="ff-flappy-playfield"
          onContextMenu={event => event.preventDefault()}
          onKeyDown={onKeyDown}
          onClick={onPlayfieldClick}
          ref={playfieldRef}
          tabIndex={0}
        >
          <svg aria-label="Bird and obstacles" role="img" viewBox={`0 0 ${game.width} ${game.height}`}>
            <defs><linearGradient id="flappy-sky" x2="0" y2="1"><stop stopColor="#153d60"/><stop offset="1" stopColor="#091a2b"/></linearGradient></defs>
            <rect width={game.width} height={game.height} fill="url(#flappy-sky)" />
            {game.obstacles.map(obstacle => <g key={obstacle.id} className="ff-flappy-pipe">
              <rect x={obstacle.x} y={0} width={game.obstacleWidth} height={obstacle.gapTop} />
              <rect x={obstacle.x} y={obstacle.gapBottom} width={game.obstacleWidth} height={game.height - obstacle.gapBottom} />
            </g>)}
            <circle className="ff-flappy-bird" cx={game.birdX} cy={game.birdY} r={game.birdRadius} />
            <circle className="ff-flappy-eye" cx={game.birdX + 5} cy={game.birdY - 3} r={2.5} />
          </svg>
          {game.phase !== 'playing' && <div className="ff-flappy-overlay"><small>Game over</small><strong>Score {game.score}</strong><button type="button" onClick={() => void start()}>Play again</button></div>}
        </section>
      </>}
      {!game && <div className="ff-flappy-loading">{error ?? 'Preparing the course…'}</div>}
      {error && game && <p className="ff-flappy-error" role="alert">{error}</p>}
    </main>
  </div>
}
