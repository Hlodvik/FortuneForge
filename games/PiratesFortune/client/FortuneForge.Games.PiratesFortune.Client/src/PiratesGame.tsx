import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { AudioSettingsDialog, SlotMachine, SlotSymbol, SidecarSpinControls, WinHelpDialog, usePrefersReducedMotion, useSlotAudio } from '@fortuneforge/slot-client-core'
import { createPiratesApi, type PiratesFetch, type PiratesResult, type PiratesState } from './api'
import { PIRATES_FORTUNE_EXPERIENCE_SET as experience } from './manifest'
import { TreasureCoveIntroduction } from './TreasureCoveIntroduction'
import { TREASURE_COVE_TITLE } from './identity'
import '@fortuneforge/slot-client-core/styles/machine.css'
import '@fortuneforge/slot-client-core/styles/machineOverrides.css'
import '@fortuneforge/slot-client-core/styles/dialogs.css'
import '@fortuneforge/slot-client-core/styles/sidecarSpinControls.css'
import './piratesGame.css'

export type PiratesGameProps = {
  account?: { userId: string; balances: { slotsCredits: number } }
  authenticatedFetch?: PiratesFetch
  renderShell?: (props: { balance: number; onNavigate: () => void; children: ReactNode }) => ReactNode
  onSpinStateChange?: (busy: boolean) => void
}
const money = (points: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(points)
const gemNames = { sync: 'Ruby · Matching Reel', rows: 'Lapis · Extra Rows', paw: 'Orange · Purse Hauls', rand: 'Emerald · Multiplier Column' }

/** Treasure Cove's complete paid client; only neutral account, transport and shell ports are injected. */
export function PiratesGame(props: PiratesGameProps) {
  const { account, authenticatedFetch } = props
  const api = useMemo(() => account && authenticatedFetch ? createPiratesApi(authenticatedFetch, account.userId) : null, [account?.userId, authenticatedFetch])
  const audio = useSlotAudio(experience.sounds)
  const reduced = usePrefersReducedMotion()
  const [state, setState] = useState<PiratesState | null>(null)
  const [result, setResult] = useState<PiratesResult | null>(null)
  const [reels, setReels] = useState<string[][]>(() => experience.rules.initialReels.map(reel => [...reel]))
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [wagerIndex, setWagerIndex] = useState(5)
  const [busy, setBusy] = useState(false)
  const [auto, setAuto] = useState(false)
  const [help, setHelp] = useState(false)
  const [settings, setSettings] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [ready, setReady] = useState(false)
  const current = useRef({ api, alive: false })
  const spinning = useRef(false)
  const reveal = useRef<(() => void) | null>(null)
  const latestSpin = useRef<() => void>(() => {})
  const helpClose = useRef<HTMLButtonElement>(null)
  const { playCue, playSequence, startLoop, stopLoop, stopResultCues } = audio

  function restore(next: PiratesState) { setState(next); if (next.slotsCreditsBalance !== null) setBalance(next.slotsCreditsBalance) }
  function show(next: PiratesResult) {
    restore(next); setResult(next); setReels(next.reels.map(column => [...column]))
    const kind = next.freeSpinsAwarded > 0 ? 'bonus' : next.payout.totalPoints >= next.wagerPoints * 5 ? 'five' : next.payout.totalPoints > 0 ? 'single-three' : 'no-win'
    playSequence((next.isFreeSpin ? experience.sounds.events.specialResults : experience.sounds.events.results)?.[kind] ?? [])
  }
  useEffect(() => {
    const request = { api, alive: true }; current.current = request
    const abort = new AbortController()
    spinning.current = false; reveal.current?.(); setBusy(false); setAuto(false); setReady(false); setState(null); setResult(null); setError(null)
    setReels(experience.rules.initialReels.map(column => [...column])); setBalance(account?.balances.slotsCredits ?? 0)
    if (api) void (async () => {
      try {
        const next = await api.state(abort.signal)
        if (!request.alive) return
        restore(next)
        if (api.pending()) {
          spinning.current = true; setBusy(true)
          const recovered = await api.resume()
          if (!request.alive) return
          show(recovered)
          const latest = await api.state(abort.signal)
          if (!request.alive) return
          restore(latest)
        }
        setReady(true)
      } catch (issue) { if (request.alive) setError(issue instanceof Error ? issue.message : 'Your voyage could not be restored.') }
      finally { if (request.alive) { spinning.current = false; setBusy(false) } }
    })()
    return () => { request.alive = false; abort.abort(); reveal.current?.(); stopLoop('reel-spin'); stopLoop('special-reel-spin'); stopLoop('ambience') }
  }, [api, reload, stopLoop])
  useEffect(() => { props.onSpinStateChange?.(busy); return () => props.onSpinStateChange?.(false) }, [busy, props.onSpinStateChange])
  const pending = api?.pending()
  const wager = pending?.wagerPoints ?? (state?.freeSpinsRemaining ? state.freeSpinWagerPoints! : experience.rules.wagerOptions[wagerIndex])
  const wagerRand = wager * .25
  async function spin(boost = false) {
    if (spinning.current) { reveal.current?.(); return }
    if (!api || current.current.api !== api || !ready || !state || help || settings) return
    const request = current.current
    const saved = api.pending()
    const free = saved?.useFreeSpin ?? state.freeSpinsRemaining > 0
    if (!saved && !free && balance < wagerRand) { setError('Add slot credits before placing this wager.'); setAuto(false); return }
    spinning.current = true; setBusy(true); setError(null); setResult(null)
    stopResultCues(); startLoop('ambience'); playCue('lever-pull')
    const loop = free ? 'special-reel-spin' : 'reel-spin'; startLoop(loop)
    try {
      const next = saved ? await api.resume() : await api.spin(wager, free, boost && !free)
      if (!request.alive || current.current !== request) return
      if (!reduced) await new Promise<void>(resolve => {
        const finish = () => { clearTimeout(timer); reveal.current = null; resolve() }
        const timer = window.setTimeout(finish, 450); reveal.current = finish
      })
      if (!request.alive || current.current !== request) return
      playCue(free ? 'special-reel-stop' : 'reel-stop'); show(next)
    } catch (issue) {
      if (request.alive && current.current === request) { setAuto(false); setError(issue instanceof Error ? issue.message : 'Resume the saved voyage.') }
    } finally {
      if (request.alive && current.current === request) { spinning.current = false; setBusy(false); stopLoop(loop) }
    }
  }
  latestSpin.current = () => { void spin() }
  useEffect(() => {
    if (!auto || busy || !ready || help || settings || error) return
    const timer = window.setTimeout(() => latestSpin.current(), 1000)
    return () => clearTimeout(timer)
  }, [auto, busy, ready, help, settings, error, result])
  const onNavigate = () => { setAuto(false); reveal.current?.() }
  const openHelp = () => { onNavigate(); setHelp(true) }
  const keys = useRef<(event: KeyboardEvent) => void>(() => {})
  keys.current = event => {
    if (event.repeat || event.altKey || event.ctrlKey || event.metaKey || (event.target as Element)?.closest?.('button,input,select,textarea,a,[contenteditable=true]')) return
    if (event.key.toLowerCase() === 'm') audio.toggleMuted()
    else if (!help && !settings && event.code === 'Space') { event.preventDefault(); latestSpin.current() }
    else if (!help && !settings && event.key === '?') openHelp()
  }
  useEffect(() => { const handler = (event: KeyboardEvent) => keys.current(event); window.addEventListener('keydown', handler); return () => window.removeEventListener('keydown', handler) }, [])
  const winners = new Set(result?.payout.paylines.flatMap(line => line.matches.flatMap(paid => paid.match.positions.map(cell => `${cell.reel}:${cell.row}`))) ?? [])
  const content = <div className="slots-page treasure-cove-game" data-slot-theme={experience.cabinet.id} data-game-busy={busy || undefined} data-expanded={reels[0]?.length === 6 || undefined}
    style={{ '--cove-backdrop': `url("${experience.cabinet.pageBackdropImage}")`, '--slot-symbol-rows': reels[0]?.length ?? 4 } as CSSProperties}>
    <header className="treasure-cove-game__brand"><p>Fortune Forge · Pirates’ Fortune</p><h1>{TREASURE_COVE_TITLE}</h1><span>Plunder the Moonlit Cove</span></header>
    <main className="treasure-cove-game__deck">
      <div className="treasure-cove-game__cabinet" aria-busy={busy} data-spinning={busy || undefined}>
        <SlotMachine cabinetTheme={experience.cabinet} reelCount={5} renderReel={column => <div className="treasure-cove-game__column">
          {reels[column].map((symbol, row) => <SlotSymbol key={row} symbol={symbol} symbolSet={experience.symbols} reelIndex={column} rowIndex={row}
            animated={!reduced && !busy} highlighted={!busy && winners.has(`${column}:${row}`)} />)}
        </div>} />
      </div>
      <SidecarSpinControls activeWager={wagerRand} wagerIndex={wagerIndex} wagerOptions={experience.rules.wagerOptions}
        onSelectWager={index => { if (!spinning.current && !pending && !state?.freeSpinsRemaining && index >= 0 && index < experience.rules.wagerOptions.length) setWagerIndex(index) }}
        disabled={!ready || help || settings} wagerLocked={Boolean(pending || state?.freeSpinsRemaining)} freeSpinsRemaining={state?.freeSpinsRemaining ?? 0}
        isAutoSpinning={auto} onAutoSpinChange={setAuto} isFreeSpinBadgePopping={false} isSpinning={busy} isStopRequested={false}
        onSpin={() => { void spin() }} pointValueInCents={25} showFreeSpinBadge={Boolean(state?.freeSpinsRemaining)} specialControlsActive={false} specialRoundLabel="Island Search" />
      <div className="treasure-cove-game__status" aria-live="polite">
        {error ? <p role="alert">{error} <button onClick={() => setReload(value => value + 1)} disabled={busy}>Reconnect / recover</button></p>
          : !ready ? <p>Opening your saved voyage…</p> : busy ? <p>Searching the moonlit cove…</p>
          : result ? <p>{result.payout.totalPoints > 0 ? `Treasure found · ${money(result.payout.totalPoints * .25)}` : 'The sea keeps its secrets. Sail again when ready.'}
            {result.moneyGrabPoints > 0 && <span> Doubloon haul · {money(result.moneyGrabPoints * .25)}</span>}
            {result.bananaBonusPoints > 0 && <span> Powder-keg volley · {money(result.bananaBonusPoints * .25)}</span>}</p>
          : <p>Follow the maps. Fill the chests. Bring home the haul.</p>}
        {state?.freeSpinsRemaining ? <p>{state.freeSpinsRemaining} Island Search rounds · {state.freeSpinFeatureMode?.split('-').map(mode => gemNames[mode as keyof typeof gemNames]).join(' + ') || 'Treasure Map bonus'} · wager locked at {money(wagerRand)}</p> : null}
      </div>
      <section className="treasure-cove-game__chests" aria-label="Treasure gem collections">
        {experience.features.collections?.entries.map(entry => {
          const progress = state?.sealCollections.find(track => track.sealId === entry.id)
          return <article key={entry.id}><img src={experience.symbols.definitions[entry.symbol]?.image} alt="" /><h2>{entry.shortLabel}</h2>
            <meter min={0} max={progress?.requiredCount ?? entry.requiredCount} value={progress?.count ?? 0} aria-label={`${entry.shortLabel} gems`} />
            <p>{progress?.count ?? 0}/{progress?.requiredCount ?? entry.requiredCount} · {entry.displayLabel}</p></article>
        })}
      </section>
      <footer><button onClick={openHelp}>How to win</button><button onClick={() => { onNavigate(); setSettings(true) }}>Sound</button>
        <button disabled={!ready || busy || Boolean(pending) || Boolean(state?.freeSpinsRemaining) || (state?.specialPointsBalance ?? 0) < experience.rules.specialBoostCost}
          onClick={() => { void spin(true) }}>Boost · {state?.specialPointsBalance ?? 0} special points</button>
        <span>Balance {money(balance)}</span></footer>
    </main>
    <WinHelpDialog isOpen={help} theme="treasure-cove" title={`How to win · ${TREASURE_COVE_TITLE}`} eyebrow="A captain’s guide" closeButtonRef={helpClose} onClose={() => setHelp(false)}>
      <TreasureCoveIntroduction onClose={() => setHelp(false)} /></WinHelpDialog>
    <AudioSettingsDialog isOpen={settings} theme="treasure-cove" onClose={() => setSettings(false)} preferences={audio.preferences}
      onToggleMuted={audio.toggleMuted} onToggleResultsOnly={audio.toggleResultsOnly} onVolumeChange={audio.setVolume} />
  </div>
  return props.renderShell ? props.renderShell({ balance, onNavigate, children: content }) : content
}
