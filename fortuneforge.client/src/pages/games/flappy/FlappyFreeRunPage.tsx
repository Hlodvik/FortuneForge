import { useEffect, useRef, useState, type ReactNode } from 'react'
import { flappyLevel, startFlappySimulation } from '@fortuneforge/games-flappy'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import type { FlappyFreeRunGateway } from '../../../games/arcade/arcadeCompetitionApi'
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
  const focusFlap = () => { course.current?.focus({ preventScroll: true }); flight.flap() }
  const openPanel = (next: 'rules' | 'discard') => { flight.pause(); setPanel(next) }
  return <main className="flappy-free-run-page" data-phase={flight.phase}>
    <header className="flappy-hud">
      <div className="flappy-hud-score"><span>{flight.phase === 'result' ? 'Official score' : 'Score'}</span><strong>{score}</strong></div>
      <div className="flappy-hud-context">{flight.sessionBest !== null && !active && <span>Session best <b>{flight.sessionBest}</b></span>}<span>Level <b>{flappyLevel(game.score)}</b></span></div>
      <div className="flappy-hud-actions">{active && <button type="button" onClick={flight.pause}>Pause</button>}<button type="button" onClick={() => openPanel('rules')}>Rules</button></div>
    </header>
    <div className="flappy-stage">
      <div className="flappy-course" tabIndex={0} ref={course} aria-label="Flappy playfield" aria-describedby="flappy-input-hint"
        onContextMenu={event => event.preventDefault()} onDragStart={event => event.preventDefault()}
        onKeyDown={event => {
          if (event.target !== event.currentTarget) return
          if (event.code === 'Space' && (active || resumable)) { event.preventDefault(); if (!event.repeat) flight.flap() }
          if (event.code === 'Escape' && active) { event.preventDefault(); flight.pause() }
        }}
        onPointerDown={event => {
          if (event.button !== 0 || !event.isPrimary || !(active || resumable) || (event.target as HTMLElement).closest('button,.flappy-panel')) return
          event.preventDefault(); focusFlap()
        }}>
        <FlappyFlightScene game={game} reducedMotion={reducedMotion} />
        {!active && <section className={`flappy-panel flappy-panel--${flight.phase}`} aria-live={busy ? 'polite' : 'off'}>
          {(flight.phase === 'lobby' || flight.phase === 'starting') && <><span className="flappy-panel-mode">Free flight</span><h1>Flappy</h1><p>One flap. One opening at a time.</p><button type="button" disabled={busy} onClick={() => void flight.start()}>{busy ? 'Preparing flight…' : 'Start flight'}</button></>}
          {resumable && <><h2>Flight paused</h2><p>Resume when ready.</p><button type="button" onClick={() => flight.resume()}>Resume flight</button><button className="flappy-secondary" type="button" onClick={() => openPanel('discard')}>New flight</button></>}
          {flight.phase === 'start-failed' && <><h2>Flight start interrupted</h2><p>Retry the same saved flight start.</p><button type="button" onClick={flight.retry}>Retry flight start</button></>}
          {(flight.phase === 'submitting' || flight.phase === 'submit-failed') && <><h2>{busy ? 'Recording flight…' : 'Recording interrupted'}</h2><p>Provisional score <strong>{score}</strong> · {collisionLabel(pending?.display.phase)}</p>{!busy && <button type="button" onClick={flight.retry}>Retry recording</button>}{!busy && <button className="flappy-secondary" type="button" onClick={() => openPanel('discard')}>Clear saved flight</button>}</>}
          {flight.phase === 'result' && <><h2>Flight recorded</h2><p className="flappy-result-score" aria-label={`Official score ${score}`}>{score}</p><p>{collisionLabel(flight.result?.terminal)}</p><button type="button" onClick={() => void flight.start()}>Fly again</button></>}
          {flight.phase === 'failed' && <><h2>Flight ended</h2><p>This flight reached the recording limit.</p><button type="button" onClick={flight.discard}>New flight</button></>}
          {flight.phase === 'unavailable' && <><h2>Saved flight unavailable</h2><button type="button" onClick={() => openPanel('discard')}>Clear saved flight</button></>}
        </section>}
      </div>
    </div>
    <footer className="flappy-controls">
      <div><span id="flappy-input-hint">Space or tap to flap</span><small>Free play · no jackpot entry</small></div>
      <p className="flappy-notice" role={flight.error ? 'alert' : 'status'}>{flight.error ?? (flight.phase === 'result' ? 'Saved to your account.' : flight.phase === 'failed' ? 'Flight not recorded.' : '\u00a0')}</p>
      <button className="flappy-flap" type="button" disabled={!active && !resumable} aria-label="Flap"
        onPointerDown={event => { if (event.button === 0 && event.isPrimary) { event.preventDefault(); focusFlap() } }}
        onClick={event => { if (event.detail === 0) focusFlap() }}>Flap <span aria-hidden="true">↑</span></button>
    </footer>
    {panel !== null && <FlappyDialog title={panel === 'rules' ? 'Flappy rules' : 'Clear this flight?'} onClose={() => setPanel(null)}>
      {panel === 'rules' ? <><p>Press Space, click the course or tap Flap. Each press gives one upward flap; holding Space does not repeat it.</p><p>Pass through the openings. Pipes, the ceiling and the bottom edge end the flight. Every five points increases the level.</p><p>Pause keeps the same course. Switching tabs or leaving the window pauses the flight; resume when ready. Reload restores a saved flight paused.</p><p>The service verifies the saved flap timing before confirming your score. Retry uses the same flight and input. The recording limit is three minutes of active flight or 1,500 flaps.</p><p>Free practice does not enter a jackpot or leaderboard and does not spend credits.</p></> : <><p>{flight.phase === 'submit-failed' ? 'This recording may already be saved by the service. Clearing removes your local retry data.' : 'Your unfinished flight and local flap timing will be removed.'}</p><button type="button" onClick={() => { flight.discard(); setPanel(null) }}>Clear flight</button></>}
    </FlappyDialog>}
  </main>
}

function collisionLabel(phase: string | undefined) { return phase === 'ground-collision' ? 'Bottom edge' : phase === 'ceiling-collision' ? 'Ceiling' : phase === 'obstacle-collision' ? 'Pipe collision' : 'Flight ended' }

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
