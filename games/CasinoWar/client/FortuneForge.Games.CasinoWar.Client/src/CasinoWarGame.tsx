import { useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CasinoWarGatewayError,
  type CasinoWarCard,
  type CasinoWarDecision,
  type CasinoWarGateway,
  type CasinoWarRound,
  type CasinoWarStatus,
} from './contracts'
import { HttpCasinoWarGateway } from './httpCasinoWarGateway'
import chip1 from './assets/chip-1.png'
import chip2 from './assets/chip-2.png'
import chip5 from './assets/chip-5.png'
import chip10 from './assets/chip-10.png'
import chip25 from './assets/chip-25.png'
import chip50 from './assets/chip-50.png'
import chipLay from './assets/chip-lay-1.ogg'
import './casinoWar.css'
import './casinoWarViewport.css'
import './casinoWarDeal.css'
import './casinoWarBoard.css'

export type CasinoWarGameProps = Readonly<{
  gateway?: CasinoWarGateway
  playerId?: string
  currencySymbol?: string
  onBalanceChange?: (balance: number) => void
  renderCardFace?: (card: CasinoWarCard) => ReactNode
}>

const defaultGateway = new HttpCasinoWarGateway()
const chipValues = [1, 2, 5, 10, 25, 50] as const
const chipArt: Record<number, string> = { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25, 50: chip50 }

type StoredPendingAction =
  | Readonly<{ operation: 'opening'; idempotencyKey: string; primaryStake: number; tieStake: number }>
  | Readonly<{ operation: 'decision'; idempotencyKey: string; roundId: string; decision: CasinoWarDecision }>

