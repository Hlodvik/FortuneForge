import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { AsteroidsEvent, AsteroidsGameState } from './contracts'
import { playAsteroidsSound, setAsteroidsThrusting, stopAsteroidsAudioScene, unlockAsteroidsAudio } from './asteroidsAudio'
import { renderAsteroids, type AsteroidsImpact, type AsteroidsSpriteAtlases } from './asteroidsCanvasRenderer'
import { playAsteroidsFrameAudio } from './asteroidsFrameAudio'
import { formatScore } from './asteroidsHelpers'
import { AsteroidsReplaySession, type AsteroidsHeldControl, type AsteroidsReplayCompletion, type AsteroidsReplayDisplayResult, type AsteroidsReplayPayload, type AsteroidsReplaySessionView } from './asteroidsReplaySession'
import { loadAsteroidsSpriteAtlases } from './asteroidsSprites'
import { AsteroidsSoundButton } from './AsteroidsSoundButton'
import { wasAlienDestroyed } from './asteroidsAlienLifecycle'
import { AsteroidsTouchControls, type AsteroidsTouchControl } from './AsteroidsTouchControls'
import './asteroidsReplayPlay.css'

export type AsteroidsReplayPlayProps = Readonly<{ runId: string; seedHex: string; modeLabel?: string; onComplete: (replay: AsteroidsReplayPayload, display: AsteroidsReplayDisplayResult) => void }>

