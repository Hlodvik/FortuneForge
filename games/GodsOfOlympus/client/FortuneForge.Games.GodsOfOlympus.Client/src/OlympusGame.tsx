import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { AudioSettingsDialog, SlotMachine, SlotSymbol, SidecarSpinControls, WinHelpDialog, usePrefersReducedMotion, useSlotAudio, type GridPosition } from '@fortuneforge/slot-client-core'
import { createOlympusApi, type OlympusFetch, type OlympusResult, type OlympusState } from './api'
import { GODS_OF_OLYMPUS_EXPERIENCE_SET as experience } from './manifest'
import { GODS_TUMBLE_INITIAL_REELS, GODS_TUMBLE_MULTIPLIERS } from './tumble'
import { GodsTumbleAwardBadge } from './GodsTumbleAwardBadge'
import '@fortuneforge/slot-client-core/styles/machine.css'
import '@fortuneforge/slot-client-core/styles/machineOverrides.css'
import '@fortuneforge/slot-client-core/styles/dialogs.css'
import '@fortuneforge/slot-client-core/styles/sidecarSpinControls.css'
import './olympusGame.css'

export type OlympusGameProps = {
  account?: { userId: string; balances: { slotsCredits: number } }
  authenticatedFetch?: OlympusFetch
  renderShell?: (props: { balance: number; onNavigate: () => void; children: ReactNode }) => ReactNode
  onSpinStateChange?: (busy: boolean) => void
}
const money = (amount: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(amount)
const initial = () => GODS_TUMBLE_INITIAL_REELS.map(column => [...column])

/** Paid tumble presentation and owed Trial playback, driven exclusively by the package's validated replies. */
export function OlympusGame(props: OlympusGameProps) {
  const { account, authenticatedFetch } = props
  const api = useMemo(() => account && authenticatedFetch ? createOlympusApi(authenticatedFetch, account.userId) : null, [account?.userId, authenticatedFetch])
  const audio = useSlotAudio(experience.sounds)
  const reduced = usePrefersReducedMotion()
  const [state, setState] = useState<OlympusState | null>(null)
  const [result, setResult] = useState<OlympusResult | null>(null)
  const [reels, setReels] = useState<string[][]>(initial)
  const [cleared, setCleared] = useState<readonly GridPosition[]>([])
  const [dropRows, setDropRows] = useState<number[][]>([])
  const [stepAward, setStepAward] = useState(0)
  const [stepIndex, setStepIndex] = useState(0)
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [wagerIndex, setWagerIndex] = useState(5)
  const [busy, setBusy] = useState(false)
  const [auto, setAuto] = useState(false)
  const [help, setHelp] = useState(false)
  const [settings, setSettings] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [ready, setReady] = useState(false)
  const active = useRef({ api, alive: false })
  const spinning = useRef(false)
  const skip = useRef(false)
  const wake = useRef<(() => void) | null>(null)
  const spinRef = useRef<() => void>(() => {})
  const helpClose = useRef<HTMLButtonElement>(null)
  const { playCue, playSequence, startLoop, stopLoop, stopResultCues } = audio
  function restore(next: OlympusState) { setState(next); if (next.slotsCreditsBalance !== null) setBalance(next.slotsCreditsBalance) }
  function finish(next: OlympusResult) {
    restore(next); setResult(next)
    setReels((next.olympusTumble?.steps.at(-1)?.reels ?? next.reels).map(column => [...column]))
    setCleared([]); setDropRows([]); setStepAward(0)
    const kind = next.freeSpinsAwarded > 0 ? 'bonus' : next.payout.totalPoints >= next.wagerPoints * 5 ? 'five' : next.payout.totalPoints > 0 ? 'single-three' : 'no-win'
    playSequence((next.isFreeSpin ? experience.sounds.events.specialResults : experience.sounds.events.results)?.[kind] ?? [])
  }
  useEffect(() => {
    const request = { api, alive: true }; active.current = request
    const abort = new AbortController()
    spinning.current = false; wake.current?.(); skip.current = false
    setBusy(false); setAuto(false); setState(null); setReady(false); setResult(null); setError(null)
    setCleared([]); setDropRows([]); setStepAward(0); setReels(initial()); setBalance(account?.balances.slotsCredits ?? 0)
    if (api) void (async () => {
      try {
        const next = await api.state(abort.signal)
        if (!request.alive) return
        restore(next)
        if (next.freeSpinsRemaining > 0) setReels(experience.rules.initialReels.map(column => [...column]))
        if (api.pending()) {
          spinning.current = true; setBusy(true)
          const saved = await api.resume()
          if (!request.alive) return
          finish(saved)
          const latest = await api.state(abort.signal)
          if (!request.alive) return
          restore(latest)
        }
        setReady(true)
      } catch (issue) { if (request.alive) setError(issue instanceof Error ? issue.message : 'Your Olympian progress could not be restored.') }
      finally { if (request.alive) { spinning.current = false; setBusy(false) } }
    })()
    return () => { request.alive = false; abort.abort(); wake.current?.(); stopLoop('reel-spin'); stopLoop('special-reel-spin'); stopLoop('ambience') }
  }, [api, reload, stopLoop])
  useEffect(() => { props.onSpinStateChange?.(busy); return () => props.onSpinStateChange?.(false) }, [busy, props.onSpinStateChange])
  const pending = api?.pending()
  const wager = pending?.wagerPoints ?? (state?.freeSpinsRemaining ? state.freeSpinWagerPoints! : experience.rules.wagerOptions[wagerIndex])
  async function pause(ms: number) {
    if (skip.current || reduced) return
    await new Promise<void>(resolve => {
      const done = () => { clearTimeout(timer); wake.current = null; resolve() }
      const timer = window.setTimeout(done, ms); wake.current = done
    })
  }
  async function spin() {
    if (spinning.current) { skip.current = true; wake.current?.(); return }
    if (!api || active.current.api !== api || !ready || !state || help || settings) return
    const request = active.current, saved = api.pending()
    const free = saved?.useFreeSpin ?? state.freeSpinsRemaining > 0
    if (!saved && !free && balance < wager * .25) { setAuto(false); setError('Add slot credits before placing this wager.'); return }
    spinning.current = true; skip.current = false; setBusy(true); setError(null); setResult(null); setStepIndex(0)
    stopResultCues(); startLoop('ambience'); playCue('lever-pull')
    const loop = free ? 'special-reel-spin' : 'reel-spin'; startLoop(loop)
    const current = () => request.alive && active.current === request
    try {
      const next = saved ? await api.resume() : await api.spin(wager, free)
      if (!current()) return
      setReels(next.reels.map(column => [...column])); stopLoop(loop); playCue('reel-stop')
      await pause(300)
      if (!current()) return
      if (next.olympusTumble) for (const [index, step] of next.olympusTumble.steps.entries()) {
        if (!current() || skip.current || reduced) break
        setStepIndex(index + 1); setCleared(step.cleared); setDropRows([]); setStepAward(step.amountPoints)
        playCue('low-win'); await pause(520)
        if (!current()) return
        setCleared([]); setStepAward(0); setReels(step.reels.map(column => [...column])); setDropRows(step.dropRows)
        playCue('reel-stop'); await pause(420)
      }
      if (current()) finish(next)
    } catch (issue) {
      if (current()) { setAuto(false); setError(issue instanceof Error ? issue.message : 'Reconnect to recover the saved Olympian round.') }
    } finally {
      if (current()) { spinning.current = false; setBusy(false); setCleared([]); setDropRows([]); stopLoop(loop) }
    }
  }
  spinRef.current = () => { void spin() }
  useEffect(() => {
    if (!auto || busy || !ready || help || settings || error) return
    const timer = window.setTimeout(() => spinRef.current(), 1000)
    return () => clearTimeout(timer)
  }, [auto, busy, ready, help, settings, error, result])
  const onNavigate = () => { setAuto(false); skip.current = true; wake.current?.() }
  const openHelp = () => { onNavigate(); setHelp(true) }
  const keys = useRef<(event: KeyboardEvent) => void>(() => {})
  keys.current = event => {
    if (event.repeat || event.altKey || event.ctrlKey || event.metaKey || (event.target as Element)?.closest?.('button,input,select,textarea,a,[contenteditable=true]')) return
    if (event.key.toLowerCase() === 'm') audio.toggleMuted()
    else if (!help && !settings && event.code === 'Space') { event.preventDefault(); spinRef.current() }
    else if (!help && !settings && event.key === '?') openHelp()
  }
  useEffect(() => { const handler = (event: KeyboardEvent) => keys.current(event); window.addEventListener('keydown', handler); return () => window.removeEventListener('keydown', handler) }, [])
  const clearing = new Set(cleared.map(cell => `${cell.reel}:${cell.row}`))
  const columns = reels.length, rows = reels[0]?.length ?? 5
  const content = <div className="slots-page olympus-game" data-slot-theme={experience.cabinet.id} data-game-busy={busy || undefined} data-expanded={reels[0]?.length === 6 || undefined}
    style={{ '--olympus-backdrop': `url("${experience.cabinet.pageBackdropImage}")`, '--slot-symbol-rows': rows } as CSSProperties}>
    <header className="olympus-game__brand"><h1 className="olympus-game__accessible">Gods of Olympus</h1>
      <img src={experience.cabinet.titleArtwork?.src} width={640} height={328} alt="" /><p>Claim the Divine Tribute</p></header>
    <main className="olympus-game__temple">
      <div className="olympus-game__cabinet" aria-busy={busy}>
        <SlotMachine cabinetTheme={experience.cabinet} reelCount={columns} renderReel={column => <div className="olympus-game__column">
          {reels[column].map((symbol, row) => <div key={`${stepIndex}:${row}:${dropRows[column]?.[row] ?? 0}`}
            className={`gods-tumble__cell ${clearing.has(`${column}:${row}`) ? 'gods-tumble__cell--clear' : dropRows[column]?.[row] ? 'gods-tumble__cell--drop' : ''}`}
            style={{ '--gods-tumble-rows': dropRows[column]?.[row] ?? 0 } as CSSProperties}>
            <SlotSymbol symbol={symbol} symbolSet={experience.symbols} reelIndex={column} rowIndex={row} animated={false} highlighted={clearing.has(`${column}:${row}`)} />
          </div>)}
        </div>} reelOverlay={cleared.length > 0 && stepAward > 0 ? <GodsTumbleAwardBadge positions={cleared} columns={columns} rows={rows} amount={money(stepAward * .25)} /> : null} />
      </div>
      <SidecarSpinControls activeWager={wager * .25} wagerIndex={wagerIndex} wagerOptions={experience.rules.wagerOptions}
        onSelectWager={index => { if (!spinning.current && !pending && !state?.freeSpinsRemaining && index >= 0 && index < experience.rules.wagerOptions.length) setWagerIndex(index) }}
        disabled={!ready || help || settings} wagerLocked={Boolean(pending || state?.freeSpinsRemaining)} freeSpinsRemaining={state?.freeSpinsRemaining ?? 0}
        isAutoSpinning={auto} onAutoSpinChange={setAuto} isFreeSpinBadgePopping={false} isSpinning={busy} isStopRequested={skip.current}
        onSpin={() => { void spin() }} pointValueInCents={25} showFreeSpinBadge={Boolean(state?.freeSpinsRemaining)} specialControlsActive={false} specialRoundLabel="Olympian Trial" />
      <div className="olympus-game__score" aria-live="polite">
        {error ? <p role="alert">{error} <button onClick={() => setReload(value => value + 1)} disabled={busy}>Reconnect / recover</button></p>
          : !ready ? <p>Opening your saved Olympian progress…</p> : busy ? <p>{stepIndex ? `Tumble ${stepIndex} · ${money(stepAward * .25)}` : 'Seeking divine tribute…'}</p>
          : result ? <p>{result.payout.totalPoints ? `Divine tribute · ${money(result.payout.totalPoints * .25)}` : 'The gods await your next spin.'}</p>
          : <p>Eight matching symbols anywhere. Every tumble brings another chance.</p>}
        {result?.olympusTumble && <p>{result.olympusTumble.steps.length} tumbles · {result.olympusTumble.multiplier ?? 1}× Zeus multiplier</p>}
        {state?.freeSpinsRemaining ? <p>{state.freeSpinsRemaining} saved Olympian Trials · wager locked at {money(wager * .25)} · {state.freeSpinFeatureMode ?? 'Classic Trial'}</p> : null}
      </div>
      <section className="olympus-game__multipliers" aria-label="Zeus multiplier coins">{Object.entries(GODS_TUMBLE_MULTIPLIERS).map(([id, value]) => <span key={id}><img src={experience.symbols.definitions[id]?.image} alt="" />{value}×</span>)}<p>Coins add together on a winning tumble · up to 25×</p></section>
      {state?.sealCollections.some(track => track.count > 0) && <section className="olympus-game__altars" aria-label="Saved divine collections">{state.sealCollections.map(track => <p key={track.sealId}>{experience.features.collections?.entries.find(entry => entry.id === track.sealId)?.label} · {track.count}/{track.requiredCount}</p>)}</section>}
      <footer><button onClick={openHelp}>How to win</button><button onClick={() => { onNavigate(); setSettings(true) }}>Sound</button><span>Balance {money(balance)}</span></footer>
    </main>
    <WinHelpDialog isOpen={help} theme="gods-of-olympus" title="The Olympian Guide" eyebrow="Gods of Olympus" closeButtonRef={helpClose} onClose={() => setHelp(false)}>
      <article className="olympus-game__guide"><h2>Tributes fall. Zeus multiplies.</h2><div><section><h3>Eight anywhere</h3><p>Paid spins open six columns and five rows. Eight or more matching symbols anywhere on the board pay. Ten or twelve matching symbols increase the award.</p></section>
        <section><h3>Watch the tumble</h3><p>Paying symbols clear, survivors drop, and new symbols arrive. Use Spin / stop to reveal the final board immediately.</p></section>
        <section><h3>Zeus coins</h3><p>2×, 3×, 5× and 10× coins add together. When the spin wins, the collected multiplier applies to its total tumble prize, capped at 25×.</p></section>
        <section><h3>Your saved Trials</h3><p>Existing free Trials keep their funded wager and divine powers. Their five-reel board can expand to six rows. Finish them before placing another paid wager.</p></section></div>
        <p>Choose your wager before spinning. Prizes and balance changes are confirmed by the game server.</p></article>
    </WinHelpDialog>
    <AudioSettingsDialog isOpen={settings} theme="gods-of-olympus" onClose={() => setSettings(false)} preferences={audio.preferences}
      onToggleMuted={audio.toggleMuted} onToggleResultsOnly={audio.toggleResultsOnly} onVolumeChange={audio.setVolume} />
  </div>
  return props.renderShell ? props.renderShell({ balance, onNavigate, children: content }) : content
}
