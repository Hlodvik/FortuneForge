import { useEffect, useId, useMemo, useRef, useState, type CSSProperties } from 'react'
import { PHARAOHS_ASCENT_VISUALS as art } from './visuals'
import { decodePharaohState } from './pharaoh'
import { decodeTombState, type TombAction, type TombChoice, type TombRelic, type TombRoomKind, type TombRun, type TombResult } from './tomb'
import './SealedTombPanel.css'

const currency = new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' })
const roomArt = { coin: art.coinRoom, relic: art.relicRoom, key: art.keyRoom } as const
const relicArt = { sun: art.sun, moon: art.eye, serpent: art.wadjet } as const
const relicLabels = { sun: 'Sun', moon: 'Moon', serpent: 'Serpent' } as const
const relicKinds: readonly TombRelic[] = ['sun', 'moon', 'serpent']
const roomKinds: readonly TombRoomKind[] = ['coin', 'relic', 'key']
const choiceName = (choice: Pick<TombChoice, 'kind' | 'relic'>) => choice.kind === 'relic'
  ? `${relicLabels[choice.relic!]} relic` : choice.kind === 'coin' ? 'Golden hoard' : 'Hidden key'

export type SealedTombPanelProps = Readonly<{
  state: unknown | null
  isSpinning: boolean
  actionPending: boolean
  pendingAction?: TombAction | null
  disabled?: boolean
  error?: string | null
  onAction: (action: TombAction) => Promise<void>
  pointValueInCents: number
  reducedMotion: boolean
  onOpen?: () => void
}>

function PyramidMap({ run, result, blocked, pendingChoice, onChoose }: {
  run: TombRun | null; result: TombResult | null; blocked: boolean; pendingChoice: string | null
  onChoose: (id: string) => void
}) {
  const nextStep = run?.phase === 'explore' ? run.path.length + 1 : null
  const opened = run?.path.length ?? result?.roomsOpened ?? 0
  const path = run?.path ?? []
  const points = ['50,98', ...path.map(entry => {
    const width = 100 - (entry.step - 1) * 10
    const x = 50 + (roomKinds.indexOf(entry.kind) - 1) * width / 3
    return `${x},${90 - (entry.step - 1) * 14.4}`
  })].join(' ')
  return <div className="sealed-tomb__map" aria-label={`Pyramid expedition: ${opened} of six rooms explored`}>
    <img className="sealed-tomb__apex" src={art.sarcophagus} alt="The sealed sarcophagus" />
    <svg className="sealed-tomb__route" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      <polyline points={points} fill="none" />
    </svg>
    <div className="sealed-tomb__floors">
      {[6, 5, 4, 3, 2, 1].map(step => {
        const entry = path.find(room => room.step === step)
        const options = nextStep === step ? run?.choices : null
        return <div key={step} className="sealed-tomb__floor" data-current={nextStep === step || undefined}
          style={{ '--floor-width': `${100 - (step - 1) * 10}%` } as CSSProperties}>
          <span className="sealed-tomb__floor-number" aria-hidden="true">{step}</span>
          {roomKinds.map((kind, index) => options?.[index]
            ? <button key={kind} type="button" className="sealed-tomb__room sealed-tomb__room--choice"
              disabled={blocked} data-pending={pendingChoice === options[index].id || undefined}
              aria-label={`Open ${choiceName(options[index])}`} onClick={() => onChoose(options[index].id)}>
              <img src={roomArt[options[index].kind]} alt="" /><span>{choiceName(options[index])}</span>
            </button>
            : <span key={kind} className="sealed-tomb__room" data-visited={entry?.kind === kind || undefined} aria-hidden="true">
              <img src={entry?.kind === kind ? roomArt[kind] : art.gate} alt="" />
            </span>)}
        </div>
      })}
    </div>
    <span className="sealed-tomb__entry-mark">{opened > 0 ? 'Your passage through the pyramid' : 'Six torches. Your own path.'}</span>
  </div>
}