export function CasinoWarGame({ gateway = defaultGateway, playerId, currencySymbol = 'R', onBalanceChange, renderCardFace }: CasinoWarGameProps) {
  const [status, setStatus] = useState<CasinoWarStatus | null>(null)
  const [round, setRound] = useState<CasinoWarRound | null>(null)
  const [primaryStake, setPrimaryStake] = useState(0)
  const [tieStake, setTieStake] = useState(0)
  const [mainChips, setMainChips] = useState<number[]>([])
  const [tieChips, setTieChips] = useState<number[]>([])
  const [selectedChip, setSelectedChip] = useState<number>(1)
  const [chipPickerOpen, setChipPickerOpen] = useState(false)
  const [soundMuted, setSoundMuted] = useState(readSoundMuted)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [tableUnavailable, setTableUnavailable] = useState(false)
  const [statusLoadAttempt, setStatusLoadAttempt] = useState(0)
  const [recovery, setRecovery] = useState<'ready' | 'recovering' | 'failed'>('ready')
  const [recoveryAttempt, setRecoveryAttempt] = useState(0)
  const [revealStage, setRevealStage] = useState(0)
  const [rulesOpen, setRulesOpen] = useState(false)
  const [lastBalance, setLastBalance] = useState<number | null>(null)
  const openingRequestKey = useRef<string | null>(null)
  const decisionRequestKey = useRef<string | null>(null)
  const pendingOpeningStakes = useRef<Readonly<{ primaryStake: number; tieStake: number }> | null>(null)
  const pendingDecision = useRef<CasinoWarDecision | null>(null)
  const rulesTrigger = useRef<HTMLButtonElement>(null)
  const rulesDialog = useRef<HTMLElement>(null)
  const rulesClose = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!round) { setRevealStage(0); return }
    if (round.phase === 'completed' && round.decision === 'surrender') { setRevealStage(2); return }
    const hasWarCards = round.phase === 'completed' && (round.playerWarCard || round.dealerWarCard)
    setRevealStage(hasWarCards ? 2 : 0)
    const targets = hasWarCards ? [3, 4] : [1, 2]
    const timers = targets.map((target, index) => window.setTimeout(() => setRevealStage(target), 180 + index * 280))
    return () => timers.forEach(timer => window.clearTimeout(timer))
  }, [round?.phase, round?.roundId])

  useEffect(() => {
    const controller = new AbortController()
    void gateway.getStatus(controller.signal).then(nextStatus => {
      setStatus(nextStatus)
      setPrimaryStake(current => current === 0 || (current >= nextStatus.minimumPrimaryStake && current <= nextStatus.maximumPrimaryStake && isIncrementAligned(current, nextStatus.minimumPrimaryStake, nextStatus.stakeIncrement)) ? current : 0)
      setTableUnavailable(!nextStatus.available)
      setError(nextStatus.available ? null : 'This table is temporarily unavailable. Please choose another game.')
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      const unavailable = isTableUnavailable(reason)
      setTableUnavailable(unavailable)
      setError(unavailable ? 'This table is temporarily unavailable. Please choose another game.' : messageForError(reason))
    })
    return () => controller.abort()
  }, [gateway, statusLoadAttempt])

  useEffect(() => {
    const pendingAction = playerId ? readPendingAction(playerId) : null
    if (pendingAction) {
      if (pendingAction.operation === 'opening') {
        setPrimaryStake(pendingAction.primaryStake)
        setTieStake(pendingAction.tieStake)
        setMainChips(chipsForAmount(pendingAction.primaryStake))
        setTieChips(chipsForAmount(pendingAction.tieStake))
      }
      const controller = new AbortController()
      setRecovery('recovering')
      const request = pendingAction.operation === 'opening'
        ? gateway.createRound(pendingAction.primaryStake, pendingAction.tieStake, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey })
        : gateway.decide(pendingAction.roundId, pendingAction.decision, { signal: controller.signal, idempotencyKey: pendingAction.idempotencyKey })
      void request.then(nextRound => {
        clearPendingAction(playerId)
        if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
        else clearStoredRoundId(playerId)
        setRound(nextRound)
        setRecovery('ready')
        setError(null)
      }).catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return
        setRecovery('failed')
        setError('Your pending Casino War request could not be restored. Retry restoration before starting another round.')
      })
      return () => controller.abort()
    }
    const storedRoundId = playerId ? readStoredRoundId(playerId) : null
    if (!storedRoundId) {
      setRecovery('ready')
      return undefined
    }
    const controller = new AbortController()
    setRecovery('recovering')
    void gateway.getRound(storedRoundId, controller.signal).then(nextRound => {
      if (nextRound.phase === 'awaiting-tie-decision') setRound(nextRound)
      else clearStoredRoundId(playerId)
      setRecovery('ready')
      setError(null)
    }).catch(reason => {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      if (reason instanceof CasinoWarGatewayError && reason.status === 404) {
        clearStoredRoundId(playerId)
        setRecovery('ready')
        setError(null)
        return
      }
      setRecovery('failed')
      setError('Your pending Casino War request could not be restored. Retry restoration before starting another round.')
    })
    return () => controller.abort()
  }, [gateway, onBalanceChange, playerId, recoveryAttempt])

  useEffect(() => {
    if (!round) return
    setPrimaryStake(round.primaryStake)
    setTieStake(round.tieStake)
    setMainChips(chips => chipTotal(chips) === round.primaryStake ? chips : chipsForAmount(round.primaryStake))
    setTieChips(chips => chipTotal(chips) === round.tieStake ? chips : chipsForAmount(round.tieStake))
  }, [round])

  const balance = round?.balance ?? lastBalance ?? status?.balance ?? null
  const displayedBalance = lastBalance ?? status?.balance ?? null
  const hasWarCards = Boolean(round?.playerWarCard || round?.dealerWarCard)
  const revealed = round !== null && revealStage >= (hasWarCards ? 4 : 2)
  useEffect(() => {
    if (!round || !revealed) return
    setLastBalance(round.balance)
    onBalanceChange?.(round.balance)
  }, [onBalanceChange, revealed, round])

  useEffect(() => {
    if (!rulesOpen) return
    rulesClose.current?.focus()
    const closeRules = (returnFocus: boolean) => {
      setRulesOpen(false)
      if (returnFocus) window.requestAnimationFrame(() => rulesTrigger.current?.focus())
    }
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') closeRules(true) }
    const onPointerDown = (event: PointerEvent) => {
      if (!rulesDialog.current?.contains(event.target as Node) && event.target !== rulesTrigger.current) closeRules(false)
    }
    document.addEventListener('keydown', onKeyDown)
    document.addEventListener('pointerdown', onPointerDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('pointerdown', onPointerDown)
    }
  }, [rulesOpen])

  const primaryIsValid = status !== null && primaryStake >= status.minimumPrimaryStake &&
    primaryStake <= status.maximumPrimaryStake && isIncrementAligned(primaryStake, status.minimumPrimaryStake, status.stakeIncrement)
  const tieIsValid = status !== null && tieStake >= 0 && tieStake <= status.maximumTieStake && Number.isFinite(tieStake) &&
    (tieStake === 0 || isIncrementAligned(tieStake, 0, status.stakeIncrement))
  const stakesAffordable = balance !== null && primaryStake + tieStake <= balance
  const canPlaceChips = !round && !busy && recovery === 'ready' && Boolean(status?.available) && pendingOpeningStakes.current === null
  const availableChips = chipValues.filter(value => status && isIncrementAligned(value, 0, status.stakeIncrement))
  const pickerChips = availableChips.length ? availableChips : chipValues
  const canPlaceSelectedChip = (kind: 'main' | 'tie') => {
    if (!canPlaceChips || !status || balance === null || !pickerChips.includes(selectedChip as typeof chipValues[number])) return false
    const current = kind === 'main' ? primaryStake : tieStake
    const cap = kind === 'main' ? status.maximumPrimaryStake : status.maximumTieStake
    return roundStake(current + selectedChip) <= cap && roundStake(primaryStake + tieStake + selectedChip) <= balance
  }

  const placeChip = (kind: 'main' | 'tie') => {
    if (!canPlaceSelectedChip(kind)) return
    const current = kind === 'main' ? primaryStake : tieStake
    if (kind === 'main') { setMainChips(chips => [...chips, selectedChip]); setPrimaryStake(roundStake(current + selectedChip)) }
    else { setTieChips(chips => [...chips, selectedChip]); setTieStake(roundStake(current + selectedChip)) }
    playChipLay(soundMuted)
  }

  const removeChip = (kind: 'main' | 'tie') => {
    if (!canPlaceChips) return
    const chips = kind === 'main' ? mainChips : tieChips
    const last = chips.at(-1)
    if (last === undefined) return
    if (kind === 'main') { setMainChips(chips.slice(0, -1)); setPrimaryStake(value => roundStake(value - last)) }
    else { setTieChips(chips.slice(0, -1)); setTieStake(value => roundStake(value - last)) }
  }

  const act = async (operation: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try { await operation() } catch (reason) { setError(messageForError(reason)) } finally { setBusy(false) }
  }

  const deal = () => {
    if (!status?.available || busy || recovery !== 'ready' || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid || !stakesAffordable))) return
    const idempotencyKey = openingRequestKey.current ??= createRequestKey('opening')
    const stakes = pendingOpeningStakes.current ?? { primaryStake, tieStake }
    pendingOpeningStakes.current = stakes
    storePendingAction(playerId, { operation: 'opening', idempotencyKey, primaryStake: stakes.primaryStake, tieStake: stakes.tieStake })
    void act(async () => {
      const nextRound = await gateway.createRound(stakes.primaryStake, stakes.tieStake, { idempotencyKey })
      openingRequestKey.current = null
      pendingOpeningStakes.current = null
      clearPendingAction(playerId)
      if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
      setRound(nextRound)
    })
  }

  const decide = (decision: CasinoWarDecision) => {
    if (!round || round.phase !== 'awaiting-tie-decision' || busy) return
    if (decision === 'go-to-war' && pendingDecision.current === null && round.primaryStake > round.balance) return
    if (pendingDecision.current && pendingDecision.current !== decision) {
      setError(`Your ${humanize(pendingDecision.current)} decision is still pending. Retry that decision so the table can return the original result.`)
      return
    }
    const idempotencyKey = decisionRequestKey.current ??= createRequestKey('decision')
    pendingDecision.current = decision
    storePendingAction(playerId, { operation: 'decision', idempotencyKey, roundId: round.roundId, decision })
    void act(async () => {
      const nextRound = await gateway.decide(round.roundId, decision, { idempotencyKey })
      decisionRequestKey.current = null
      pendingDecision.current = null
      clearPendingAction(playerId)
      clearStoredRoundId(playerId)
      setRound(nextRound)
    })
  }

  const reset = () => {
    if (busy || recovery !== 'ready' || !round || round.phase !== 'completed' || !revealed) return
    setLastBalance(round.balance)
    setRound(null)
    setPrimaryStake(0)
    setTieStake(0)
    setMainChips([])
    setTieChips([])
    setError(null)
    openingRequestKey.current = null
    decisionRequestKey.current = null
    pendingOpeningStakes.current = null
    pendingDecision.current = null
    clearPendingAction(playerId)
    clearStoredRoundId(playerId)
  }

  useEffect(() => {
    if (!round || round.phase !== 'completed' || !revealed || busy || recovery !== 'ready' || rulesOpen) return
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target
      if (target instanceof Element && target.closest('.ff-casino-war__callout, .ff-casino-war__round-actions')) return
      reset()
    }
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') reset() }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [round, revealed, busy, recovery, rulesOpen])

  const rebet = () => {
    if (!round || round.phase !== 'completed' || busy || recovery !== 'ready' || !status?.available || round.primaryStake + round.tieStake > round.balance) return
    const stakes = { primaryStake: round.primaryStake, tieStake: round.tieStake }
    setPrimaryStake(stakes.primaryStake)
    setTieStake(stakes.tieStake)
    setRound(null)
    const idempotencyKey = createRequestKey('opening')
    openingRequestKey.current = idempotencyKey
    pendingOpeningStakes.current = stakes
    storePendingAction(playerId, { operation: 'opening', idempotencyKey, ...stakes })
    void act(async () => {
      const nextRound = await gateway.createRound(stakes.primaryStake, stakes.tieStake, { idempotencyKey })
      openingRequestKey.current = null
      pendingOpeningStakes.current = null
      clearPendingAction(playerId)
      if (nextRound.phase === 'awaiting-tie-decision') storeRoundId(playerId, nextRound.roundId)
      setRound(nextRound)
    })
  }

  const retryStatus = () => {
    setError(null)
    setTableUnavailable(false)
    setStatusLoadAttempt(attempt => attempt + 1)
  }

  return (
    <main className="ff-casino-war" aria-busy={busy}>
      <section className="ff-casino-war__table" aria-label="Casino War table">
        <div className="ff-casino-war__table-top">
          <button className="ff-casino-war__sound" type="button" aria-label={soundMuted ? 'Turn sound on' : 'Mute sound'} aria-pressed={!soundMuted} onClick={() => { const next = !soundMuted; setSoundMuted(next); writeSoundMuted(next) }}>{soundMuted ? '♪ Off' : '♪ On'}</button>
          <div className="ff-casino-war__felt-title" aria-label="Casino War"><strong><span>C</span>asino <span>W</span>ar</strong><i aria-hidden="true" /></div>
          <button className="ff-casino-war__help" ref={rulesTrigger} type="button" aria-label="How to play Casino War" aria-expanded={rulesOpen} onClick={() => setRulesOpen(true)}>?</button>
        </div>

        <section className="ff-casino-war__arena" aria-live="polite">
          <CardSeat label="Dealer" openingLabel="Dealer opening card" openingCard={round && revealStage >= 2 ? round.dealerOpeningCard : null} warCard={round && revealStage >= 4 ? round.dealerWarCard : null} showBack={!round || revealStage < 2} renderCardFace={renderCardFace} />
          <RoundCallout round={round} revealStage={revealStage} currencySymbol={currencySymbol} onContinue={reset} />
          <CardSeat label="You" openingLabel="Player opening card" openingCard={round && revealStage >= 1 ? round.playerOpeningCard : null} warCard={round && revealStage >= 3 ? round.playerWarCard : null} showBack={!round || revealStage < 1} renderCardFace={renderCardFace} />
        </section>

        <section className="ff-casino-war__bets" aria-label="Casino War bets">
          <BetSpot kind="main" label="Main bet" amount={primaryStake} chips={mainChips} currencySymbol={currencySymbol} placeDisabled={!canPlaceSelectedChip('main')} removeDisabled={!canPlaceChips} onPlace={() => placeChip('main')} onRemove={() => removeChip('main')} />
          <BetSpot kind="tie" label="Tie bet" amount={tieStake} chips={tieChips} currencySymbol={currencySymbol} placeDisabled={!canPlaceSelectedChip('tie')} removeDisabled={!canPlaceChips} onPlace={() => placeChip('tie')} onRemove={() => removeChip('tie')} />
        </section>

        <section className="ff-casino-war__controls">
          <div className="ff-casino-war__balance"><span>Balance</span><strong>{displayedBalance === null ? '—' : formatMoney(displayedBalance, currencySymbol)}</strong></div>
          {!round && <>
            <div className="ff-casino-war__chip-picker" aria-label="Chip selection">
              <button type="button" className="ff-casino-war__chip-step" aria-label="Higher chip value" disabled={!canPlaceChips || pickerChips.indexOf(selectedChip as typeof chipValues[number]) >= pickerChips.length - 1} onClick={() => { setSelectedChip(pickerChips[Math.min(pickerChips.length - 1, pickerChips.indexOf(selectedChip as typeof chipValues[number]) + 1)]); setChipPickerOpen(false) }}><svg viewBox="0 0 32 32" aria-hidden="true"><path d="M5 21 16 10l11 11" /></svg></button>
              <button type="button" className="ff-casino-war__chip-current" aria-label={`Select chip value, currently ${formatMoney(selectedChip, currencySymbol)}`} aria-expanded={chipPickerOpen} disabled={!canPlaceChips} onClick={() => setChipPickerOpen(open => !open)}><img src={chipArt[selectedChip]} alt="" /></button>
              <button type="button" className="ff-casino-war__chip-step" aria-label="Lower chip value" disabled={!canPlaceChips || pickerChips.indexOf(selectedChip as typeof chipValues[number]) <= 0} onClick={() => { setSelectedChip(pickerChips[Math.max(0, pickerChips.indexOf(selectedChip as typeof chipValues[number]) - 1)]); setChipPickerOpen(false) }}><svg viewBox="0 0 32 32" aria-hidden="true"><path d="M5 11 16 22l11-11" /></svg></button>
              {chipPickerOpen && <div className="ff-casino-war__chip-options" aria-label="Chip values">{pickerChips.map(value => <button type="button" key={value} aria-label={`Select ${formatMoney(value, currencySymbol)} chip`} onClick={() => { setSelectedChip(value); setChipPickerOpen(false) }}><img src={chipArt[value]} alt="" /></button>)}</div>}
            </div>
            <button type="button" className="ff-casino-war__primary ff-casino-war__deal" aria-label={busy ? 'Dealing…' : pendingOpeningStakes.current ? 'Retry deal' : 'Deal'} title={pendingOpeningStakes.current ? 'Retry deal' : 'Deal'} disabled={busy || recovery !== 'ready' || !status?.available || (!pendingOpeningStakes.current && (!primaryIsValid || !tieIsValid || !stakesAffordable))} onClick={deal}><CardStackIcon /></button>
          </>}
          {round?.phase === 'awaiting-tie-decision' && revealStage >= 2 && <div className="ff-casino-war__decision" aria-label="Tie decision">
            <button className="ff-casino-war__secondary" aria-label={pendingDecision.current === 'surrender' ? 'Retry Surrender' : 'Surrender'} disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'surrender')} onClick={() => decide('surrender')}><span>{pendingDecision.current === 'surrender' ? 'Retry' : 'Surrender'}</span><small>Return {formatMoney(round.primaryStake / 2, currencySymbol)}</small></button>
            <button className="ff-casino-war__primary" aria-label={pendingDecision.current === 'go-to-war' ? 'Retry Go to War' : 'Go to War'} disabled={busy || (pendingDecision.current !== null && pendingDecision.current !== 'go-to-war') || (pendingDecision.current === null && round.primaryStake > round.balance)} onClick={() => decide('go-to-war')}><span>{busy ? 'Resolving…' : pendingDecision.current === 'go-to-war' ? 'Retry War' : 'Go to War'}</span><small>Add {formatMoney(round.primaryStake, currencySymbol)}</small></button>
          </div>}
          {round?.phase === 'completed' && revealStage >= ((round.playerWarCard || round.dealerWarCard) ? 4 : 2) && <div className="ff-casino-war__round-actions"><button type="button" className="ff-casino-war__primary" disabled={busy || !status?.available || recovery !== 'ready' || round.primaryStake + round.tieStake > round.balance} onClick={rebet}>Deal again</button></div>}
          {recovery === 'recovering' && <span className="ff-casino-war__loading" role="status">Restoring…</span>}
          {recovery === 'failed' && <button className="ff-casino-war__secondary" type="button" onClick={() => setRecoveryAttempt(value => value + 1)}>Retry restoration</button>}
          {!status && error && !tableUnavailable && <button className="ff-casino-war__secondary" type="button" onClick={retryStatus}>Retry connection</button>}
        </section>

        {rulesOpen && <section className="ff-casino-war__rules-dialog" ref={rulesDialog} role="dialog" aria-modal="true" aria-label="How to play Casino War">
          <button type="button" className="ff-casino-war__rules-close" ref={rulesClose} aria-label="Close rules" onClick={() => { setRulesOpen(false); window.requestAnimationFrame(() => rulesTrigger.current?.focus()) }}>×</button>
          <h2>How to play</h2>
          <dl>
            <div><dt>Main bet</dt><dd>Highest card wins and pays 1:1. Ace is high; suits do not matter.</dd></div>
            <div><dt>Tie bet</dt><dd>An opening tie pays 10:1 profit.</dd></div>
            <div><dt>Surrender</dt><dd>On a tie, recover half of the main bet.</dd></div>
            <div><dt>Go to War</dt><dd>Add one matching main bet. Three cards are burned before each War card. A second tie wins.</dd></div>
          </dl>
          {status && <p>Main bet {formatMoney(status.minimumPrimaryStake, currencySymbol)}–{formatMoney(status.maximumPrimaryStake, currencySymbol)} · Tie bet up to {formatMoney(status.maximumTieStake, currencySymbol)}</p>}
        </section>}
      </section>
      {error && <p className="ff-casino-war__error" role="alert">{error}</p>}
    </main>
  )
}

