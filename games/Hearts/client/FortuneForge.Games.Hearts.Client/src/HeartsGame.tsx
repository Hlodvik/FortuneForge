import { useCallback, useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { useHeartsAudio } from './cardAudio'
import { HeartsGatewayError, type HeartsCard, type HeartsGateway, type HeartsMatch, type HeartsOnboardingState, type HeartsPaidRequest, type HeartsPaidSession, type HeartsPaidTable, type HeartsSeat, type HeartsStatus } from './contracts'
import { cardName, directionLabel, relativeSeat } from './heartsHelpers'
import { getTrialCoach } from './heartsTrialCoach'
import { HeartsTrialCoachBubble } from './HeartsTrialCoachBubble'
import chip1 from './assets/chip-1.png'
import chip2 from './assets/chip-2.png'
import chip5 from './assets/chip-5.png'
import chip10 from './assets/chip-10.png'
import chip25 from './assets/chip-25.png'

export type HeartsGameProps = Readonly<{
  gateway: HeartsGateway
  balance?: number
  onBalanceChange?: (balance: number) => void
  renderCard?: (card: HeartsCard, faceDown?: boolean) => ReactNode
}>
type Panel = 'rules' | 'scores' | 'history' | 'leave' | null
const chipColors = ['#55a126', '#dd6c27', '#2b83c4', '#b92d4f', '#7653b9']
const chipImages: Readonly<Record<number, string>> = { 1: chip1, 2: chip2, 5: chip5, 10: chip10, 25: chip25 }
const money = (value: number) => `R${value.toFixed(2)}`
const backCard: HeartsCard = { code: 'back', rank: 'ace', suit: 'spades', label: 'A♠' }

export function HeartsGame({ gateway, balance = 0, onBalanceChange, renderCard }: HeartsGameProps) {
  const [status, setStatus] = useState<HeartsStatus | null>(null)
  const [paid, setPaid] = useState<HeartsPaidSession | null>(null)
  const [practice, setPractice] = useState<HeartsMatch | null>(null)
  const [mode, setMode] = useState<'lobby' | 'free' | 'paid' | 'trial'>('lobby')
  const [onboarding, setOnboarding] = useState<HeartsOnboardingState | null>(null)
  const [onboardingBusy, setOnboardingBusy] = useState(false)
  const [onboardingError, setOnboardingError] = useState<string | null>(null)
  const [trialExitFailed, setTrialExitFailed] = useState(false)
  const [entry, setEntry] = useState(1)
  const [selected, setSelected] = useState<readonly string[]>([])
  const [busy, setBusy] = useState(false)
  const [connecting, setConnecting] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [recovery, setRecovery] = useState(false)
  const [panel, setPanel] = useState<Panel>(null)
  const [history, setHistory] = useState<readonly HeartsPaidTable[]>([])
  const paidRef = useRef<HeartsPaidSession | null>(null)
  const actionLock = useRef(false)
  const onboardingLock = useRef(false)
  const completedTrialCheck = useRef<string | null>(null)
  const boardRef = useRef<HTMLElement | null>(null)
  const handCardRefs = useRef(new Map<string, HTMLButtonElement>())
  const passButtonRef = useRef<HTMLButtonElement | null>(null)
  const balanceCallback = useRef(onBalanceChange)
  balanceCallback.current = onBalanceChange
  const { muted, toggleMute, play } = useHeartsAudio()
  const table = mode === 'paid' ? paid?.table ?? null : null
  const match = mode === 'paid' ? table?.match ?? null : practice
  const queue = mode === 'paid' ? paid?.queue ?? null : null
  const { shownTrick, revealing } = useTrickReveal(match, play)
  const trialComplete = mode === 'trial' && practice?.isTrial === true && practice.phase === 'complete' && practice.completedTricks.length === 13
  const setupResolved = !gateway.onboarding || onboarding?.shouldAskPlayedBefore === false
  const showEntryGate = mode === 'lobby' && !setupResolved
  const choices = paid?.entryOptions ?? [1, 2, 5, 10, 25]
  const wallet = paid?.balanceCredits ?? balance
  const face = (card: HeartsCard, hidden = false) => renderCard?.(card, hidden) ?? <CardFace card={card} faceDown={hidden} />

  const publishPaid = useCallback((next: HeartsPaidSession) => {
    const current = paidRef.current
    const currentId = current?.queue?.matchId ?? current?.table?.match.matchId
    const nextId = next.queue?.matchId ?? next.table?.match.matchId
    if (current && currentId && currentId === nextId && next.version < current.version) return
    paidRef.current = next
    setPaid(next)
    balanceCallback.current?.(next.balanceCredits)
    if (next.kind === 'idle') setMode(current => current === 'paid' ? 'lobby' : current)
    if (next.notice) setNotice(next.notice)
  }, [])
  const connect = useCallback(async (signal?: AbortSignal) => {
    setConnecting(true)
    setError(null)
    const paidRequest = gateway.paid?.getSession(signal).then(next => {
      if (!signal?.aborted) {
        publishPaid(next)
        if (next.kind === 'queue' || next.kind === 'match') setMode('paid')
      }
      return next
    }) ?? Promise.resolve(null)
    const results = await Promise.allSettled([gateway.getStatus(signal), paidRequest, gateway.onboarding?.getState(signal) ?? Promise.resolve(null)])
    if (signal?.aborted) return
    if (results[0].status === 'fulfilled') setStatus(results[0].value)
    if (results[1].status === 'fulfilled' && results[1].value) {
      const kind = results[1].value.kind
      const accountSetupComplete = !gateway.onboarding || results[2].status === 'fulfilled' && results[2].value?.shouldAskPlayedBefore === false
      if (kind === 'queue' || kind === 'match' || kind === 'result' && accountSetupComplete) setMode('paid')
    }
    if (results[2].status === 'fulfilled') { setOnboarding(results[2].value); setOnboardingError(null) }
    else setOnboardingError(errorMessage(results[2].reason))
    const failure = results.slice(0, 2).find(result => result.status === 'rejected')
    if (failure?.status === 'rejected') setError(errorMessage(failure.reason))
    setConnecting(false)
  }, [gateway, publishPaid])

  useEffect(() => { const controller = new AbortController(); void connect(controller.signal); return () => controller.abort() }, [connect])
  useEffect(() => { setSelected([]) }, [match?.matchId, match?.roundNumber, match?.phase])
  useEffect(() => { if (!choices.includes(entry)) setEntry(choices[0] ?? 1) }, [entry, choices.join(',')])
  useEffect(() => { if (match) play('phase') }, [match?.phase, match?.roundNumber, match?.matchId])
  useEffect(() => { if (match?.winner || paid?.kind === 'result' && mode === 'paid') play('win') }, [match?.winner, paid?.kind, mode])

  const refreshOnboarding = useCallback(async () => {
    if (!gateway.onboarding || onboardingLock.current) return
    onboardingLock.current = true
    setOnboardingBusy(true)
    setOnboardingError(null)
    try { setOnboarding(await gateway.onboarding.getState()) }
    catch (reason) { setOnboardingError(errorMessage(reason)) }
    finally { onboardingLock.current = false; setOnboardingBusy(false) }
  }, [gateway])
  useEffect(() => {
    if (!trialComplete || !practice || completedTrialCheck.current === practice.matchId) return
    completedTrialCheck.current = practice.matchId
    play('win')
    void refreshOnboarding()
  }, [trialComplete, practice?.matchId, play, refreshOnboarding])

  const refresh = useCallback(async () => {
    if (!gateway.paid || actionLock.current) return
    actionLock.current = true
    setBusy(true)
    try { publishPaid(await gateway.paid.getSession()); setRecovery(false); setError(null) }
    catch (reason) { setError(errorMessage(reason)); setRecovery(true) }
    finally { actionLock.current = false; setBusy(false) }
  }, [gateway, publishPaid])

  useEffect(() => {
    if (busy || revealing || recovery || trialExitFailed || panel === 'leave') return
    const shouldPollPaid = mode === 'paid' && (paid?.kind === 'queue' || paid?.kind === 'match')
    const shouldPollFree = (mode === 'free' || mode === 'trial') && !!practice?.opponentsThinking
    if (!shouldPollPaid && !shouldPollFree) return
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      if (actionLock.current) return
      try {
        if (shouldPollPaid && gateway.paid) { const next = await gateway.paid.getSession(controller.signal); if (!controller.signal.aborted) publishPaid(next) }
        else if (practice) { const next = await gateway.advance(practice.matchId, controller.signal); if (!controller.signal.aborted) setPractice(next) }
      } catch (reason) { if (!controller.signal.aborted) { setError(errorMessage(reason)); if (shouldPollPaid) setRecovery(true) } }
    }, shouldPollPaid ? 1000 : 1200)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [busy, revealing, recovery, mode, paid, practice, gateway, publishPaid, panel, trialExitFailed])

  async function act(operation: () => Promise<HeartsMatch | HeartsPaidSession>, isPaid = mode === 'paid', practiceMode: 'free' | 'trial' = mode === 'trial' ? 'trial' : 'free') {
    if (actionLock.current || recovery) return
    actionLock.current = true
    setBusy(true)
    setError(null)
    try {
      const next = await operation()
      if ('kind' in next) { publishPaid(next); setMode(next.kind === 'idle' ? 'lobby' : 'paid') }
      else { setPractice(next); setMode(practiceMode) }
      setSelected([])
    } catch (reason) {
      setError(errorMessage(reason))
      if (isPaid && (!(reason instanceof HeartsGatewayError) || reason.status === 0 || reason.status >= 500 || reason.status === 409)) setRecovery(true)
    } finally { actionLock.current = false; setBusy(false) }
  }
  function request(): HeartsPaidRequest { return { idempotencyKey: `hearts-${crypto.randomUUID()}`, expectedVersion: paid?.version } }
  function startFree() { if (!setupResolved) return; play('shuffle'); void act(() => gateway.startMatch({ targetScore: 100, difficulty: 'standard' }), false, 'free') }
  async function confirmExperience() {
    if (!gateway.onboarding || onboardingLock.current || actionLock.current) return
    onboardingLock.current = true
    setOnboardingBusy(true)
    setOnboardingError(null)
    try {
      const next = await gateway.onboarding.confirmExperience()
      if (next.shouldAskPlayedBefore) throw new Error('Your answer could not be saved. Please try again.')
      setOnboarding(next)
    } catch (reason) { setOnboardingError(errorMessage(reason)) }
    finally { onboardingLock.current = false; setOnboardingBusy(false) }
  }
  function startTrial() {
    if (!gateway.startTrial || onboardingBusy) return
    play('shuffle')
    void act(async () => {
      const next = await gateway.startTrial!()
      if (!next.isTrial) throw new HeartsGatewayError('The trial could not be opened. Please try again.')
      setTrialExitFailed(false)
      return next
    }, false, 'trial')
  }
  function joinPaid() { if (!gateway.paid || !setupResolved) return; play('chip'); const options = request(); void act(() => gateway.paid!.join(entry, options), true) }
  function selectCard(card: HeartsCard) {
    if (!match || busy || revealing || recovery || trialExitFailed) return
    if (match.phase === 'passing') {
      play('click')
      setSelected(current => current.includes(card.code) ? current.filter(code => code !== card.code) : current.length < 3 ? [...current, card.code] : current)
    } else {
      play('deal')
      const options = request()
      void act(() => mode === 'paid' ? gateway.paid!.play(match.matchId, card.code, options) : gateway.playCard(match.matchId, card.code))
    }
  }
  function passCards() {
    if (!match || selected.length !== 3) return
    play('move')
    const options = request()
    void act(() => mode === 'paid' ? gateway.paid!.pass(match.matchId, selected, options) : gateway.pass(match.matchId, selected))
  }
  function nextRound() {
    if (!match || mode === 'trial') return
    play('shuffle')
    const options = request()
    void act(() => mode === 'paid' ? gateway.paid!.nextRound(match.matchId, options) : gateway.nextRound(match.matchId))
  }
  function leave() {
    setPanel(null)
    if (mode === 'trial') { void leaveTrial(); return }
    const matchId = queue?.matchId ?? match?.matchId
    if (mode !== 'paid' || paid?.kind === 'result') { setMode('lobby'); setPractice(null); return }
    if (!matchId || !gateway.paid) return
    const options = request()
    void act(async () => { const next = await gateway.paid!.leave(matchId, options); setMode('lobby'); return next.kind === 'match' || next.kind === 'result' ? { ...next, kind: 'idle' as const, queue: null, table: null } : next })
  }
  async function leaveTrial() {
    if (!practice || actionLock.current) return
    actionLock.current = true
    setBusy(true)
    setError(null)
    setTrialExitFailed(false)
    try {
      if (!gateway.cancelTrial) throw new Error('The trial could not be closed. Please retry.')
      let state: HeartsOnboardingState
      try { state = await gateway.cancelTrial(practice.matchId) }
      catch (reason) {
        if (!(reason instanceof HeartsGatewayError) || reason.status !== 404 || !gateway.onboarding) throw reason
        state = await gateway.onboarding.getState()
      }
      setOnboarding(state)
      setPractice(null)
      setMode('lobby')
    } catch (reason) { setError(errorMessage(reason)); setTrialExitFailed(true) }
    finally { actionLock.current = false; setBusy(false) }
  }
  async function openPanel(next: Panel) {
    play('click')
    setPanel(next)
    if (next === 'history' && gateway.paid) {
      try { setHistory(await gateway.paid.getHistory()) } catch (reason) { setError(errorMessage(reason)) }
    }
  }
  const hasLeft = table?.players.some(player => player.isCurrentPlayer && player.hasLeft) ?? false
  const canPass = !!match && !hasLeft && match.phase === 'passing' && !match.submittedPasses.includes(match.humanSeat)
  const canPlay = !!match && !hasLeft && match.phase === 'playing' && match.yourTurn
  const locked = busy || revealing || recovery || trialExitFailed
  const legal = new Set(match?.legalCards.map(card => card.code) ?? [])
  const result = !!match && mode !== 'trial' && (mode === 'paid' ? paid?.kind === 'result' : !!match.winner)
  const turnName = match?.players.find(player => player.seat === match.turn)?.displayName ?? ''
  const instruction = !match ? '' : trialComplete ? 'Trial complete' : result ? 'Match complete' : revealing ? shownTrick?.winner ? `${playerName(match, shownTrick.winner)} takes the trick` : 'Cards in play' : canPass ? `Pass 3 cards ${directionLabel(match.passDirection).toLowerCase()}` : match.phase === 'passing' ? 'Waiting for the pass' : match.phase === 'complete' ? 'Round complete' : canPlay ? 'Your turn' : `${turnName} is playing`
  const deadline = mode === 'paid' && (canPlay || canPass) ? table?.actionDeadlineAtUtc : null
  const completedTrickVisible = revealing && shownTrick?.plays.length === 4 && shownTrick.winner
  const trialCoach = mode === 'trial' && match ? revealing ? {
    kind: 'waiting' as const,
    title: completedTrickVisible ? `${playerName(match, completedTrickVisible)} takes the trick` : 'Around the table we go!',
    message: completedTrickVisible ? shownTrick.points ? `${playerName(match, completedTrickVisible)} takes ${shownTrick.points} point${shownTrick.points === 1 ? '' : 's'} and leads next. Fewer points is better!` : `${playerName(match, completedTrickVisible)} takes no points this time and leads the next trick.` : 'One card each, clockwise. Watch the suit that starts this trick—you’ll follow it if you can.',
    why: 'The 2 of clubs starts the hand. Then the highest card of the suit led takes each trick and leads the next one. Other suits cannot beat the led suit, but hearts and the queen of spades add points to whoever takes them.',
    suggestedCodes: [],
  } : getTrialCoach(match, selected) : null
  const suggestedCodes = new Set(trialCoach?.suggestedCodes ?? [])

  return <div className="ff-hearts-page">
    <main className="ff-hearts-main">
      <nav className="ff-hearts-toolbar" aria-label="Hearts table tools">
        <div className="ff-hearts-table-info"><span className="ff-hearts-heart" aria-hidden="true">♥</span><strong>{mode === 'lobby' ? 'Hearts' : mode === 'trial' ? 'Trial run' : mode === 'free' ? 'Free play' : queue ? 'Finding a table' : `${money(table?.entryCredits ?? entry)} table`}</strong>{match && <span>{mode === 'trial' ? 'One hand · 13 tricks' : <>Round {match.roundNumber} <i>·</i> {match.targetScore} points</>}</span>}</div>
        <div className="ff-hearts-tools"><button type="button" onClick={toggleMute} aria-label={muted ? 'Turn sound on' : 'Mute sound'} title={muted ? 'Turn sound on' : 'Mute sound'}>{muted ? <SoundIcon muted /> : <SoundIcon />}</button>{match && <button type="button" onClick={() => void openPanel('scores')} aria-label="View scores"><ScoresIcon /></button>}<button type="button" onClick={() => void openPanel('rules')} aria-label="How to play">?</button>{gateway.paid && !showEntryGate && mode !== 'trial' && <button type="button" onClick={() => void openPanel('history')} aria-label="Match history"><HistoryIcon /></button>}{mode !== 'lobby' && <button type="button" className="ff-hearts-leave" onClick={() => mode === 'free' || mode === 'trial' || result ? leave() : void openPanel('leave')}>{mode === 'trial' ? 'Leave trial' : 'Leave table'}</button>}</div>
      </nav>

      <section className={`ff-hearts-table ${mode === 'lobby' ? 'is-lobby' : ''} ${queue ? 'is-queue' : ''} ${mode === 'trial' ? 'is-trial' : ''}`} aria-label="Hearts table" ref={boardRef}>
        <div className="ff-hearts-table__edge" aria-hidden="true" />
        <div className="ff-hearts-watermark" aria-hidden="true"><span>♥</span><strong>Hearts</strong><small>FORTUNE FORGE</small></div>
        {showEntryGate && <div className="ff-hearts-onboarding" aria-labelledby="hearts-experience-question" aria-busy={connecting || onboardingBusy || busy}>
          <span className="ff-hearts-onboarding__suit" aria-hidden="true">♥</span>
          {onboarding ? <>
            <h1 id="hearts-experience-question">Have you played Hearts before?</h1>
            <p>Find your feet with a free practice hand, or head straight for a table.</p>
            <div className="ff-hearts-onboarding__actions"><button type="button" className="ff-hearts-primary" onClick={startTrial} disabled={busy || onboardingBusy || connecting || !gateway.startTrial || !status?.available}>Learn to play</button><button type="button" className="ff-hearts-secondary" onClick={() => void confirmExperience()} disabled={busy || onboardingBusy || connecting}>I know how to play</button></div>
            {(onboardingBusy || busy) && <span className="ff-hearts-onboarding__status" role="status">{onboardingBusy ? 'Saving your answer…' : 'Opening your trial…'}</span>}
          </> : <><h1 id="hearts-experience-question">Opening Hearts</h1><p role="status">{onboardingError ? 'Your Hearts setup could not be loaded.' : 'Checking your Hearts setup…'}</p></>}
          {onboardingError && <div className="ff-hearts-onboarding__error" role="alert"><span>{onboardingError}</span>{!onboarding && <button type="button" className="ff-hearts-secondary" disabled={onboardingBusy || connecting} onClick={() => void refreshOnboarding()}>Retry setup</button>}</div>}
        </div>}
        {mode === 'lobby' && setupResolved && <div className="ff-hearts-lobby">
          <span className="ff-hearts-lobby__suits" aria-hidden="true">♠ <b>♥</b> ♣ <b>♦</b></span>
          <h1>Hearts</h1><p>Keep your score low. Take the pot.</p>
          <div className="ff-hearts-entry" aria-label="Choose your table buy-in">{choices.map((value, index) => <button key={value} type="button" className={`ff-hearts-chip ${entry === value ? 'is-selected' : ''}`} style={{ '--chip-color': chipColors[index % chipColors.length] } as CSSProperties} aria-label={`Select ${money(value)} entry`} aria-pressed={entry === value} disabled={busy || !paid} onClick={() => { play('chip'); setEntry(value) }}>{chipImages[value] ? <img src={chipImages[value]} alt="" draggable={false} /> : <span>{value}</span>}</button>)}</div>
          <div className="ff-hearts-entry-summary"><span>Entry <strong>{money(entry)}</strong></span><span>4-player pot <strong>{money(entry * 4)}</strong></span></div>
          <button className="ff-hearts-primary" type="button" onClick={joinPaid} disabled={connecting || busy || !paid || !gateway.paid || wallet < entry || recovery}>{busy ? 'Opening table…' : `Play for ${money(entry)}`}</button>
          <button className="ff-hearts-secondary" type="button" onClick={startFree} disabled={connecting || busy || !status?.available}>Play free</button>
          <small className="ff-hearts-lobby__note">Equal entries. Lowest score wins. Ties split the pot.</small>
          {paid && wallet < entry && <small className="ff-hearts-insufficient">Not enough balance for this table.</small>}
          <span className="ff-hearts-lobby__balance">Balance <b>{money(wallet)}</b></span>
        </div>}

        {queue && <div className="ff-hearts-queue">
          <div className="ff-hearts-queue__heading"><span className="ff-hearts-search" aria-hidden="true" /><h2>Finding your table</h2><p>{queue.players.length} of 4 seats filled</p></div>
          <div className="ff-hearts-queue__seats">{Array.from({ length: 4 }, (_, index) => { const player = queue.players[index]; return <div className={player ? 'has-player' : ''} key={index}><span className="ff-hearts-avatar">{player ? initials(player.displayName) : '·'}</span><strong>{player ? player.isCurrentPlayer ? 'You' : player.displayName : 'Waiting for players…'}</strong><small>{player ? money(queue.entryCredits) : 'Open seat'}</small></div> })}</div>
          <div className="ff-hearts-entry-summary"><span>Your entry <strong>{money(queue.entryCredits)}</strong></span><span>Pot <strong>{money(queue.potCredits)}</strong></span></div>
          <button type="button" className="ff-hearts-secondary" onClick={leave} disabled={locked}>Cancel & return entry</button>
        </div>}

        {match && <>
          {match.players.map(player => {
            const position = relativeSeat(player.seat, match.humanSeat)
            const active = !result && !revealing && match.turn === player.seat && match.phase === 'playing'
            const paidPlayer = table?.players.find(item => item.seat === player.seat)
            return <div className={`ff-hearts-seat seat-${position} ${active ? 'is-turn' : ''} ${paidPlayer?.hasLeft ? 'has-left' : ''}`} key={player.seat} aria-label={`${player.seat === match.humanSeat ? 'You' : player.displayName}, ${player.matchScore} points${active ? ', current turn' : ''}`}>
              {position !== 'bottom' && <div className="ff-hearts-seat__cards" aria-hidden="true">{Array.from({ length: Math.min(5, player.handCount) }, (_, index) => <span key={index}>{face(backCard, true)}</span>)}</div>}
              <div className="ff-hearts-seat__badge"><span className="ff-hearts-avatar">{player.seat === match.humanSeat ? 'You' : initials(player.displayName)}</span><div><strong>{player.seat === match.humanSeat ? 'You' : player.displayName}</strong><small>{paidPlayer?.hasLeft ? 'Left table' : match.phase === 'passing' ? player.hasPassed ? 'Cards passed' : 'Choosing cards' : `${player.roundScore} this round`}</small></div><b className="ff-hearts-seat__score" title="Match points; lowest wins">{player.matchScore}</b></div>
            </div>
          })}

          <div className="ff-hearts-center" aria-label="Current trick">
            {shownTrick?.plays.map(item => <div className={`ff-hearts-played seat-${relativeSeat(item.seat, match.humanSeat)}`} key={`${match.roundNumber}:${shownTrick.number ?? match.completedTricks.length}:${item.seat}`} aria-label={`${playerName(match, item.seat)} played ${cardName(item.card)}`}>{face(item.card)}</div>)}
            {!shownTrick?.plays.length && <div className="ff-hearts-center__mark" aria-hidden="true">♥</div>}
            {canPass && <div className={`ff-hearts-pass-direction direction-${match.passDirection}`} aria-label={`Pass ${directionLabel(match.passDirection).toLowerCase()}`}><b aria-hidden="true">{match.passDirection === 'across' ? '↑' : match.passDirection === 'left' ? '↖' : '↗'}</b><span>Pass {directionLabel(match.passDirection).toLowerCase()}</span></div>}
          </div>

          <div className="ff-hearts-decision">
            <div className="ff-hearts-status" role="status" aria-live="polite"><strong>{instruction}</strong>{canPass ? <span>{selected.length}/3 selected</span> : match.phase === 'playing' && <span className={match.heartsBroken ? 'hearts-broken' : ''}>♥ {match.heartsBroken ? 'Hearts broken' : 'Hearts unbroken'}</span>}</div>
            {deadline && <TurnClock deadline={deadline} />}
            {canPass && <button type="button" ref={passButtonRef} className="ff-hearts-primary ff-hearts-pass-button" onClick={passCards} disabled={locked || selected.length !== 3}>Pass cards {selected.length}/3</button>}
            {match.phase === 'complete' && !result && mode !== 'trial' && <button type="button" className="ff-hearts-primary" onClick={nextRound} disabled={locked}>Next round</button>}
          </div>
          <div className="ff-hearts-hand" aria-label="Your hand" style={{ '--hand-count': match.hand.length } as CSSProperties}>{match.hand.map((card, index) => <button key={card.code} type="button" ref={element => { if (element) handCardRefs.current.set(card.code, element); else handCardRefs.current.delete(card.code) }} className={`ff-hearts-hand__card ${selected.includes(card.code) ? 'is-selected' : ''} ${canPlay && legal.has(card.code) ? 'is-legal' : ''} ${suggestedCodes.has(card.code) ? 'is-suggested' : ''}`} style={{ '--card-index': index } as CSSProperties} aria-label={`${cardName(card)}${canPass ? selected.includes(card.code) ? ', selected to pass' : ', select to pass' : canPlay && legal.has(card.code) ? ', play card' : ''}`} aria-describedby={suggestedCodes.has(card.code) ? 'hearts-trial-coach' : undefined} aria-pressed={canPass ? selected.includes(card.code) : undefined} disabled={locked || (!canPass && (!canPlay || !legal.has(card.code)))} onClick={() => selectCard(card)}>{face(card)}</button>)}</div>
          {trialCoach && <HeartsTrialCoachBubble coach={trialCoach} boardRef={boardRef} cardRefs={handCardRefs} passRef={passButtonRef} layoutKey={`${match.matchId}:${match.hand.map(card => card.code).join(',')}:${selected.join(',')}`} onExplain={() => void openPanel('rules')} />}
          {mode === 'paid' && <div className="ff-hearts-pot" aria-label="Table pot"><span className="ff-hearts-pot__chip" aria-hidden="true">R</span><small>Pot</small><strong>{money(table?.potCredits ?? 0)}</strong></div>}
          {result && <div className="ff-hearts-result"><div><span className="ff-hearts-result__suit" aria-hidden="true">♥</span><small>Final result</small><h2>{resultTitle(match, table)}</h2><ResultScores match={match} table={table} />{mode === 'paid' && <p className="ff-hearts-result__payout">Your return <b>{money(table?.payoutCredits ?? 0)}</b></p>}<button type="button" className="ff-hearts-primary" onClick={() => { setMode('lobby'); setPractice(null) }}>Back to tables</button></div></div>}
          {trialComplete && !revealing && <div className="ff-hearts-result ff-hearts-trial-complete"><div><span className="ff-hearts-result__suit" aria-hidden="true">♥</span><small>13 tricks played</small><h2>Trial complete</h2><p>You finished your introductory hand. In Hearts, fewer points is better.</p><ResultScores match={match} table={null} />{onboardingBusy ? <p role="status">Confirming your trial…</p> : setupResolved ? <button type="button" className="ff-hearts-primary" onClick={() => { setMode('lobby'); setPractice(null) }}>Choose how to play</button> : <><p role="status">Your trial completion has not been confirmed yet.</p><button type="button" className="ff-hearts-primary" onClick={() => void refreshOnboarding()}>Retry confirmation</button></>}{onboardingError && <p role="alert">{onboardingError}</p>}</div></div>}
        </>}
      </section>
      {error && <div className="ff-hearts-error" role="alert"><span>{error}</span><button type="button" disabled={busy} onClick={() => trialExitFailed ? void leaveTrial() : recovery ? void refresh() : void connect()}>{trialExitFailed ? 'Retry closing trial' : recovery ? 'Refresh table' : 'Retry connection'}</button></div>}
      {notice && <div className="ff-hearts-notice" role="status"><span>{notice}</span><button type="button" aria-label="Dismiss notice" onClick={() => setNotice(null)}>×</button></div>}
      {mode !== 'lobby' && <footer className="ff-hearts-footer"><span>{mode === 'paid' ? <>Entry <b>{money(queue?.entryCredits ?? table?.entryCredits ?? entry)}</b></> : mode === 'trial' ? 'Trial run · No entry' : 'Free play'}</span>{mode !== 'trial' && <span>Balance <b>{money(wallet)}</b></span>}<span>{match ? `${match.completedTricks.length}/13 tricks` : '4 seats'}</span></footer>}
    </main>
    {panel && <Dialog title={panel === 'rules' ? 'How to play Hearts' : panel === 'scores' ? 'Table scores' : panel === 'history' ? 'Match history' : 'Leave this table?'} onClose={() => setPanel(null)}>
      {panel === 'rules' && <div className="ff-hearts-rules">
        <p>In Hearts, less is more! Each heart adds 1 point; the queen of spades brings a hefty 13. A full game ends when someone reaches 100, and the lowest score wins. Your Trial run is just one hand: 13 tricks to find your feet.</p>
        <h3>Pass three troublemakers</h3><p>Pick three cards you’d rather give away, then tap Pass cards. You’ll get three back. Pass left, then right, then across; on the fourth hand, everyone keeps their cards. Repeat!</p>
        <h3>Around the table we go</h3><p>Whoever holds the 2♣ starts the first trick with it. Play moves clockwise, one card each. The highest card of the suit led takes the trick—including its point cards—and leads the next one.</p>
        <h3>Follow that suit</h3><p>Have the suit that started the trick? You must play it. None left? Pick another suit. Only the suit led can win, so an off-suit card cannot steal the trick.</p><p>Leading a fresh trick? Pick a legal suit. Hearts cannot lead until a heart has been discarded on an earlier trick (hearts are “broken”), unless hearts are all you have left. On the very first trick, hearts and the queen of spades stay in your hand if a non-point card is legally available.</p>
        <details className="ff-hearts-strategy"><summary>Why keep or pass certain cards?</summary><p>A few friendly pointers—not a magic winning recipe. Keep an eye on the cards already played.</p><p><b>Keep a low escape card:</b> low cards in each suit can help you duck under a higher card when a trick gets loaded with points.</p><p><b>Lose with your highest safe card:</b> when following suit, consider the highest card that still stays below the current winner. That’s one less risky high card to save for later.</p><p><b>Give points a new home:</b> if you cannot follow suit, you can often discard the queen of spades or a heart into someone else’s trick. First-trick restrictions still count!</p><p><b>Watch those high spades:</b> the ace and king may force you to take the queen later. Low spades can help protect you; without them, consider passing the high ones.</p><p><b>Run out of a suit:</b> passing your last few cards of one suit may let you discard points when that suit comes around. Balance that plan against keeping useful low cards.</p></details>
        <h3>Feeling brave? Shoot the moon</h3><p>Take all 26 points in one hand and the other three players each get 26 instead. It’s a daring alternative to dodging points—not a plan to try by accident!</p>
        <h3>Playing for chips</h3><p>All four seats put in the same entry. The lowest eligible score wins the whole pot. Tied winners split it. Leaving before the deal returns your entry; leaving after the deal forfeits it. Take your turn before the table timer ends. A second missed turn leaves the table.</p>
      </div>}
      {panel === 'scores' && match && <ResultScores match={match} table={table} />}
      {panel === 'history' && <div className="ff-hearts-history">{history.length ? history.map(item => <div key={item.match.matchId}><span>Table {money(item.entryCredits)} <small>{item.completedAtUtc ? new Date(item.completedAtUtc).toLocaleDateString() : 'In progress'}</small></span><ResultScores match={item.match} table={item} /><strong>Your return {money(item.payoutCredits)}</strong></div>) : <p>No completed chip matches yet.</p>}</div>}
      {panel === 'leave' && <div className="ff-hearts-leave-confirm"><p>{queue ? 'Your entry will be returned to your balance.' : 'The cards have been dealt. Leaving forfeits your entry and your chance to win this pot.'}</p><button type="button" className="ff-hearts-primary" onClick={leave} disabled={locked}>{queue ? 'Leave & return entry' : 'Leave table'}</button><button type="button" className="ff-hearts-secondary" onClick={() => setPanel(null)}>Keep playing</button></div>}
    </Dialog>}
  </div>
}

