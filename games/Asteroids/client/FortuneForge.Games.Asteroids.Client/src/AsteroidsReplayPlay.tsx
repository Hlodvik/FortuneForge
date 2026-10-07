import { useCallback, useEffect, useRef, useState } from 'react'
import type { AsteroidsEvent, AsteroidsGameState } from './contracts'
import { renderAsteroids, type AsteroidsImpact, type AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'
import { formatScore } from './asteroidsHelpers'
import { AsteroidsReplaySession, type AsteroidsHeldControl, type AsteroidsReplayDisplayResult, type AsteroidsReplayPayload, type AsteroidsReplaySessionView } from './asteroidsReplaySession'
import { loadAsteroidsSpriteAtlases } from './asteroidsSprites'
import './asteroidsReplayPlay.css'

export type AsteroidsReplayPlayProps = Readonly<{ runId: string; seedHex: string; modeLabel?: string; onComplete: (replay: AsteroidsReplayPayload, display: AsteroidsReplayDisplayResult) => void }>

/** A network-free paid-run surface. Its callback receives only canonical controls plus local display data. */
export function AsteroidsReplayPlay({ runId, seedHex, modeLabel = 'Deterministic replay', onComplete }: AsteroidsReplayPlayProps) {
  const sessionRef = useRef(new AsteroidsReplaySession(runId, seedHex))
  const completionSent = useRef(false)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const previousRef = useRef<AsteroidsGameState | null>(null)
  const impactsRef = useRef<readonly AsteroidsImpact[]>([])
  const [view, setView] = useState<AsteroidsReplaySessionView>(sessionRef.current.view)
  const [atlases, setAtlases] = useState<AsteroidsSpriteAtlases>({})

  const advance = useCallback(() => {
    const session = sessionRef.current
    const next = session.advanceFrame()
    setView(next)
    const completion = session.takeCompletion()
    if (completion !== null && !completionSent.current) {
      completionSent.current = true
      onComplete(completion.replay, completion.display)
    }
  }, [onComplete])

  useEffect(() => {
    const next = new AsteroidsReplaySession(runId, seedHex)
    sessionRef.current = next
    completionSent.current = false
    previousRef.current = null
    impactsRef.current = []
    setView(next.view)
  }, [runId, seedHex])

  useEffect(() => {
    let active = true
    void loadAsteroidsSpriteAtlases().then(next => { if (active) setAtlases(next) })
    return () => { active = false }
  }, [])

  const game = rendererGame(runId, view)
  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) return
    canvas.width = game.width
    canvas.height = game.height
    const context = canvas.getContext('2d')
    impactsRef.current = nextImpacts(impactsRef.current, previousRef.current, game)
    if (context !== null) renderAsteroids(context, game, atlases, impactsRef.current)
    previousRef.current = game
  }, [atlases, game])

  useEffect(() => {
    if (view.status !== 'running') return
    const timer = window.setInterval(advance, 33)
    return () => window.clearInterval(timer)
  }, [advance, view.status])

  useEffect(() => {
    const session = sessionRef.current
    const clear = () => { session.clearHeld(); setView(session.view) }
    const down = (event: KeyboardEvent) => {
      if (isInteractiveTarget(event.target)) return
      const control = keyControl(event)
      if (control === null || session.view.status !== 'running') return
      event.preventDefault()
      session.setHeld(control, true)
      setView(session.view)
    }
    const up = (event: KeyboardEvent) => {
      const control = keyControl(event)
      if (control === null) return
      event.preventDefault()
      session.setHeld(control, false)
      setView(session.view)
    }
    window.addEventListener('keydown', down)
    window.addEventListener('keyup', up)
    window.addEventListener('blur', clear)
    return () => {
      window.removeEventListener('keydown', down)
      window.removeEventListener('keyup', up)
      window.removeEventListener('blur', clear)
      clear()
    }
  }, [runId, seedHex])

  const setPointerControl = (control: AsteroidsHeldControl, pressed: boolean) => {
    sessionRef.current.setHeld(control, pressed)
    setView(sessionRef.current.view)
  }
  const result = view.status === 'finished' ? localResult(view) : null
  const seconds = Math.ceil(view.remainingSteps * 0.033)

  return <section className="ff-asteroids-replay" aria-label={modeLabel + ' Asteroids replay'}>
    <header className="ff-asteroids-replay-head">
      <div><small>{modeLabel}</small><h2>Asteroids</h2><p>Server-seeded local simulation · controls are recorded as held-state transitions.</p></div>
      <div className="ff-asteroids-replay-clock" aria-label={seconds + ' seconds remaining'}><small>Remaining</small><strong>{formatTime(seconds)}</strong></div>
    </header>
    <section className="ff-asteroids-replay-stats" aria-live="polite">
      <div><small>Score</small><strong>{formatScore(view.state.score)}</strong></div>
      <div><small>Wave</small><strong>{view.state.wave}</strong></div>
      <div><small>Lives</small><strong>{view.state.lives}</strong></div>
      <div><small>Frame</small><strong>{view.state.tick}/3600</strong></div>
    </section>
    <div className="ff-asteroids-replay-canvas-wrap">
      <canvas ref={canvasRef} className="ff-asteroids-replay-canvas" aria-label="Asteroids deterministic replay playfield" />
      {view.status !== 'running' && <div className="ff-asteroids-replay-overlay" role={view.status === 'failed' ? 'alert' : 'status'}>
        <small>{view.status === 'failed' ? 'Replay unavailable' : result?.reason === 'time-up' ? 'Time up' : 'Mission ended'}</small>
        <strong>{view.status === 'failed' ? 'Run stopped' : result?.reason === 'time-up' ? 'Two-minute limit reached' : 'Game over'}</strong>
        <span>{view.status === 'failed' ? view.error : formatScore(result?.score ?? view.state.score) + ' points · Wave ' + (result?.wave ?? view.state.wave) + ' · ' + (result?.lives ?? view.state.lives) + ' lives'}</span>
      </div>}
    </div>
    <p className="ff-asteroids-replay-message" aria-live="polite">{view.status === 'running' ? 'A / Left · D / Right · W / Up · Space to fire' : 'Controls are locked after the replay ends.'}</p>
    <div className="ff-asteroids-replay-controls" aria-label="Touch controls">
      <ControlButton label="Turn left" control="left" setControl={setPointerControl} disabled={view.status !== 'running'} />
      <ControlButton label="Thrust" control="thrust" setControl={setPointerControl} disabled={view.status !== 'running'} />
      <ControlButton label="Turn right" control="right" setControl={setPointerControl} disabled={view.status !== 'running'} />
      <ControlButton label="Fire" control="fire" setControl={setPointerControl} disabled={view.status !== 'running'} />
    </div>
  </section>
}