function BetSpot({ kind, label, amount, chips, currencySymbol, placeDisabled, removeDisabled, onPlace, onRemove }: Readonly<{ kind: 'main' | 'tie'; label: string; amount: number; chips: number[]; currencySymbol: string; placeDisabled: boolean; removeDisabled: boolean; onPlace: () => void; onRemove: () => void }>) {
  const visibleChips = chips.slice(-3)
  return <div className={`ff-casino-war__bet-spot ff-casino-war__bet-spot--${kind}`}>
    <span className="ff-casino-war__bet-label">{label}</span>
    <button type="button" className="ff-casino-war__diamond" aria-label={`Place ${kind === 'main' ? 'Main' : 'Tie'} bet chip`} disabled={placeDisabled} onClick={onPlace} />
    {chips.length > 0 && <button type="button" className="ff-casino-war__placed-chips" aria-label={`Remove last ${kind === 'main' ? 'Main' : 'Tie'} bet chip`} disabled={removeDisabled} onClick={onRemove}>
      {visibleChips.map((value, index) => <img src={chipArt[value]} alt="" key={`${kind}-${chips.length - visibleChips.length + index}`} style={{ bottom: `${index * 3}px` }} />)}
      {chips.length > 3 && <span className="ff-casino-war__chip-count">+{chips.length - 3}</span>}
    </button>}
    <strong className="ff-casino-war__bet-amount">{formatMoney(amount, currencySymbol)}</strong>
  </div>
}