/** A network-free paid-run surface. Its callback receives only canonical controls plus local display data. */
export function AsteroidsReplayPlay({ runId, seedHex, modeLabel = 'Deterministic replay', onComplete }: AsteroidsReplayPlayProps) {
  const sessionRef = useRef(new AsteroidsReplaySession(runId, seedHex))
  const completionSent = useRef(false)
  const pendingCompletionRef = useRef<AsteroidsReplayCompletion | null>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const previousRef = useRef<AsteroidsGameState | null>(null)
  const previousAudioRef = useRef<AsteroidsGameState | null>(null)
  const impactsRef = useRef<readonly AsteroidsImpact[]>([])
  const completionSoundSent = useRef(false)
  const [view, setView] = useState<AsteroidsReplaySessionView>(sessionRef.current.view)
  const [atlases, setAtlases] = useState<AsteroidsSpriteAtlases>({})

  const advance = useCallback(() => {
    const session = sessionRef.current
    const next = session.advanceFrame()
    setView(next)
    const completion = session.takeCompletion()
    if (completion !== null && !completionSent.current) pendingCompletionRef.current = completion
  }, [])

  useEffect(() => {
    const next = new AsteroidsReplaySession(runId, seedHex)
    sessionRef.current = next
    completionSent.current = false
    pendingCompletionRef.current = null
    completionSoundSent.current = false
    previousRef.current = null
    previousAudioRef.current = null
    impactsRef.current = []
    setView(next.view)
  }, [runId, seedHex])

  useEffect(() => {
    let active = true
    void loadAsteroidsSpriteAtlases().then(next => { if (active) setAtlases(next) })
    return () => { active = false }
  }, [])

  const game = useMemo(() => rendererGame(runId, view), [runId, view.state])
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
    playAsteroidsFrameAudio(previousAudioRef.current, game, { playerFired: view.firedThisFrame })
    previousAudioRef.current = game
  }, [game, view.firedThisFrame])

  useEffect(() => () => stopAsteroidsAudioScene(), [])

  useEffect(() => {
    if (view.status === 'running') return
    setAsteroidsThrusting(false)
    if (view.status === 'finished' && view.state.phase !== 'game-over' && !completionSoundSent.current) {
      completionSoundSent.current = true
      playAsteroidsSound('time-up')
    }
  }, [view.state.phase, view.status])

  useEffect(() => {
    if (view.status === 'running' || completionSent.current) return
    const completion = pendingCompletionRef.current
    if (completion === null) return
    completionSent.current = true
    pendingCompletionRef.current = null
    onComplete(completion.replay, completion.display)
  }, [onComplete, view.status])

  useEffect(() => {
    if (view.status !== 'running') return
    const timer = window.setInterval(advance, 33)
    return () => window.clearInterval(timer)
  }, [advance, view.status])

  useEffect(() => {
    const session = sessionRef.current
    const clear = () => { session.clearHeld(); setAsteroidsThrusting(false); setView(session.view) }
    const down = (event: KeyboardEvent) => {
      if (isInteractiveTarget(event.target)) return
      const control = keyControl(event)
      if (control === null || session.view.status !== 'running') return
      event.preventDefault()
      unlockAsteroidsAudio()
      session.setHeld(control, true)
      if (control === 'thrust') setAsteroidsThrusting(true)
      setView(session.view)
    }
    const up = (event: KeyboardEvent) => {
      const control = keyControl(event)
      if (control === null) return
      event.preventDefault()
      session.setHeld(control, false)
      if (control === 'thrust') setAsteroidsThrusting(false)
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

  const setPointerControl = useCallback((control: AsteroidsTouchControl, pressed: boolean) => {
    if (pressed) unlockAsteroidsAudio()
    sessionRef.current.setHeld(control, pressed)
    if (control === 'thrust') setAsteroidsThrusting(pressed)
    setView(sessionRef.current.view)
  }, [])
  const result = view.status === 'finished' ? localResult(view) : null
  const seconds = Math.ceil(view.remainingSteps * 0.033)

  return <section className="ff-asteroids-replay" aria-label={modeLabel + ' Asteroid Blaster run'}>
    <div className="ff-asteroids-replay-stage">
      <canvas ref={canvasRef} className="ff-asteroids-replay-canvas" aria-label="Asteroid Blaster playfield" />
      <header className="ff-asteroids-replay-head">
        <div className="ff-asteroids-replay-title"><small>{modeLabel}</small><h2>Asteroid Blaster</h2></div>
        <div className="ff-asteroids-replay-head-actions">
          <AsteroidsSoundButton className="ff-asteroids-replay-sound" />
          <div className="ff-asteroids-replay-clock" aria-label={seconds + ' seconds remaining'}><small>Time</small><strong>{formatTime(seconds)}</strong></div>
        </div>
      </header>
      <section className="ff-asteroids-replay-stats" aria-live="polite">
        <div className="ff-asteroids-replay-score"><small>Score</small><strong>{formatScore(view.state.score)}</strong></div>
        <div className="ff-asteroids-replay-wave"><small>Wave</small><strong>{view.state.wave}</strong></div>
        <div className="ff-asteroids-replay-lives"><small>Lives</small><strong>{'◆'.repeat(view.state.lives) || '—'}</strong></div>
      </section>
      {view.status !== 'running' && <div className="ff-asteroids-replay-overlay" role={view.status === 'failed' ? 'alert' : 'status'}>
        <small>{view.status === 'failed' ? 'Replay unavailable' : result?.reason === 'time-up' ? 'Time up' : 'Mission ended'}</small>
        <strong>{view.status === 'failed' ? 'Run stopped' : result?.reason === 'time-up' ? 'Two-minute limit reached' : 'Game over'}</strong>
        <span>{view.status === 'failed' ? view.error : formatScore(result?.score ?? view.state.score) + ' points · Wave ' + (result?.wave ?? view.state.wave) + ' · ' + (result?.lives ?? view.state.lives) + ' lives'}</span>
      </div>}
      <AsteroidsTouchControls disabled={view.status !== 'running'} shipAngle={view.state.ship.angle} onControlChange={setPointerControl} />
    </div>
  </section>
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
  const enemies = state as typeof state & Readonly<{
    alienShip?: null | Readonly<{ id: number; position: Readonly<{ x: number; y: number }>; velocity: Readonly<{ x: number; y: number }>; radius: number; type: 'scout' | 'hunter'; hitPoints: number; fireCooldownTicks: number; courseChangeTicks: number; remainingTicks: number }>
    enemyBullets?: readonly Readonly<{ id: number; position: Readonly<{ x: number; y: number }>; velocity: Readonly<{ x: number; y: number }>; remainingTicks: number }>[]
  }>
  return {
    gameId: runId, width: state.width, height: state.height,
    ship: { x: state.ship.position.x, y: state.ship.position.y, velocityX: state.ship.velocity.x, velocityY: state.ship.velocity.y, angle: state.ship.angle, invulnerabilityTicks: state.ship.invulnerabilityTicks, thrustTicks: state.ship.thrustTicks },
    asteroids: state.asteroids.map(asteroid => ({ id: asteroid.id, x: asteroid.position.x, y: asteroid.position.y, velocityX: asteroid.velocity.x, velocityY: asteroid.velocity.y, radius: asteroid.radius, size: asteroid.size, hitPoints: asteroid.hitPoints, spriteVariant: asteroid.spriteVariant })),
    bullets: state.bullets.map(bullet => ({ id: bullet.id, x: bullet.position.x, y: bullet.position.y, velocityX: bullet.velocity.x, velocityY: bullet.velocity.y, remainingTicks: bullet.remainingTicks })),
    alienShip: enemies.alienShip === null || enemies.alienShip === undefined ? null : { id: enemies.alienShip.id, x: enemies.alienShip.position.x, y: enemies.alienShip.position.y, velocityX: enemies.alienShip.velocity.x, velocityY: enemies.alienShip.velocity.y, radius: enemies.alienShip.radius, type: enemies.alienShip.type, hitPoints: enemies.alienShip.hitPoints, fireCooldownTicks: enemies.alienShip.fireCooldownTicks, courseChangeTicks: enemies.alienShip.courseChangeTicks, remainingTicks: enemies.alienShip.remainingTicks },
    enemyBullets: (enemies.enemyBullets ?? []).map(bullet => ({ id: bullet.id, x: bullet.position.x, y: bullet.position.y, velocityX: bullet.velocity.x, velocityY: bullet.velocity.y, remainingTicks: bullet.remainingTicks })),
    powerUps: state.powerUps.map(powerUp => ({ id: powerUp.id, x: powerUp.position.x, y: powerUp.position.y, velocityX: powerUp.velocity.x, velocityY: powerUp.velocity.y, remainingTicks: powerUp.remainingTicks, type: powerUp.type })),
    score: state.score, bestScore: state.bestScore, lives: state.lives, wave: state.wave, tick: state.tick, phase: state.phase, lastEvent: state.event as AsteroidsEvent, scoreGained: state.scoreGained, rapidFireTicks: state.rapidFireTicks, message: state.message,
  }
}

function nextImpacts(existing: readonly AsteroidsImpact[], previous: AsteroidsGameState | null, game: AsteroidsGameState): readonly AsteroidsImpact[] {
  if (previous === null || previous.gameId !== game.gameId) return []
  const active = existing.filter(impact => game.tick >= impact.startedTick && game.tick - impact.startedTick < (impact.kind === 'hit' || impact.kind === 'alien-hit' ? 7 : 18))
  if (game.tick <= previous.tick) return active
  const before = new Map(previous.asteroids.map(asteroid => [asteroid.id, asteroid]))
  const after = new Set(game.asteroids.map(asteroid => asteroid.id))
  return [...active,
    ...game.asteroids.filter(asteroid => (before.get(asteroid.id)?.hitPoints ?? asteroid.hitPoints) > asteroid.hitPoints).map(asteroid => ({ x: asteroid.x, y: asteroid.y, startedTick: game.tick, kind: 'hit' as const })),
    ...previous.asteroids.filter(asteroid => !after.has(asteroid.id)).map(asteroid => ({ x: wrap(asteroid.x + asteroid.velocityX, game.width), y: wrap(asteroid.y + asteroid.velocityY, game.height), startedTick: game.tick, kind: 'destroyed' as const })),
    ...(previous.alienShip !== null && game.alienShip?.id === previous.alienShip.id && game.alienShip.hitPoints < previous.alienShip.hitPoints
      ? [{ x: game.alienShip.x, y: game.alienShip.y, startedTick: game.tick, kind: 'alien-hit' as const }]
      : []),
    ...(previous.alienShip !== null && game.alienShip?.id !== previous.alienShip.id && wasAlienDestroyed(previous.alienShip, game)
      ? [{ x: wrap(previous.alienShip.x + previous.alienShip.velocityX, game.width), y: wrap(previous.alienShip.y + previous.alienShip.velocityY, game.height), startedTick: game.tick, kind: 'alien-destroyed' as const }]
      : [])]
}

function wrap(value: number, maximum: number): number { return ((value % maximum) + maximum) % maximum }
