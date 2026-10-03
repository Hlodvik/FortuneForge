import { useEffect, useRef, useState, type ReactNode } from 'react'
import { startFlappySimulation } from '@fortuneforge/games-flappy'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { FlappyFreeRunGateway } from '../../../games/arcade/arcadeCompetitionApi'
import { playFlappySound } from './flappyAudio'
import { FlappyFlightScene } from './FlappyFlightScene'
import { useRecordedFlappy } from './useRecordedFlappy'
import './FlappyFreeRunPage.css'

export type FlappyFreeRunPageProps = Readonly<{ account: AccountSummary; gateway: FlappyFreeRunGateway }>
const preview = startFlappySimulation(17)
export function FlappyFreeRunPage(props: FlappyFreeRunPageProps) { return <RecordedFlight key={props.account.userId} {...props} /> }

function RecordedFlight({ account, gateway }: FlappyFreeRunPageProps) {
  const flight = useRecordedFlappy(account.userId, gateway)
  const course = useRef<HTMLDivElement>(null)
  const [panel, setPanel] = useState<'rules' | 'discard' | null>(null)
  const [reducedMotion, setReducedMotion] = useState(false)
  const priorScore = useRef(0)
  const priorGamePhase = useRef(gamePhase(preview.phase))
  useEffect(() => {
    const media = matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReducedMotion(media.matches)
    update(); media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  useEffect(() => { if (flight.phase === 'playing') course.current?.focus({ preventScroll: true }) }, [flight.phase])
  const game = flight.view?.state ?? preview
  const pending = flight.recovery?.kind === 'submission' ? flight.recovery.submission : null
  const score = flight.result?.score ?? pending?.display.score ?? game.score
  const active = flight.phase === 'playing', resumable = flight.phase === 'paused'
  const busy = flight.phase === 'starting' || flight.phase === 'submitting'
  const focusFlap = () => { course.current?.focus({ preventScroll: true }); playFlappySound('flap'); flight.flap() }
  const beginFlight = () => { playFlappySound('flap'); void flight.start() }
  const openPanel = (next: 'rules' | 'discard') => { flight.pause(); setPanel(next) }
  useEffect(() => {
    if (game.score > priorScore.current) playFlappySound('score')
    priorScore.current = game.score
  }, [game.score])
  useEffect(() => {
    const next = gamePhase(game.phase)
    if (priorGamePhase.current === 'playing' && next === 'collision') playFlappySound('collision')
    priorGamePhase.current = next
  }, [game.phase])
  return <main className="flappy-free-run-page" data-phase={flight.phase}>
    <div className="flappy-stage">
      <div className="flappy-course" tabIndex={0} ref={course} aria-label="Flappy playfield" aria-describedby="flappy-input-hint"
        onContextMenu={event => event.preventDefault()} onDragStart={event => event.preventDefault()}
        onKeyDown={event => {
          if (event.target !== event.currentTarget) return
          if (event.code === 'Space' && (active || resumable)) { event.preventDefault(); if (!event.repeat) { playFlappySound('flap'); flight.flap() } }
          if (event.code === 'Escape' && active) { event.preventDefault(); flight.pause() }
        }}
        onPointerDown={event => {
          if (event.button !== 0 || !event.isPrimary || !(active || resumable) || (event.target as HTMLElement).closest('button,.flappy-panel')) return
          event.preventDefault(); focusFlap()
        }}>
        <FlappyFlightScene game={game} reducedMotion={reducedMotion} />
        {(active || resumable) && <div className="flappy-score" aria-label={`Score ${game.score}`}>{game.score}</div>}
        <div className="flappy-course-actions">
          {active && <button type="button" aria-label="Pause" onClick={flight.pause}>Ⅱ</button>}
          <button type="button" aria-label="Rules" onClick={() => openPanel('rules')}>?</button>
        </div>
        {!active && <section className={`flappy-panel flappy-panel--${flight.phase}`} aria-live={busy ? 'polite' : 'off'}>
          {(flight.phase === 'lobby' || flight.phase === 'starting') && <><span className="flappy-panel-mode">Fortune Forge Arcade</span><h1>Flappy</h1><p>Tap to fly</p><button type="button" aria-label={busy ? 'Preparing flight…' : 'Start flight'} disabled={busy} onClick={beginFlight}>{busy ? 'Loading…' : 'Start'}</button></>}
          {resumable && <><h2>Paused</h2><button type="button" aria-label="Resume flight" onClick={() => flight.resume()}>Resume</button><button className="flappy-secondary" type="button" onClick={() => openPanel('discard')}>New flight</button></>}
          {flight.phase === 'start-failed' && <><h2>Couldn’t start</h2><button type="button" aria-label="Retry flight start" onClick={flight.retry}>Retry</button></>}
          {(flight.phase === 'submitting' || flight.phase === 'submit-failed') && <><h2>Game over</h2><p className="flappy-result-score">{score}</p><p>{busy ? 'Saving…' : 'Save interrupted'}</p>{!busy && <button type="button" aria-label="Retry recording" onClick={flight.retry}>Retry save</button>}{!busy && <button className="flappy-secondary" type="button" onClick={() => openPanel('discard')}>Clear</button>}</>}
          {flight.phase === 'result' && <><h2>Game over</h2><div className="flappy-result-board"><span>Score<strong className="flappy-result-score" aria-label={`Official score ${score}`}>{score}</strong></span><span>Best<strong>{flight.sessionBest ?? score}</strong></span></div><button type="button" onClick={beginFlight}>Fly again</button></>}
          {flight.phase === 'failed' && <><h2>Game over</h2><button type="button" onClick={flight.discard}>New flight</button></>}
          {flight.phase === 'unavailable' && <><h2>Saved flight unavailable</h2><button type="button" onClick={() => openPanel('discard')}>Clear saved flight</button></>}
        </section>}
      </div>
    </div>
    <footer className="flappy-controls">
      <span className="flappy-input-hint" id="flappy-input-hint">Tap or press Space</span>
      {flight.error && <p className="flappy-notice" role="alert">{flight.error}</p>}
      <button className="flappy-flap" type="button" disabled={!active && !resumable} aria-label="Flap"
        onPointerDown={event => { if (event.button === 0 && event.isPrimary) { event.preventDefault(); focusFlap() } }}
        onClick={event => { if (event.detail === 0) focusFlap() }}><span aria-hidden="true">▲</span> Flap</button>
    </footer>
    {panel !== null && <FlappyDialog title={panel === 'rules' ? 'Flappy rules' : 'Clear this flight?'} onClose={() => setPanel(null)}>
      {panel === 'rules' ? <><p>Tap the course, press Space, or use the Flap button.</p><p>Pass through each opening. A pipe, the ceiling, or the ground ends the flight.</p><p>The course speeds up as your score rises. Leaving the window pauses the game.</p></> : <><p>{flight.phase === 'submit-failed' ? 'This score may already be saved. Clearing removes the local retry.' : 'This unfinished flight will be removed.'}</p><button type="button" onClick={() => { flight.discard(); setPanel(null) }}>Clear flight</button></>}
    </FlappyDialog>}
  </main>
}

function gamePhase(phase: string): 'playing' | 'collision' { return phase === 'playing' ? 'playing' : 'collision' }

function FlappyDialog({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null), close = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null, node = dialog.current
    node?.showModal(); close.current?.focus()
    return () => { node?.close(); if (opener?.isConnected) opener.focus({ preventScroll: true }) }
  }, [])
  return <dialog className="flappy-dialog" ref={dialog} aria-label={title} onCancel={event => { event.preventDefault(); onClose() }}
    onKeyDown={event => {
      event.stopPropagation()
      if (event.key !== 'Tab') return
      const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled),[tabindex="0"]'))
      const first = focusable[0], last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
    }}>
    <header><h2>{title}</h2><button type="button" aria-label="Close flight details" ref={close} onClick={onClose}>×</button></header>
    <div className="flappy-dialog-body" tabIndex={0}>{children}</div>
  </dialog>
}