function playerName(match: HeartsMatch, seat: HeartsSeat) { return seat === match.humanSeat ? 'You' : match.players.find(player => player.seat === seat)?.displayName ?? 'Player' }
function resultTitle(match: HeartsMatch, table: HeartsPaidTable | null) {
  const winners = table?.winnerSeats ?? (match.winner ? [match.winner] : [])
  if (!winners.length) return 'Table closed'
  if (winners.includes(match.humanSeat)) return winners.length > 1 ? 'You share the win!' : 'You win!'
  return winners.length > 1 ? 'Shared win' : `${playerName(match, winners[0])} wins`
}
function initials(name: string) { return name.replace(/[^a-z0-9 ]/gi, '').split(' ').map(part => part[0]).join('').slice(0, 2).toUpperCase() || 'P' }
function errorMessage(reason: unknown) { return reason instanceof Error ? reason.message : 'The table connection was interrupted. Refresh the table to check your game.' }
function ResultScores({ match, table }: { match: HeartsMatch; table: HeartsPaidTable | null }) { return <ol className="ff-hearts-results-list">{[...match.players].sort((a, b) => a.matchScore - b.matchScore).map(player => <li key={player.seat}><span>{playerName(match, player.seat)}{table?.players.find(item => item.seat === player.seat)?.hasLeft ? <small>Left</small> : null}</span><strong>{player.matchScore} <small>pts</small></strong>{table && <span>{table.winnerSeats.includes(player.seat) ? money(table.players.find(item => item.seat === player.seat)?.payoutCredits ?? 0) : '—'}</span>}</li>)}</ol> }