function chipsForAmount(amount: number) {
  const chips: number[] = []
  let remaining = roundStake(amount)
  for (const value of [...chipValues].reverse()) {
    while (remaining >= value && chips.length < 100) { chips.push(value); remaining = roundStake(remaining - value) }
  }
  return chips
}

function chipTotal(chips: number[]) { return roundStake(chips.reduce((sum, chip) => sum + chip, 0)) }

function playChipLay(muted: boolean) {
  if (muted || typeof Audio === 'undefined') return
  try { const audio = new Audio(chipLay); audio.volume = .28; void audio.play()?.catch(() => {}) } catch { /* Sound is optional when media is blocked. */ }
}

function readSoundMuted() { try { return localStorage.getItem('fortuneforge:casino-war:muted') === 'true' } catch { return false } }
function writeSoundMuted(muted: boolean) { try { localStorage.setItem('fortuneforge:casino-war:muted', String(muted)) } catch { /* Sound preference can remain local to this visit. */ } }

function CardSeat({ label, openingLabel, openingCard, warCard, showBack, renderCardFace }: Readonly<{ label: string; openingLabel: string; openingCard: CasinoWarCard | null; warCard: CasinoWarCard | null; showBack: boolean; renderCardFace?: (card: CasinoWarCard) => ReactNode }>) {
  return <section className={`ff-casino-war__seat ff-casino-war__seat--${label.toLowerCase()}`} aria-label={`${label} cards`}>
    <strong>{label}</strong>
    <div className="ff-casino-war__hand">
      {openingCard ? <div aria-label={openingLabel}><CardFace card={openingCard} renderCardFace={renderCardFace} /></div> : showBack ? <CardBack label={`${label} card`} /> : null}
      {warCard && <div className="ff-casino-war__war-card" aria-label={`${label} War card`}><CardFace card={warCard} renderCardFace={renderCardFace} /></div>}
    </div>
  </section>
}

