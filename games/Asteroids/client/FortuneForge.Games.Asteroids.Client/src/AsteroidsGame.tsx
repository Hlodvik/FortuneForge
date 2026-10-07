import { useCallback, useEffect, useRef, useState } from 'react'
import { AsteroidsGatewayError } from './httpAsteroidsGateway'
import type { AsteroidsAction, AsteroidsGameState, AsteroidsGateway } from './contracts'
import { playAsteroidsSound } from './asteroidsAudio'
import { renderAsteroids, type AsteroidsImpact, type AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'
import { formatScore } from './asteroidsHelpers'
import { loadAsteroidsSpriteAtlases } from './asteroidsSprites'
import { AsteroidsTouchControls, type AsteroidsTouchControl } from './AsteroidsTouchControls'

export type AsteroidsGameProps = Readonly<{ gateway: AsteroidsGateway; backHref?: string; playerName?: string; tableLabel?: string }>

const heldControlIntervalMilliseconds = 35

export function AsteroidsGame({ gateway, tableLabel = 'Free Play' }: AsteroidsGameProps) {
  const [status, setStatus] = useState<Awaited<ReturnType<AsteroidsGateway['getStatus']>> | null>(null)
  const [game, setGame] = useState<AsteroidsGameState | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [spriteAtlases, setSpriteAtlases] = useState<AsteroidsSpriteAtlases>({})
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const tickInFlight = useRef(false)
  const actionInFlight = useRef(false)
  const gameRef = useRef<AsteroidsGameState | null>(null)
  const busyRef = useRef(false)
  const heldControlTimerRef = useRef<number | null>(null)
  const refreshHeldControlsRef = useRef<() => void>(() => undefined)
  const heldRotationsRef = useRef(new Map<string, AsteroidsAction>())
  const heldThrustsRef = useRef(new Set<string>())
  const heldFiresRef = useRef(new Set<string>())
  const lastHeldControlRef = useRef<AsteroidsAction>('fire')
  const previousGameRef = useRef<AsteroidsGameState | null>(null)
  const impactsRef = useRef<readonly AsteroidsImpact[]>([])

  gameRef.current = game
  busyRef.current = busy

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
    const previous = previousGameRef.current
    impactsRef.current = nextImpacts(impactsRef.current, previous, game)
    if (context) renderAsteroids(context, game, spriteAtlases, impactsRef.current)
    playFrameSounds(previous, game)
    previousGameRef.current = game
  }, [game, spriteAtlases])

  const perform = useCallback((action: AsteroidsAction) => {
    const currentGame = gameRef.current
    if (!currentGame || currentGame.phase !== 'playing' || busyRef.current || actionInFlight.current) return false
    actionInFlight.current = true
    setError(null)
    void gateway.action(currentGame.gameId, action)
      .then(setGame)
      .catch(reason => setError(messageForError(reason)))
      .finally(() => { actionInFlight.current = false })
    return true
  }, [gateway])

  useEffect(() => {
    if (!game || game.phase !== 'playing' || !status) return
    const timer = window.setInterval(() => {
      if (busy || tickInFlight.current) return
      tickInFlight.current = true
      void gateway.action(game.gameId, 'tick')
        .then(setGame)
        .catch(reason => setError(messageForError(reason)))
        .finally(() => { tickInFlight.current = false })
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [busy, game, gateway, status])

  useEffect(() => {
    const stopHeldControls = () => {
      if (heldControlTimerRef.current !== null) {
        window.clearInterval(heldControlTimerRef.current)
        heldControlTimerRef.current = null
      }
    }
    const performHeldControl = () => {
      const rotation = Array.from(heldRotationsRef.current.values()).at(-1) ?? null
      const actions = [rotation, heldThrustsRef.current.size > 0 ? 'thrust' : null, heldFiresRef.current.size > 0 ? 'fire' : null]
        .filter((action): action is AsteroidsAction => action !== null)
      if (actions.length === 0) return
      const priorIndex = actions.indexOf(lastHeldControlRef.current)
      const action = actions[(priorIndex + 1) % actions.length]!
      if (perform(action)) lastHeldControlRef.current = action
    }
    const refreshHeldControls = () => {
      if (heldRotationsRef.current.size === 0 && heldThrustsRef.current.size === 0 && heldFiresRef.current.size === 0) {
        stopHeldControls()
        return
      }
      performHeldControl()
      if (heldControlTimerRef.current === null) {
        heldControlTimerRef.current = window.setInterval(performHeldControl, heldControlIntervalMilliseconds)
      }
    }
    refreshHeldControlsRef.current = refreshHeldControls
    const onKeyDown = (event: KeyboardEvent) => {
      if (isInteractiveTarget(event.target)) return
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
      if (!heldFiresRef.current.has(event.code)) {
        heldFiresRef.current.add(event.code)
        refreshHeldControls()
      }
    }
    const onKeyUp = (event: KeyboardEvent) => {
      const rotationReleased = heldRotationsRef.current.delete(event.code)
      const thrustReleased = heldThrustsRef.current.delete(event.code)
      const fireReleased = heldFiresRef.current.delete(event.code)
      if (!rotationReleased && !thrustReleased && !fireReleased) return
      event.preventDefault()
      refreshHeldControls()
    }
    const onWindowBlur = () => {
      heldRotationsRef.current.clear()
      heldThrustsRef.current.clear()
      heldFiresRef.current.clear()
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
      heldFiresRef.current.clear()
      stopHeldControls()
      refreshHeldControlsRef.current = () => undefined
    }
  }, [perform])

  const setTouchControl = useCallback((control: AsteroidsTouchControl, pressed: boolean) => {
    const controlKey = control === 'fire' ? 'touch-fire' : 'touch-joystick'
    if (control === 'left' || control === 'right') {
      if (pressed) heldRotationsRef.current.set(controlKey, control === 'left' ? 'rotate-left' : 'rotate-right')
      else heldRotationsRef.current.delete(controlKey)
    } else if (control === 'thrust') {
      if (pressed) heldThrustsRef.current.add(controlKey)
      else heldThrustsRef.current.delete(controlKey)
    } else if (pressed) heldFiresRef.current.add(controlKey)
    else heldFiresRef.current.delete(controlKey)
    refreshHeldControlsRef.current()
  }, [])

  const newGame = () => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
    void run(() => game ? gateway.reset(game.gameId) : gateway.startGame())
  }

  return <div className="ff-asteroids-page">
    <main className="ff-asteroids-main">
      {game ? <>
        <section className="ff-asteroids-stage" aria-label="Asteroid Blaster playfield">
          <canvas ref={canvasRef} className="ff-asteroids-canvas" />
          <section className="ff-asteroids-title">
            <div><small>{tableLabel}</small><h1>Asteroid Blaster</h1></div>
            <button className="ff-asteroids-new" type="button" aria-label="New mission" title="New mission" onClick={newGame} disabled={busy}>{busy ? '…' : '↻'}</button>
          </section>
          <section className="ff-asteroids-stats" aria-live="polite"><div className="ff-asteroids-score"><small>Score</small><strong>{formatScore(game.score)}</strong></div><div className="ff-asteroids-best"><small>Best</small><strong>{formatScore(game.bestScore)}</strong></div><div className="ff-asteroids-lives"><small>Lives</small><strong>{'◆'.repeat(game.lives) || '—'}</strong></div><div className="ff-asteroids-wave"><small>Wave</small><strong>{game.wave}</strong></div></section>
          {game.phase === 'game-over' && <div className="ff-asteroids-overlay" role="status"><small>Mission ended</small><strong>{formatScore(game.score)}</strong><button type="button" onClick={newGame} disabled={busy}>Play again</button></div>}
          <AsteroidsTouchControls disabled={game.phase !== 'playing'} shipAngle={game.ship.angle} onControlChange={setTouchControl} />
        </section>
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

function playFrameSounds(previous: AsteroidsGameState | null, game: AsteroidsGameState): void {
  if (previous === null || previous.gameId !== game.gameId || game.tick <= previous.tick) return
  const previousBullets = new Set(previous.bullets.map(bullet => bullet.id))
  const currentAsteroids = new Set(game.asteroids.map(asteroid => asteroid.id))
  if (game.bullets.some(bullet => !previousBullets.has(bullet.id))) playAsteroidsSound('laser')
  if (previous.asteroids.some(asteroid => !currentAsteroids.has(asteroid.id))) playAsteroidsSound('explosion')
  if (game.ship.thrustTicks > 0) playAsteroidsSound('thrust')
}
