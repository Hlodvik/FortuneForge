import { useCallback, useEffect, useRef, useState } from 'react'
import { AsteroidsGatewayError } from './httpAsteroidsGateway'
import type { AsteroidsAction, AsteroidsGameState, AsteroidsGateway, AsteroidsLeaderboard } from './contracts'
import { renderAsteroids, type AsteroidsImpact, type AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'
import { formatScore } from './asteroidsHelpers'
import { loadAsteroidsSpriteAtlases } from './asteroidsSprites'
import './asteroidsAccessibility.css'

export type AsteroidsGameProps = Readonly<{ gateway: AsteroidsGateway; backHref?: string; playerName?: string; tableLabel?: string }>

const heldControlIntervalMilliseconds = 35

export function AsteroidsGame({ gateway, backHref = '/', playerName = 'Player', tableLabel = 'Arcade free play' }: AsteroidsGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<AsteroidsGateway['getStatus']>> | null>(null)
  const [game, setGame] = useState<AsteroidsGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [spriteAtlases, setSpriteAtlases] = useState<AsteroidsSpriteAtlases>({})
  const [leaderboard, setLeaderboard] = useState<AsteroidsLeaderboard | null>(null)
  const [submittingScore, setSubmittingScore] = useState(false)
  const [paused, setPaused] = useState(false)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const leaderboardRef = useRef<HTMLElement>(null)
  const tickInFlight = useRef(false)
  const actionInFlight = useRef(false)
  const gameRef = useRef<AsteroidsGameState | null>(null)
  const busyRef = useRef(false)
  const pausedRef = useRef(false)
  const heldControlTimerRef = useRef<number | null>(null)
  const heldRotationsRef = useRef(new Map<string, AsteroidsAction>())
  const heldThrustsRef = useRef(new Set<string>())
  const lastHeldControlRef = useRef<'rotate' | 'thrust'>('thrust')
  const previousGameRef = useRef<AsteroidsGameState | null>(null)
  const impactsRef = useRef<readonly AsteroidsImpact[]>([])
  const submittedRunRef = useRef<string | null>(null)

  gameRef.current = game
  busyRef.current = busy
  pausedRef.current = paused

  const run = useCallback(async (action: () => Promise<AsteroidsGameState>) => {
    if (busy) return
    setBusy(true)
    setError(null)
    try { setGame(await action()) } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }, [busy])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal)
      .then(nextStatus => { setStatus(nextStatus); return nextStatus.available ? gateway.startGame(undefined, controller.signal) : Promise.reject(new Error('The Asteroids service is unavailable.')) })
      .then(setGame)
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason)) })
    return () => controller.abort()
  }, [gateway])

  useEffect(() => {
    let active = true
    void loadAsteroidsSpriteAtlases().then(atlases => { if (active) setSpriteAtlases(atlases) })
    return () => { active = false }
  }, [])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas || !game) return
    canvas.width = game.width
    canvas.height = game.height
    const context = canvas.getContext('2d')
    impactsRef.current = nextImpacts(impactsRef.current, previousGameRef.current, game)
    if (context) renderAsteroids(context, game, spriteAtlases, impactsRef.current)
    previousGameRef.current = game
  }, [game, spriteAtlases])

  useEffect(() => {
    if (!game || game.phase !== 'game-over') return
    const runKey = `${game.gameId}:${game.tick}:${game.score}`
    if (submittedRunRef.current === runKey) return
    submittedRunRef.current = runKey
    setSubmittingScore(true)
    setLeaderboard(null)
    void gateway.submitScore(game.gameId, playerName)
      .then(setLeaderboard)
      .catch(reason => setError(messageForError(reason)))
      .finally(() => setSubmittingScore(false))
  }, [game, gateway, playerName])

  useEffect(() => {
    if (game?.phase !== 'game-over' || !leaderboardRef.current) return
    const frame = window.requestAnimationFrame(() => {
      leaderboardRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      leaderboardRef.current?.focus({ preventScroll: true })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [game?.gameId, game?.phase])

  const perform = useCallback((action: AsteroidsAction) => {
    const currentGame = gameRef.current
    if (!currentGame || currentGame.phase !== 'playing' || busyRef.current || pausedRef.current || actionInFlight.current) return false
    actionInFlight.current = true
    setError(null)
    void gateway.action(currentGame.gameId, action)
      .then(setGame)
      .catch(reason => setError(messageForError(reason)))
      .finally(() => { actionInFlight.current = false })
    return true
  }, [gateway])

  useEffect(() => {
    if (!game || game.phase !== 'playing' || !status || paused) return
    const timer = window.setInterval(() => {
      if (busy || tickInFlight.current) return
      tickInFlight.current = true
      void gateway.action(game.gameId, 'tick')
        .then(setGame)
        .catch(reason => setError(messageForError(reason)))
        .finally(() => { tickInFlight.current = false })
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [busy, game, gateway, paused, status])

  useEffect(() => {
    const stopHeldControls = () => {
      if (heldControlTimerRef.current !== null) {
        window.clearInterval(heldControlTimerRef.current)
        heldControlTimerRef.current = null
      }
    }
    const performHeldControl = () => {
      const rotation = Array.from(heldRotationsRef.current.values()).at(-1) ?? null
      const thrusting = heldThrustsRef.current.size > 0
      if (!rotation && !thrusting) return
      const action = rotation && thrusting
        ? lastHeldControlRef.current === 'rotate' ? 'thrust' : rotation
        : rotation ?? 'thrust'
      if (perform(action) && rotation && thrusting) lastHeldControlRef.current = action === 'thrust' ? 'thrust' : 'rotate'
    }
    const refreshHeldControls = () => {
      if (heldRotationsRef.current.size === 0 && heldThrustsRef.current.size === 0) {
        stopHeldControls()
        return
      }
      performHeldControl()
      if (heldControlTimerRef.current === null) {
        heldControlTimerRef.current = window.setInterval(performHeldControl, heldControlIntervalMilliseconds)
      }
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (isInteractiveTarget(event.target)) return
      if (event.key.toLowerCase() === 'p' && gameRef.current?.phase === 'playing') {
        event.preventDefault()
        setPaused(value => !value)
        return
      }
      const action = actionForKey(event)
      if (!action || !gameRef.current || gameRef.current.phase !== 'playing' || busyRef.current) return
      event.preventDefault()
      if (isRotation(action)) {
        if (!heldRotationsRef.current.has(event.code)) {
          heldRotationsRef.current.set(event.code, action)
          refreshHeldControls()
        }
        return
      }
      if (action === 'thrust') {
        if (!heldThrustsRef.current.has(event.code)) {
          heldThrustsRef.current.add(event.code)
          refreshHeldControls()
        }
        return
      }
      perform(action)
    }
    const onKeyUp = (event: KeyboardEvent) => {
      const rotationReleased = heldRotationsRef.current.delete(event.code)
      const thrustReleased = heldThrustsRef.current.delete(event.code)
      if (!rotationReleased && !thrustReleased) return
      event.preventDefault()
      refreshHeldControls()
    }
    const onWindowBlur = () => {
      heldRotationsRef.current.clear()
      heldThrustsRef.current.clear()
      stopHeldControls()
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    window.addEventListener('blur', onWindowBlur)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
      window.removeEventListener('blur', onWindowBlur)
      heldRotationsRef.current.clear()
      heldThrustsRef.current.clear()
      stopHeldControls()
    }
  }, [perform])

  useEffect(() => {
    let frame = 0
    let lastActionAt = 0
    let fireHeld = false
    let pauseHeld = false
    const poll = (now: number) => {
      const pad = navigator.getGamepads?.().find(Boolean)
      if (pad && gameRef.current?.phase === 'playing') {
        const pausePressed = Boolean(pad.buttons[9]?.pressed)
        if (pausePressed && !pauseHeld) setPaused(value => !value)
        pauseHeld = pausePressed
        if (!pausedRef.current) {
          const horizontal = pad.axes[0] ?? 0
          const thrust = (pad.axes[1] ?? 0) < -.35 || pad.buttons[7]?.pressed || pad.buttons[0]?.pressed
          if (now - lastActionAt >= heldControlIntervalMilliseconds) {
            const action = horizontal < -.35 ? 'rotate-left' : horizontal > .35 ? 'rotate-right' : thrust ? 'thrust' : null
            if (action && perform(action)) lastActionAt = now
          }
          const firing = Boolean(pad.buttons[1]?.pressed || pad.buttons[2]?.pressed || pad.buttons[5]?.pressed)
          if (firing && !fireHeld) perform('fire')
          fireHeld = firing
        }
      }
      frame = window.requestAnimationFrame(poll)
    }
    frame = window.requestAnimationFrame(poll)
    return () => window.cancelAnimationFrame(frame)
  }, [perform])

  const newGame = () => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
    submittedRunRef.current = null
    setLeaderboard(null)
    setSubmittingScore(false)
    setPaused(false)
    void run(() => game ? gateway.reset(game.gameId) : gateway.startGame())
  }

  return <div className="ff-asteroids-page">
    <header className="ff-asteroids-header"><a className="ff-asteroids-brand" href={backHref} aria-label="Fortune Forge home"><span aria-hidden="true">✦</span><strong>Fortune Forge</strong></a><a className="ff-asteroids-games" href={backHref}>Other games</a><div className="ff-asteroids-account"><strong>{playerName}</strong><span>{tableLabel}</span></div></header>
    <main className="ff-asteroids-main">
      <section className="ff-asteroids-title">
        <div><small>Arcade free play</small><h1>Asteroids</h1><p>Rotate, thrust, and fire through deterministic waves. Clear the field to advance.</p></div>
        {game?.phase !== 'game-over' && <button className="ff-asteroids-new" type="button" onClick={newGame} disabled={busy}>{busy ? 'Working…' : 'New mission'}</button>}
      </section>
      {game?.phase === 'game-over' ? <section ref={leaderboardRef} className="ff-asteroids-leaderboard" aria-live="polite" tabIndex={-1}>
        <div className="ff-asteroids-leaderboard-head"><div><small>Mission complete</small><h2>Galactic leaderboard</h2></div><strong>{submittingScore ? 'Submitting score…' : `${formatScore(game.score)} points · Wave ${game.wave}`}</strong></div>
        {leaderboard ? <ol>{leaderboard.entries.map(entry => <li key={`${entry.rank}:${entry.playerName}:${entry.score}`} className={entry.playerName === playerName && entry.score === game.score ? 'ff-asteroids-leaderboard-current' : undefined}><span>#{entry.rank}</span><strong>{entry.playerName}</strong><span>{formatScore(entry.score)}</span><small>Wave {entry.wave}</small></li>)}</ol> : <p>{submittingScore ? 'Posting your score to the leaderboard…' : 'The leaderboard is unavailable.'}</p>}
        <button className="ff-asteroids-play-again" type="button" onClick={newGame} disabled={busy}>{busy ? 'Working…' : 'Play again'}</button>
      </section> : game ? <>
        <section className="ff-asteroids-stats" aria-live="polite"><div><small>Score</small><strong>{formatScore(game.score)}</strong></div><div><small>Best</small><strong>{formatScore(game.bestScore)}</strong></div><div><small>Hull</small><strong>{'◆'.repeat(game.lives) || '—'}</strong></div><div><small>Wave / boost</small><strong>{game.wave} · {game.rapidFireTicks > 0 ? 'Rapid' : 'Normal'}</strong></div></section>
        <section className="ff-asteroids-canvas-wrap" aria-label="Asteroids playfield"><canvas ref={canvasRef} className="ff-asteroids-canvas" />{paused && <div className="ff-asteroids-overlay"><small>Mission held</small><strong>Paused</strong><span>Press P, Start, or Resume when you are ready.</span><button type="button" onClick={() => setPaused(false)}>Resume</button></div>}</section>
        <p className="ff-asteroids-message" aria-live="polite">{game.message || (paused ? 'Simulation paused.' : 'Hull diamonds show remaining hits. Rapid fire appears when its power-up is active.')}</p>
        <section className="ff-asteroids-controls" aria-label="Asteroids touch controls">
          <button type="button" disabled={paused} onClick={() => perform('rotate-left')} aria-label="Rotate left">↶<span>Left</span></button>
          <button type="button" disabled={paused} onClick={() => perform('thrust')} aria-label="Thrust">▲<span>Thrust</span></button>
          <button type="button" disabled={paused} onClick={() => perform('fire')} aria-label="Fire">●<span>Fire</span></button>
          <button type="button" disabled={paused} onClick={() => perform('rotate-right')} aria-label="Rotate right">↷<span>Right</span></button>
          <button type="button" onClick={() => setPaused(value => !value)} aria-pressed={paused}>Ⅱ<span>{paused ? 'Resume' : 'Pause'}</span></button>
        </section>
        <p className="ff-asteroids-help">Keyboard: arrows/WASD, Space, P · Controller: left stick, A/trigger, B/RB, Start · Touch controls below.</p>
      </> : <div className="ff-asteroids-loading">{error ?? 'Launching mission…'}</div>}
      {error && <div className="ff-asteroids-error" role="alert"><strong>{error}</strong><button type="button" onClick={newGame} disabled={busy}>Try again</button></div>}
    </main>
  </div>
}

function actionForKey(event: KeyboardEvent): AsteroidsAction | null {
  const key = event.key.toLowerCase()
  return key === 'arrowleft' || key === 'a' ? 'rotate-left' : key === 'arrowright' || key === 'd' ? 'rotate-right' : key === 'arrowup' || key === 'w' || key === 'shift' ? 'thrust' : event.code === 'Space' ? 'fire' : null
}
function isRotation(action: AsteroidsAction): action is 'rotate-left' | 'rotate-right' { return action === 'rotate-left' || action === 'rotate-right' }
function isInteractiveTarget(target: EventTarget | null): boolean { return target instanceof HTMLElement && target.closest('a,button,input,select,textarea,[contenteditable="true"]') !== null }
function messageForError(reason: unknown): string { return reason instanceof AsteroidsGatewayError ? reason.message : 'The Asteroids mission is unavailable. Start a new local mission.' }

function nextImpacts(existing: readonly AsteroidsImpact[], previous: AsteroidsGameState | null, game: AsteroidsGameState): readonly AsteroidsImpact[] {
  if (!previous || previous.gameId !== game.gameId) return []
  const active = existing.filter(impact => game.tick >= impact.startedTick && game.tick - impact.startedTick < (impact.kind === 'hit' ? 7 : 18))
  if (game.tick <= previous.tick) return active
  const remainingIds = new Set(game.asteroids.map(asteroid => asteroid.id))
  const previousById = new Map(previous.asteroids.map(asteroid => [asteroid.id, asteroid]))
  const hitFlashes = game.asteroids
    .filter(asteroid => (previousById.get(asteroid.id)?.hitPoints ?? asteroid.hitPoints) > asteroid.hitPoints)
    .map(asteroid => ({ x: asteroid.x, y: asteroid.y, startedTick: game.tick, kind: 'hit' as const }))
  const destroyedImpacts = previous.asteroids
    .filter(asteroid => !remainingIds.has(asteroid.id))
    .map(asteroid => ({ x: clamp(asteroid.x + asteroid.velocityX, asteroid.radius, game.width - asteroid.radius), y: clamp(asteroid.y + asteroid.velocityY, asteroid.radius, game.height - asteroid.radius), startedTick: game.tick, kind: 'destroyed' as const }))
  return [...active, ...hitFlashes, ...destroyedImpacts]
}

function clamp(value: number, minimum: number, maximum: number): number { return Math.min(maximum, Math.max(minimum, value)) }