function CardBack({ label }: Readonly<{ label: string }>) { return <div className="ff-casino-war__card ff-casino-war__card--back" aria-label={label}><span>FF</span></div> }

function CardFace({ card, renderCardFace }: Readonly<{ card: CasinoWarCard; renderCardFace?: (card: CasinoWarCard) => ReactNode }>) {
  if (renderCardFace) return <div className="ff-casino-war__card ff-casino-war__card--shared">{renderCardFace(card)}</div>
  const symbol = suitSymbol(card.suit)
  const red = card.suit === 'diamonds' || card.suit === 'hearts'
  return <div className={`ff-casino-war__card${red ? ' is-red' : ''}`} aria-label={`${card.rank} of ${card.suit}`}><span className="ff-casino-war__corner">{shortRank(card.rank)}<small>{symbol}</small></span><span className="ff-casino-war__suit" aria-hidden="true">{symbol}</span><span className="ff-casino-war__corner ff-casino-war__corner--reverse">{shortRank(card.rank)}<small>{symbol}</small></span></div>
}

function RoundCallout({ round, revealStage, currencySymbol, onContinue }: Readonly<{ round: CasinoWarRound | null; revealStage: number; currencySymbol: string; onContinue: () => void }>) {
  if (!round) return <div className="ff-casino-war__crest" aria-hidden="true"><span>W</span></div>
  if (round.phase === 'awaiting-tie-decision') return revealStage >= 2 ? <div className="ff-casino-war__callout is-tie"><strong>Tie</strong>{round.tieSettlement && <small>Tie bet {formatSignedMoney(round.tieSettlement.profit, currencySymbol)}</small>}</div> : null
  const neededStage = (round.playerWarCard || round.dealerWarCard) ? 4 : 2
  if (revealStage < neededStage) return null
  const net = (round.primarySettlement?.profit ?? 0) + (round.tieSettlement?.profit ?? 0)
  const title = round.primarySettlement?.disposition === 'surrender' ? 'Surrendered' : net > 0 ? 'You win' : net < 0 ? 'Dealer wins' : 'Push'
  return <div className={`ff-casino-war__callout is-${net > 0 ? 'win' : net < 0 ? 'loss' : 'push'}`}><strong>{title}</strong><b>{formatSignedMoney(net, currencySymbol)}</b>{round.tieSettlement && <small>Tie bet {formatSignedMoney(round.tieSettlement.profit, currencySymbol)}</small>}<button type="button" className="ff-casino-war__continue" onClick={onContinue}>Continue</button></div>
}

