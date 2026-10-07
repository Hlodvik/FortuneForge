import { useCallback, useEffect, useRef, useState } from 'react'
import { AsteroidsGatewayError } from './httpAsteroidsGateway'
import type { AsteroidsAction, AsteroidsGameState, AsteroidsGateway } from './contracts'
import { setAsteroidsThrusting, stopAsteroidsAudioScene, unlockAsteroidsAudio } from './asteroidsAudio'
import { renderAsteroids, type AsteroidsImpact, type AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'
import { playAsteroidsFrameAudio } from './asteroidsFrameAudio'
import { formatScore } from './asteroidsHelpers'
import { loadAsteroidsSpriteAtlases } from './asteroidsSprites'
import { AsteroidsSoundButton } from './AsteroidsSoundButton'
import { wasAlienDestroyed } from './asteroidsAlienLifecycle'
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
  const pendingControlRequestRef = useRef<Promise<void> | null>(null)
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
  const requestSequenceRef = useRef(0)
  const appliedSequenceRef = useRef(0)

  gameRef.current = game
  busyRef.current = busy

  const applyGameSnapshot = useCallback((next: AsteroidsGameState, sequence: number) => {
    setGame(current => {
      if (sequence < appliedSequenceRef.current) return current
      if (current !== null && current.gameId === next.gameId) {
        const isReset = next.tick === 0 && next.lastEvent === 'started'
        if (next.tick < current.tick && !isReset) return current
      }
      appliedSequenceRef.current = sequence
      return next
    })
  }, [])

  const run = useCallback(async (action: () => Promise<AsteroidsGameState>) => {
    if (busyRef.current) return
    busyRef.current = true
    setBusy(true)
    setError(null)
    try {
      await pendingControlRequestRef.current
      const sequence = ++requestSequenceRef.current
      applyGameSnapshot(await action(), sequence)
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }, [applyGameSnapshot])

  useEffect(() => {
    const controller = new AbortController()
    const sequence = ++requestSequenceRef.current
    void gateway.getStatus(controller.signal)
      .then(nextStatus => { setStatus(nextStatus); return nextStatus.available ? gateway.startGame(undefined, controller.signal) : Promise.reject(new Error('The Asteroids service is unavailable.')) })
      .then(next => applyGameSnapshot(next, sequence))
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === 'AbortError')) setError(messageForError(reason)) })
    return () => controller.abort()
  }, [applyGameSnapshot, gateway])

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
    playAsteroidsFrameAudio(previous, game)
    previousGameRef.current = game
  }, [game, spriteAtlases])

  useEffect(() => () => stopAsteroidsAudioScene(), [])

  const perform = useCallback((action: AsteroidsAction) => {
    const currentGame = gameRef.current
    if (!currentGame || currentGame.phase !== 'playing' || busyRef.current || actionInFlight.current || tickInFlight.current) return false
    actionInFlight.current = true
    const sequence = ++requestSequenceRef.current
    setError(null)
    const request = gateway.action(currentGame.gameId, action)
      .then(next => applyGameSnapshot(next, sequence))
      .catch(reason => setError(messageForError(reason)))
      .finally(() => {
        actionInFlight.current = false
        if (pendingControlRequestRef.current === request) pendingControlRequestRef.current = null
      })
    pendingControlRequestRef.current = request
    return true
  }, [applyGameSnapshot, gateway])

  useEffect(() => {
    if (!game || game.phase !== 'playing' || !status) return
    const timer = window.setInterval(() => {
      if (busyRef.current || tickInFlight.current || actionInFlight.current) return
      tickInFlight.current = true
      const sequence = ++requestSequenceRef.current
      const request = gateway.action(game.gameId, 'tick')
        .then(next => applyGameSnapshot(next, sequence))
        .catch(reason => setError(messageForError(reason)))
        .finally(() => {
          tickInFlight.current = false
          if (pendingControlRequestRef.current === request) pendingControlRequestRef.current = null
        })
      pendingControlRequestRef.current = request
    }, status.tickMilliseconds)
    return () => window.clearInterval(timer)
  }, [applyGameSnapshot, game, gateway, status])

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
      setAsteroidsThrusting(heldThrustsRef.current.size > 0)
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
      unlockAsteroidsAudio()
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
      setAsteroidsThrusting(false)
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
      setAsteroidsThrusting(false)
      stopHeldControls()
      refreshHeldControlsRef.current = () => undefined
    }
  }, [perform])

  const setTouchControl = useCallback((control: AsteroidsTouchControl, pressed: boolean) => {
    if (pressed) unlockAsteroidsAudio()
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
    unlockAsteroidsAudio()
    void run(() => game ? gateway.reset(game.gameId) : gateway.startGame())
  }

  return <div className="ff-asteroids-page">
    <main className="ff-asteroids-main">
      {game ? <>
        <section className="ff-asteroids-stage" aria-label="Asteroid Blaster playfield">
          <canvas ref={canvasRef} className="ff-asteroids-canvas" />
          <section className="ff-asteroids-title">
            <div><small>{tableLabel}</small><h1>Asteroid Blaster</h1></div>
            <div className="ff-asteroids-title-actions">
              <AsteroidsSoundButton className="ff-asteroids-sound" />
              <button className="ff-asteroids-new" type="button" aria-label="New mission" title="New mission" onClick={newGame} disabled={busy}>{busy ? '…' : '↻'}</button>
            </div>
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
  const active = existing.filter(impact => game.tick >= impact.startedTick && game.tick - impact.startedTick < (impact.kind === 'hit' || impact.kind === 'alien-hit' ? 7 : 18))
  if (game.tick <= previous.tick) return active
  const remainingIds = new Set(game.asteroids.map(asteroid => asteroid.id))
  const previousById = new Map(previous.asteroids.map(asteroid => [asteroid.id, asteroid]))
  const hitFlashes = game.asteroids
    .filter(asteroid => (previousById.get(asteroid.id)?.hitPoints ?? asteroid.hitPoints) > asteroid.hitPoints)
    .map(asteroid => ({ x: asteroid.x, y: asteroid.y, startedTick: game.tick, kind: 'hit' as const }))
  const destroyedImpacts = previous.asteroids
    .filter(asteroid => !remainingIds.has(asteroid.id))
    .map(asteroid => ({ x: wrap(asteroid.x + asteroid.velocityX, game.width), y: wrap(asteroid.y + asteroid.velocityY, game.height), startedTick: game.tick, kind: 'destroyed' as const }))
  const alienHit = previous.alienShip !== null && game.alienShip?.id === previous.alienShip.id && game.alienShip.hitPoints < previous.alienShip.hitPoints
    ? [{ x: game.alienShip.x, y: game.alienShip.y, startedTick: game.tick, kind: 'alien-hit' as const }]
    : []
  const alienDestroyed = previous.alienShip !== null && game.alienShip?.id !== previous.alienShip.id && wasAlienDestroyed(previous.alienShip, game)
    ? [{ x: wrap(previous.alienShip.x + previous.alienShip.velocityX, game.width), y: wrap(previous.alienShip.y + previous.alienShip.velocityY, game.height), startedTick: game.tick, kind: 'alien-destroyed' as const }]
    : []
  return [...active, ...hitFlashes, ...destroyedImpacts, ...alienHit, ...alienDestroyed]
}

function wrap(value: number, maximum: number): number { return ((value % maximum) + maximum) % maximum }