export function SealedTombPanel({ state, isSpinning, actionPending, pendingAction = null, disabled = false, error, onAction, pointValueInCents, reducedMotion, onOpen }: SealedTombPanelProps) {
  const tomb = useMemo(() => decodeTombState(state), [state])
  const reelBonusActive = (decodePharaohState(state)?.freeSpinsRemaining ?? 0) > 0
  const heading = useId()
  const [localPending, setLocalPending] = useState(false)
  const [pendingChoice, setPendingChoice] = useState<string | null>(null)
  const [localError, setLocalError] = useState<string | null>(null)
  const guard = useRef(false)
  const choicesRef = useRef<HTMLDivElement>(null)
  const focusAfterAction = useRef(false)
  const money = (points: number) => currency.format(points * pointValueInCents / 100)
  const run = tomb?.activeRun ?? null
  const result = tomb?.lastRun ?? null
  const pending = tomb?.pendingAction ?? pendingAction
  const blocked = disabled || isSpinning || actionPending || localPending
  const expedition = run !== null || result !== null || pending !== null
  const firstTicket = tomb?.tickets[0]
  const banked = run?.bankedPoints ?? result?.payoutPoints ?? 0
  const foundRelics = run?.relics ?? result?.relics
  const previousRoom = run?.path.at(-1)

  useEffect(() => {
    setLocalError(null)
  }, [tomb?.revision])
  useEffect(() => {
    if (focusAfterAction.current && !localPending && !actionPending) {
      choicesRef.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
      focusAfterAction.current = false
    }
  }, [tomb?.revision, localPending, actionPending])

  async function perform(action: TombAction) {
    if (!tomb || guard.current || blocked) return
    if (pending && JSON.stringify(action) !== JSON.stringify(pending)) return
    guard.current = true
    setLocalPending(true)
    setPendingChoice(action.choiceId ?? null)
    setLocalError(null)
    focusAfterAction.current = true
    onOpen?.()
    try {
      await onAction(action)
    } catch (cause) {
      focusAfterAction.current = false
      setLocalError(cause instanceof Error ? cause.message : 'The chamber could not be opened. Your saved expedition is unchanged.')
    } finally {
      guard.current = false
      setLocalPending(false)
      setPendingChoice(null)
    }
  }
  const submit = (action: TombAction['action'], choiceId?: string) => {
    if (tomb) void perform({ action, expectedRevision: tomb.revision, ...(choiceId === undefined ? {} : { choiceId }) })
  }

  if (!tomb) return <section className="sealed-tomb" aria-label="The Sealed Tomb">
    <p className="sealed-tomb__loading" role="status">{state === null ? 'Unsealing your expedition map…' : 'Your expedition could not be read. Reload the game to restore your saved progress.'}</p>
  </section>

  return <section className="sealed-tomb" aria-labelledby={heading} data-expedition-active={expedition || undefined}
    data-reduced-motion={reducedMotion || undefined} data-phase={run?.phase ?? (result ? 'complete' : 'idle')}
    onFocusCapture={onOpen} onPointerDownCapture={onOpen}>
    <header className="sealed-tomb__heading"><div><span className="sealed-tomb__eyebrow">Beyond the reels</span>
      <h2 id={heading}>The Sealed Tomb</h2><p>{expedition ? 'Choose your path. Keep every discovery.' : 'A pyramid expedition shaped by your choices.'}</p>
    </div><img className="sealed-tomb__sigil" src={art.ankh} alt="" /></header>
    <div className="sealed-tomb__bank" aria-live="polite" aria-atomic="true">
      <div><span>{result ? 'Expedition paid' : run ? 'Secured treasure' : 'Inside the pyramid'}</span>
        <strong>{expedition ? money(banked) : '5× entry secured'}</strong></div>
      <span className="sealed-tomb__bank-note">{result ? 'Added to your game balance' : run ? 'Yours to collect. Every room adds more.' : 'Six torches · three keys · one sarcophagus'}</span>
    </div>
    <div className="sealed-tomb__play" ref={choicesRef}>
      <PyramidMap run={run} result={result} blocked={blocked || pending !== null} pendingChoice={pendingChoice} onChoose={id => submit('open-room', id)} />
      <div className="sealed-tomb__interaction">
        {run && <>
          <div className="sealed-tomb__inventory">
            <div className="sealed-tomb__torches"><div className="sealed-tomb__inventory-label"><span>Torches</span><strong>{run.torchesRemaining} / 6</strong></div>
              <div className="sealed-tomb__torch-row" aria-hidden="true">{Array.from({ length: 6 }, (_, index) => <i key={index} data-lit={index < run.torchesRemaining || undefined} />)}</div></div>
            <div className="sealed-tomb__keys"><div className="sealed-tomb__inventory-label"><span>Keys found</span><strong>{run.keys} / 3</strong></div><span aria-hidden="true">⚿ ⚿ ⚿</span></div>
          </div>
          <div className="sealed-tomb__relics" aria-label="Your collected relics"><span>Relic sets<br /><b>Room prizes ×{run.relicMultiplier}</b></span>
            {relicKinds.map(relic => <span key={relic} className="sealed-tomb__relic" aria-label={`${relicLabels[relic]} relics: ${run.relics[relic]}`}>
              <img src={relicArt[relic]} alt="" /><small>{run.relics[relic]}</small></span>)}
          </div>
          <p className="sealed-tomb__combo-note">A Sun, Moon and Serpent set raises all room prizes by another ×1.</p>
          {run.phase === 'explore' ? <>
            <div className="sealed-tomb__choices-heading"><h3>Choose a door in the pyramid</h3><span>Room {run.path.length + 1} of 6 · one torch</span></div>
            <div className="sealed-tomb__choices" aria-label="Offered chamber discoveries">
              {run.choices.map(choice => <article key={choice.id} className="sealed-tomb__choice">
                <img src={roomArt[choice.kind]} alt="" /><span className="sealed-tomb__choice-kind">{choice.kind === 'coin' ? 'Coins' : choice.kind === 'relic' ? 'Relic' : 'Key'}</span>
                <strong>{choiceName(choice)}</strong>
                <p>{choice.kind === 'coin' ? 'Find 2×, 3× or 4× your funded stake.' : choice.kind === 'relic' ? `Add a ${relicLabels[choice.relic!]} relic to your set.` : 'Three keys open the sarcophagus.'}</p>
                <small>{choice.kind === 'coin' ? 'Largest room reward' : '1× stake + your find'}</small>
              </article>)}
            </div>
            <button type="button" className="sealed-tomb__collect" disabled={blocked || pending !== null} onClick={() => submit('collect')}>Collect {money(run.bankedPoints)} &amp; leave</button>
          </> : <div className="sealed-tomb__finale">
            <img src={art.sarcophagus} alt="The unlocked golden sarcophagus" /><span className="sealed-tomb__eyebrow">Three keys. The final chamber.</span>
            <h3>The sarcophagus opens</h3><p>Your secured treasure is safe. Reveal an additional 8×, 12× or 20× your funded stake.</p>
            <button type="button" className="sealed-tomb__entry-button" disabled={blocked || pending !== null} onClick={() => submit('open-sarcophagus')}>Open the sarcophagus</button>
          </div>}
          <p className="sealed-tomb__funded-stake">Funded stake {money(run.fundedWagerPoints)} · No additional wager during this expedition.</p>
          {previousRoom && <div className="sealed-tomb__receipt" role="status"><span className="sealed-tomb__eyebrow">Last discovery · room {previousRoom.step}</span>
            <p>{choiceName(previousRoom)} <strong>+{money(previousRoom.awardPoints)}</strong></p>
            <small>Room value before your relic-set multiplier.</small></div>}
        </>}
        {result && <div className="sealed-tomb__completion">
          <img src={result.reason === 'sarcophagus' ? art.sarcophagus : art.chest} alt="" /><span className="sealed-tomb__eyebrow">Expedition complete</span>
          <h3>{result.reason === 'sarcophagus' ? 'The pharaoh’s hidden fortune' : 'Your discoveries are yours'}</h3>
          <strong>{money(result.payoutPoints)}</strong>
          <p>{result.roomsOpened} {result.roomsOpened === 1 ? 'chamber' : 'chambers'} explored · {result.keys} {result.keys === 1 ? 'key' : 'keys'} found<br />Your reward has been added to your game balance.</p>
          <dl className="sealed-tomb__breakdown"><div><dt>Secured entry</dt><dd>{money(result.basePoints)}</dd></div>
            <div><dt>Room prizes ×{result.relicMultiplier}</dt><dd>{money(result.roomPoints * result.relicMultiplier)}</dd></div>
            {result.finalePoints > 0 && <div><dt>Sarcophagus</dt><dd>{money(result.finalePoints)}</dd></div>}</dl>
          <button type="button" className="sealed-tomb__return" disabled={blocked || pending !== null} onClick={() => submit('dismiss')}>Return to the reels</button>
        </div>}
        {!run && !result && <div className="sealed-tomb__entry">
          <p>Spend six torches exploring chambers. Choose coins, complete relic sets, or gather three keys to unlock the sarcophagus. Every discovery is yours to keep.</p>
          <button type="button" className="sealed-tomb__entry-button" disabled={blocked || pending !== null || !firstTicket || reelBonusActive} onClick={() => submit('enter')}>
            {reelBonusActive && firstTicket ? 'Finish your earned reel spins first' : firstTicket ? 'Enter the Sealed Tomb' : 'Earn an expedition ticket'}</button>
          {firstTicket && <p className="sealed-tomb__funded-stake">Ticket stake {money(firstTicket.fundedWagerPoints)} · No additional charge to enter.</p>}
          <div className="sealed-tomb__funding"><strong>{tomb.tickets.length} expedition {tomb.tickets.length === 1 ? 'ticket' : 'tickets'}</strong>
            <span>{tomb.funding.sigils} / {tomb.funding.requiredSigils} sigils to your next ticket</span>
            <progress aria-label="Expedition ticket sigils" max={tomb.funding.requiredSigils} value={tomb.funding.sigils} />
            <small>{tomb.funding.minimumScarabs ? `${tomb.funding.minimumScarabs}+ red scarabs on a paid spin add a sigil.` : 'Qualifying red scarabs on a paid spin add a sigil.'}</small></div>
        </div>}
        {pending && <div className="sealed-tomb__recovery"><p>Your last expedition action is waiting to settle. Resume it to restore the confirmed result.</p>
          <button type="button" className="sealed-tomb__entry-button" disabled={blocked} onClick={() => { void perform(pending) }}>Resume saved action</button></div>}
      </div>
    </div>
    {!result && <details className="sealed-tomb__rules"><summary>How the Sealed Tomb works</summary>
      <p>{tomb.funding.minimumScarabs ? `On a paid spin, ${tomb.funding.minimumScarabs} or more red scarabs anywhere add a sigil.` : 'On a qualifying paid spin, red scarabs add a sigil.'} Collect {tomb.funding.requiredSigils} sigils for an expedition ticket funded at their average wager. Each expedition starts with six torches and a secured 5× entry reward.</p>
      <p>Each room uses one torch. Coins give the largest immediate room prizes. Relics form Sun, Moon and Serpent sets that multiply all room prizes. Keys unlock the sarcophagus at three. Your entry reward and sarcophagus prize are separate from the relic multiplier.</p>
      <p>You can collect and leave while exploring. The last torch collects your reward automatically. Three keys lead straight to the sarcophagus. All expedition rewards are added to your game balance when the expedition ends.</p>
    </details>}
    {(error || localError) && <p className="sealed-tomb__error" role="alert">{error ?? localError}</p>}
    <div className="sealed-tomb__status" role="status" aria-live="polite">{actionPending || localPending ? 'Confirming your expedition…' : pending ? 'A saved action needs to finish before you continue.' : ''}</div>
    {foundRelics && <span className="sealed-tomb__sr">{relicKinds.map(relic => `${foundRelics[relic]} ${relicLabels[relic]} relics`).join(', ')}.</span>}
  </section>
}