function CardStackIcon() {
  return <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><rect x="12" y="11" width="38" height="43" rx="4" fill="#123d29" stroke="#edf2e4" strokeWidth="3" transform="rotate(-11 31 32)"/><rect x="17" y="11" width="38" height="43" rx="4" fill="#f4f5eb" stroke="#52715b" strokeWidth="2.5"/><path d="M36 18l12 15-12 15-12-15z" fill="#879b7c"/><path d="M36 24l7 9-7 9-7-9z" fill="#edf1e2"/></svg>
}

function isIncrementAligned(value: number, minimum: number, increment: number) {
  const steps = (value - minimum) / increment
  return Number.isFinite(steps) && Math.abs(steps - Math.round(steps)) < 1e-9
}
function shortRank(rank: CasinoWarCard['rank']) { return ({ ace: 'A', two: '2', three: '3', four: '4', five: '5', six: '6', seven: '7', eight: '8', nine: '9', ten: '10', jack: 'J', queen: 'Q', king: 'K' } as const)[rank] }
function suitSymbol(suit: CasinoWarCard['suit']) { return ({ clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' } as const)[suit] }
function humanize(value: string) { return value.split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ') }
function formatMoney(value: number, currencySymbol: string) { return `${value < 0 ? '-' : ''}${currencySymbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` }
function formatSignedMoney(value: number, currencySymbol: string) { return `${value > 0 ? '+' : ''}${formatMoney(value, currencySymbol)}` }
function roundStake(value: number) { return Math.round((value + Number.EPSILON) * 100) / 100 }
function messageForError(reason: unknown) { return reason instanceof CasinoWarGatewayError ? reason.message : 'The Casino War table is unavailable.' }
function isTableUnavailable(reason: unknown) { return reason instanceof CasinoWarGatewayError && reason.code === 'casino-war-disabled' }
function createRequestKey(action: 'opening' | 'decision') { const random = typeof crypto?.randomUUID === 'function' ? crypto.randomUUID().replaceAll('-', '') : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`; return `casino-war-${action}-${random}` }
function storedRoundKey(playerId: string) { return `fortuneforge:casino-war:round:${playerId}` }
function pendingActionKey(playerId: string) { return `fortuneforge:casino-war:pending:${playerId}` }
function readStoredRoundId(playerId: string) { try { return sessionStorage.getItem(storedRoundKey(playerId)) } catch { return null } }
function readPendingAction(playerId: string): StoredPendingAction | null {
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(pendingActionKey(playerId)) ?? 'null')
    if (!value || typeof value !== 'object') return null
    const action = value as Record<string, unknown>
    if (action.operation === 'opening' && typeof action.idempotencyKey === 'string' && typeof action.primaryStake === 'number' && typeof action.tieStake === 'number') {
      return { operation: 'opening', idempotencyKey: action.idempotencyKey, primaryStake: action.primaryStake, tieStake: action.tieStake }
    }
    if (action.operation === 'decision' && typeof action.idempotencyKey === 'string' && typeof action.roundId === 'string' && (action.decision === 'surrender' || action.decision === 'go-to-war')) {
      return { operation: 'decision', idempotencyKey: action.idempotencyKey, roundId: action.roundId, decision: action.decision }
    }
  } catch { /* session storage is optional */ }
  return null
}
function storeRoundId(playerId: string | undefined, roundId: string) { if (!playerId) return; try { sessionStorage.setItem(storedRoundKey(playerId), roundId) } catch { /* session storage is optional */ } }
function storePendingAction(playerId: string | undefined, action: StoredPendingAction) { if (!playerId) return; try { sessionStorage.setItem(pendingActionKey(playerId), JSON.stringify(action)) } catch { /* session storage is optional */ } }
function clearStoredRoundId(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(storedRoundKey(playerId)) } catch { /* session storage is optional */ } }
function clearPendingAction(playerId: string | undefined) { if (!playerId) return; try { sessionStorage.removeItem(pendingActionKey(playerId)) } catch { /* session storage is optional */ } }