function ControlButton({ label, control, setControl, disabled }: Readonly<{ label: string; control: AsteroidsHeldControl; setControl: (control: AsteroidsHeldControl, pressed: boolean) => void; disabled: boolean }>) {
  return <button type="button" className="ff-asteroids-replay-control" aria-label={label} disabled={disabled}
    onPointerDown={event => { event.currentTarget.setPointerCapture(event.pointerId); setControl(control, true) }}
    onPointerUp={() => setControl(control, false)} onPointerCancel={() => setControl(control, false)} onLostPointerCapture={() => setControl(control, false)}
    onKeyDown={event => { if (event.key === ' ' || event.key === 'Enter') { event.preventDefault(); setControl(control, true) } }}
    onKeyUp={event => { if (event.key === ' ' || event.key === 'Enter') { event.preventDefault(); setControl(control, false) } }}>
    <strong>{label}</strong><span>{control === 'left' ? 'A / Left' : control === 'right' ? 'D / Right' : control === 'thrust' ? 'W / Up' : 'Space'}</span>
  </button>
}

function keyControl(event: KeyboardEvent): AsteroidsHeldControl | null {
  const key = event.key.toLowerCase()
  return key === 'a' || key === 'arrowleft' ? 'left' : key === 'd' || key === 'arrowright' ? 'right' : key === 'w' || key === 'arrowup' ? 'thrust' : event.code === 'Space' ? 'fire' : null
}
function isInteractiveTarget(target: EventTarget | null): boolean { return target instanceof HTMLElement && target.closest('a,button,input,select,textarea,[contenteditable="true"]') !== null }
function formatTime(seconds: number): string { return Math.floor(seconds / 60) + ':' + String(seconds % 60).padStart(2, '0') }
function localResult(view: AsteroidsReplaySessionView): AsteroidsReplayDisplayResult { return { score: view.state.score, wave: view.state.wave, lives: view.state.lives, reason: view.state.phase === 'game-over' ? 'game-over' : 'time-up' } }

function rendererGame(runId: string, view: AsteroidsReplaySessionView): AsteroidsGameState {
  const state = view.state
  return {
    gameId: runId, width: state.width, height: state.height,
    ship: { x: state.ship.position.x, y: state.ship.position.y, velocityX: state.ship.velocity.x, velocityY: state.ship.velocity.y, angle: state.ship.angle, invulnerabilityTicks: state.ship.invulnerabilityTicks, thrustTicks: state.ship.thrustTicks },
    asteroids: state.asteroids.map(asteroid => ({ id: asteroid.id, x: asteroid.position.x, y: asteroid.position.y, velocityX: asteroid.velocity.x, velocityY: asteroid.velocity.y, radius: asteroid.radius, size: asteroid.size, hitPoints: asteroid.hitPoints, spriteVariant: asteroid.spriteVariant })),
    bullets: state.bullets.map(bullet => ({ id: bullet.id, x: bullet.position.x, y: bullet.position.y, velocityX: bullet.velocity.x, velocityY: bullet.velocity.y, remainingTicks: bullet.remainingTicks })),
    powerUps: state.powerUps.map(powerUp => ({ id: powerUp.id, x: powerUp.position.x, y: powerUp.position.y, velocityX: powerUp.velocity.x, velocityY: powerUp.velocity.y, remainingTicks: powerUp.remainingTicks, type: powerUp.type })),
    score: state.score, bestScore: state.bestScore, lives: state.lives, wave: state.wave, tick: state.tick, phase: state.phase, lastEvent: state.event as AsteroidsEvent, scoreGained: state.scoreGained, rapidFireTicks: state.rapidFireTicks, message: state.message,
  }
}

function nextImpacts(existing: readonly AsteroidsImpact[], previous: AsteroidsGameState | null, game: AsteroidsGameState): readonly AsteroidsImpact[] {
  if (previous === null || previous.gameId !== game.gameId) return []
  const active = existing.filter(impact => game.tick >= impact.startedTick && game.tick - impact.startedTick < (impact.kind === 'hit' ? 7 : 18))
  const before = new Map(previous.asteroids.map(asteroid => [asteroid.id, asteroid]))
  const after = new Set(game.asteroids.map(asteroid => asteroid.id))
  return [...active,
    ...game.asteroids.filter(asteroid => (before.get(asteroid.id)?.hitPoints ?? asteroid.hitPoints) > asteroid.hitPoints).map(asteroid => ({ x: asteroid.x, y: asteroid.y, startedTick: game.tick, kind: 'hit' as const })),
    ...previous.asteroids.filter(asteroid => !after.has(asteroid.id)).map(asteroid => ({ x: asteroid.x, y: asteroid.y, startedTick: game.tick, kind: 'destroyed' as const }))]
}
