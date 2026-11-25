import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { AudioSettingsDialog, SlotMachine, SlotSymbol, SidecarSpinControls, WinHelpDialog, usePrefersReducedMotion, useSlotAudio } from '@fortuneforge/slot-client-core'
import { createSunstoneApi, type SunstoneFetch, type SunstoneResult, type SunstoneState } from './api'
import { FIFTH_SUN_SLOT_GAME } from './manifest'
import { SunwheelPanel } from './SunwheelPanel'
import { SunstoneIntroduction } from './SunstoneIntroduction'
import '@fortuneforge/slot-client-core/styles/machine.css'
import '@fortuneforge/slot-client-core/styles/machineOverrides.css'
import '@fortuneforge/slot-client-core/styles/dialogs.css'
import '@fortuneforge/slot-client-core/styles/sidecarSpinControls.css'
import './sunstoneGame.css'

export type SunstoneGameProps = {
  account?: { userId: string; balances: { slotsCredits: number } }
  authenticatedFetch?: SunstoneFetch
  renderShell?: (props: { balance: number; onNavigate: () => void; children: ReactNode }) => ReactNode
  onSpinStateChange?: (busy: boolean) => void
}
const experience = FIFTH_SUN_SLOT_GAME.experience
const money = (value: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(value)

/** Sunstone's paid screen, progression presentation and recovery; the platform supplies only neutral ports. */
export function SunstoneGame({ account, authenticatedFetch, renderShell, onSpinStateChange }: SunstoneGameProps) {
  const api = useMemo(() => account && authenticatedFetch ? createSunstoneApi(authenticatedFetch, account.userId) : null, [account?.userId, authenticatedFetch])
  const audio = useSlotAudio(experience.sounds)
  const reduced = usePrefersReducedMotion()
  const [state, setState] = useState<SunstoneState | null>(null)
  const [result, setResult] = useState<SunstoneResult | null>(null)
  const [reels, setReels] = useState<string[][]>(() => experience.rules.initialReels.map(column => [...column]))
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [wagerIndex, setWagerIndex] = useState(0)
  const [busy, setBusy] = useState(false)
  const [presenting, setPresenting] = useState(false)
  const [auto, setAuto] = useState(false)
  const [help, setHelp] = useState(false)
  const [settings, setSettings] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [ready, setReady] = useState(false)
  const [reload, setReload] = useState(0)
  const current = useRef({ api, alive: false })
  const inFlight = useRef(false)
  const reveal = useRef<(() => void) | null>(null)
  const spinRef = useRef<() => void>(() => {})
  const cabinet = useRef<HTMLDivElement>(null)
  const helpClose = useRef<HTMLButtonElement>(null)
  const { playCue, playSequence, startLoop, stopLoop, stopResultCues } = audio
  function restore(next: SunstoneState) { setState(next); if (next.slotsCreditsBalance !== null) setBalance(next.slotsCreditsBalance) }
  function show(next: SunstoneResult) {
    restore(next); setResult(next); setReels(next.reels.map(column => [...column]))
    const kind = next.paidMode && !next.spinFeature?.isEclipseSpin ? 'bonus' : next.payout.totalPoints >= next.wagerPoints * 5 ? 'five' : next.payout.totalPoints > 0 ? 'single-three' : 'no-win'
    playSequence(experience.sounds.events.results?.[kind] ?? [])
  }
  useEffect(() => {
    const request = { api, alive: true }; current.current = request
    const abort = new AbortController()
    inFlight.current = false; reveal.current?.(); setBusy(false); setPresenting(false); setAuto(false); setReady(false); setState(null); setResult(null); setError(null)
    setReels(experience.rules.initialReels.map(column => [...column])); setBalance(account?.balances.slotsCredits ?? 0)
    if (api) void (async () => {
      try {
        const next = await api.state(abort.signal)
        if (!request.alive) return
        restore(next)
        if (api.pending()) {
          inFlight.current = true; setBusy(true)
          const settled = await api.resume()
          if (!request.alive) return
          show(settled)
          const latest = await api.state(abort.signal)
          if (!request.alive) return
          restore(latest)
        }
        setReady(true)
      } catch (issue) { if (request.alive) setError(issue instanceof Error ? issue.message : 'Your temple round could not be restored.') }
      finally { if (request.alive) { inFlight.current = false; setBusy(false) } }
    })()
    return () => { request.alive = false; abort.abort(); reveal.current?.(); stopLoop('reel-spin'); stopLoop('special-reel-spin'); stopLoop('ambience'); stopLoop('special-ambience') }
  }, [api, reload, stopLoop])
  useEffect(() => { onSpinStateChange?.(busy || presenting); return () => onSpinStateChange?.(false) }, [busy, presenting, onSpinStateChange])
  const pending = api?.pending()
  const wager = pending?.wagerPoints ?? state?.paidMode?.wagerPoints ?? (state?.freeSpinsRemaining ? state.freeSpinWagerPoints! : experience.rules.wagerOptions[wagerIndex])
  const wagerRand = wager * .25
  async function spin() {
    if (inFlight.current) { reveal.current?.(); return }
    if (!api || current.current.api !== api || !ready || !state || presenting || help || settings) return
    const request = current.current
    const saved = api.pending()
    const free = saved?.useFreeSpin ?? state.freeSpinsRemaining > 0
    if (!saved && !free && balance < wagerRand) { setError('Add slot credits before placing this wager.'); setAuto(false); return }
    inFlight.current = true; setBusy(true); setError(null); setResult(null)
    stopResultCues(); playCue('lever-pull')
    const special = Boolean(state.paidMode || free)
    const ambience = special ? 'special-ambience' : 'ambience'
    const loop = special ? 'special-reel-spin' : 'reel-spin'
    stopLoop(special ? 'ambience' : 'special-ambience'); startLoop(ambience); startLoop(loop)
    try {
      const next = saved ? await api.resume() : await api.spin(wager, free)
      if (!request.alive || current.current !== request) return
      if (!reduced) await new Promise<void>(resolve => {
        const finish = () => { clearTimeout(timer); reveal.current = null; resolve() }
        const timer = window.setTimeout(finish, 450); reveal.current = finish
      })
      if (!request.alive || current.current !== request) return
      playCue(special ? 'special-reel-stop' : 'reel-stop'); show(next)
    } catch (issue) {
      if (request.alive && current.current === request) { setAuto(false); setError(issue instanceof Error ? issue.message : 'Recover the saved temple round.') }
    } finally {
      if (request.alive && current.current === request) { inFlight.current = false; setBusy(false); stopLoop(loop) }
    }
  }
  spinRef.current = () => { void spin() }
  useEffect(() => {
    if (!auto || busy || presenting || !ready || help || settings || error) return
    const timer = window.setTimeout(() => spinRef.current(), 1000)
    return () => clearTimeout(timer)
  }, [auto, busy, presenting, ready, help, settings, error, result])
  const navigate = () => { setAuto(false); reveal.current?.() }
  const openHelp = () => { navigate(); playCue('button-click'); setHelp(true) }
  const keys = useRef<(event: KeyboardEvent) => void>(() => {})
  keys.current = event => {
    if (event.repeat || event.altKey || event.ctrlKey || event.metaKey || (event.target as Element)?.closest?.('button,input,select,textarea,a,[contenteditable=true]')) return
    if (event.key.toLowerCase() === 'm') audio.toggleMuted()
    else if (!help && !settings && event.code === 'Space') { event.preventDefault(); spinRef.current() }
    else if (!help && !settings && event.key === '?') openHelp()
  }
  useEffect(() => { const handler = (event: KeyboardEvent) => keys.current(event); window.addEventListener('keydown', handler); return () => window.removeEventListener('keydown', handler) }, [])
  const winners = new Set(result?.payout.paylines.flatMap(line => line.matches.flatMap(paid => paid.match.positions.map(cell => `${cell.reel}:${cell.row}`))) ?? [])
  const content = <div className="slots-page sunstone-game" data-slot-theme={experience.cabinet.id} data-slot-controls="sidecar" data-game-busy={busy || presenting || undefined}
    style={{ '--sunstone-backdrop': `url("${experience.cabinet.pageBackdropImage}")`, '--slot-symbol-rows': reels[0]?.length ?? 4 } as CSSProperties}>
    <main className="slots-page__main"><div className="slots-page__layout"><div className="slots-page__stage">
      <h1 className="sunstone-game__sr-title">Sunstone Eclipse</h1>
      <div ref={cabinet} className="slots-page__cabinet-spotlight" aria-busy={busy} data-spinning={busy || undefined}>
        <SlotMachine cabinetTheme={experience.cabinet} reelCount={5} renderReel={column => <div className="sunstone-game__column">
          {reels[column].map((symbol, row) => <SlotSymbol key={row} symbol={symbol} symbolSet={experience.symbols} reelIndex={column} rowIndex={row}
            animated={!reduced && !busy} highlighted={!busy && winners.has(`${column}:${row}`)} />)}
        </div>} />
      </div>
      <SunwheelPanel feature={result?.spinFeature ?? null} spinKey={result ? 1 : null} isSpinning={busy} reducedMotion={reduced}
        pointValueInCents={25} cabinetRef={cabinet} paidMode={state?.paidMode ?? null} onPresentationBusyChange={setPresenting} />
      <div className="slots-page__playbar"><SidecarSpinControls activeWager={wagerRand} wagerIndex={wagerIndex} wagerOptions={experience.rules.wagerOptions}
        onSelectWager={index => { if (!inFlight.current && !presenting && !pending && !state?.paidMode && !state?.freeSpinsRemaining && index >= 0 && index < experience.rules.wagerOptions.length) { setWagerIndex(index); playCue('bet-step') } }}
        disabled={!ready || presenting || help || settings} wagerLocked={Boolean(pending || state?.paidMode || state?.freeSpinsRemaining)}
        freeSpinsRemaining={state?.freeSpinsRemaining ?? 0} isAutoSpinning={auto} onAutoSpinChange={setAuto} isFreeSpinBadgePopping={false}
        isSpinning={busy} isStopRequested={false} onSpin={() => { void spin() }} pointValueInCents={25}
        showFreeSpinBadge={Boolean(state?.freeSpinsRemaining)} specialControlsActive={false} specialRoundLabel="Eclipse" /></div>
    </div></div>
      <section className="sunstone-game__status" aria-live="polite">
        {error ? <p role="alert">{error} <button onClick={() => setReload(value => value + 1)} disabled={busy}>Reconnect / recover</button></p>
          : !ready ? <p>Opening your saved temple…</p> : busy ? <p>The calendar turns…</p>
          : result ? <p>{result.payout.totalPoints > 0 ? `Temple treasure · ${money(result.payout.totalPoints * .25)}` : 'The guardians rest.'}</p>
          : <p>Turn the Sunwheel. Awaken the guardians.</p>}
        {state?.paidMode && <p>{state.paidMode.mode === 'solar-eclipse' ? 'Solar' : 'Lunar'} Eclipse · {state.paidMode.remaining} paid rounds remain at {money(wagerRand)} each · three alignments per round</p>}
        {Boolean(state?.freeSpinsRemaining) && <p>{state!.freeSpinsRemaining} saved free rounds · original wager {money(wagerRand)}</p>}
      </section>
      <footer className="sunstone-game__utilities"><button onClick={openHelp}>How to win</button>
        <button onClick={() => { navigate(); setSettings(true) }}>Sound</button><span>Balance {money(balance)}</span></footer>
    </main>
    <WinHelpDialog isOpen={help} theme="sunstone-eclipse" title="How to win · Sunstone Eclipse" eyebrow="The Sunwheel" closeButtonRef={helpClose} onClose={() => setHelp(false)}>
      <SunstoneIntroduction onClose={() => setHelp(false)} /></WinHelpDialog>
    <AudioSettingsDialog isOpen={settings} theme="sunstone-eclipse" onClose={() => setSettings(false)} preferences={audio.preferences}
      onToggleMuted={audio.toggleMuted} onToggleResultsOnly={audio.toggleResultsOnly} onVolumeChange={audio.setVolume} />
  </div>
  return renderShell ? renderShell({ balance, onNavigate: navigate, children: content }) : content
}