function TurnClock({ deadline }: { deadline: string }) {
  const [now, setNow] = useState(Date.now)
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 500); return () => window.clearInterval(timer) }, [deadline])
  const left = Math.max(0, Math.ceil((Date.parse(deadline) - now) / 1000))
  return <span className={`ff-hearts-clock ${left <= 10 ? 'is-low' : ''}`} aria-label={`${left} seconds remaining for your turn`}>{left}s</span>
}

function useTrickReveal(match: HeartsMatch | null, play: (cue: 'deal' | 'collect') => void) {
  type Trick = { plays: HeartsMatch['currentTrick']['plays']; number?: number; winner?: HeartsSeat; points?: number }
  const [shownTrick, setShownTrick] = useState<Trick | null>(null)
  const [revealing, setRevealing] = useState(false)
  const prior = useRef<{ matchId: string; round: number; recent: number; plays: HeartsMatch['currentTrick']['plays'] } | null>(null)
  const recentNumber = match?.recentTrick?.number ?? 0
  const currentKey = match?.currentTrick.plays.map(item => item.seat + item.card.code).join(',') ?? ''
  useEffect(() => {
    const timers: number[] = []
    if (!match) { setShownTrick(null); setRevealing(false); prior.current = null; return }
    const previous = prior.current
    const fresh = !previous || previous.matchId !== match.matchId || previous.round !== match.roundNumber
    const completed = !fresh && recentNumber > previous.recent && match.recentTrick
    const target = completed ? match.recentTrick! : match.currentTrick
    const initial = fresh ? target.plays.length : target.plays.findIndex((item, index) => previous.plays[index]?.card.code !== item.card.code)
    const known = initial === -1 ? target.plays.length : Math.max(0, initial)
    prior.current = { matchId: match.matchId, round: match.roundNumber, recent: recentNumber, plays: target.plays }
    setShownTrick({ ...target, plays: target.plays.slice(0, known) })
    const additions = target.plays.length - known
    setRevealing(!fresh && (additions > 0 || !!completed))
    for (let index = known; index < target.plays.length; index++) timers.push(window.setTimeout(() => { setShownTrick({ ...target, plays: target.plays.slice(0, index + 1) }); play('deal') }, (index - known + 1) * 260))
    timers.push(window.setTimeout(() => {
      if (completed) { play('collect'); setShownTrick(match.currentTrick); prior.current = { matchId: match.matchId, round: match.roundNumber, recent: recentNumber, plays: match.currentTrick.plays } }
      setRevealing(false)
    }, additions * 260 + (completed ? 1250 : 120)))
    return () => timers.forEach(window.clearTimeout)
  }, [match?.matchId, match?.roundNumber, match?.phase, recentNumber, currentKey, play])
  return { shownTrick, revealing }
}

function CardFace({ card, faceDown }: { card: HeartsCard; faceDown?: boolean }) {
  if (faceDown) return <span className="ff-hearts-fallback-card is-back"><b>FF</b></span>
  const symbol = ({ hearts: '♥', diamonds: '♦', spades: '♠', clubs: '♣' } as Record<string, string>)[card.suit] ?? '♠'
  const rank = card.label.replace(/[♠♥♣♦]/g, '')
  return <span className={`ff-hearts-fallback-card ${card.suit === 'hearts' || card.suit === 'diamonds' ? 'is-red' : ''}`}><span><b>{rank}</b><i>{symbol}</i></span><strong>{symbol}</strong><span><b>{rank}</b><i>{symbol}</i></span></span>
}
function Dialog({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  const root = useRef<HTMLDivElement>(null)
  const close = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    close.current?.focus()
    function key(event: KeyboardEvent) {
      if (event.key === 'Escape') { event.preventDefault(); onClose() }
      if (event.key !== 'Tab') return
      const controls = [...root.current?.querySelectorAll<HTMLElement>('button:not(:disabled),a[href],summary,[tabindex="0"]') ?? []]
      const first = controls[0], last = controls.at(-1)
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
    }
    document.addEventListener('keydown', key)
    return () => { document.removeEventListener('keydown', key); if (previous?.isConnected) previous.focus() }
  }, [])
  return <div className="ff-hearts-dialog-backdrop" onClick={event => { if (event.target === event.currentTarget) onClose() }}><div className="ff-hearts-dialog" role="dialog" aria-modal="true" aria-label={title} ref={root}><header><h2>{title}</h2><button type="button" ref={close} aria-label="Close dialog" onClick={onClose}>×</button></header><div className="ff-hearts-dialog__body">{children}</div></div></div>
}
function SoundIcon({ muted }: { muted?: boolean }) { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" />{muted ? <path d="m17 9 5 6m0-6-5 6" /> : <><path d="M17 8q5 4 0 8M20 5q7 7 0 14" /></>}</svg> }
function ScoresIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 21V11h4v10m2 0V3h4v18m2 0v-7h4v7" /></svg> }
function HistoryIcon() { return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6v5h5M4 11a8 8 0 1 1 2 7M12 7v6l4 2" /></svg> }
