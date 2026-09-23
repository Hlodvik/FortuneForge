import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { CrapsGatewayError, type CrapsGateway, type CrapsRound, type CrapsStatus } from './contracts'
import { DiceThrow } from './DiceThrow'
import './craps.css'

export type CrapsGameProps = Readonly<{
  gateway: CrapsGateway
  backHref?: string
  playerName?: string
  tableLabel?: string
  tableArtworkUrl?: string
  isYourTurn?: boolean
  activePlayerName?: string
  onRoundChange?: (round: CrapsRound | null) => void
}>

const pointNumbers = [4, 5, 6, 8, 9, 10] as const

export function CrapsGame({
  gateway,
  backHref,
  playerName = 'Player',
  tableLabel = 'Free-play table',
  tableArtworkUrl,
  isYourTurn = true,
  activePlayerName = 'Another player',
  onRoundChange,
}: CrapsGameProps) {
  const [status, setStatus] = useState<CrapsStatus | null>(null)
  const [round, setRound] = useState<CrapsRound | null>(null)
  const [pendingRound, setPendingRound] = useState<CrapsRound | null>(null)
  const [stake, setStake] = useState(10)
  const [isBusy, setIsBusy] = useState(false)
  const [diceMotion, setDiceMotion] = useState<'idle' | 'rolling' | 'settling'>('idle')
  const [error, setError] = useState<string | null>(null)
  const [statusRevision, setStatusRevision] = useState(0)
  const [tipsOpen, setTipsOpen] = useState(false)
  const tipsButtonRef = useRef<HTMLButtonElement>(null)
  const tipsCloseRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const controller = new AbortController()
    setStatus(null)
    setError(null)
    void gateway.getStatus(controller.signal)
      .then((nextStatus) => {
        setStatus(nextStatus)
        setStake((value) => clampStake(value, nextStatus))
      })
      .catch((reason: unknown) => {
        if (!(reason instanceof DOMException && reason.name === 'AbortError')) {
          setError(messageForError(reason))
        }
      })
    return () => controller.abort()
  }, [gateway, statusRevision])

  const publishRound = useCallback((nextRound: CrapsRound | null) => {
    setRound(nextRound)
    onRoundChange?.(nextRound)
  }, [onRoundChange])

  const startRound = useCallback(async () => {
    if (!status?.available || isBusy || !isYourTurn) return
    setIsBusy(true)
    setError(null)
    try {
      publishRound(await gateway.startRound(clampStake(stake, status)))
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      setIsBusy(false)
    }
  }, [gateway, isBusy, isYourTurn, publishRound, stake, status])

  const roll = useCallback(async () => {
    if (!round || round.phase === 'resolved' || isBusy || !isYourTurn) return
    setIsBusy(true)
    setDiceMotion('rolling')
    setError(null)
    try {
      const nextRound = await gateway.roll(round.roundId)
      setPendingRound(nextRound)
      setDiceMotion('settling')
      await diceSettleDelay()
      publishRound(nextRound)
      setPendingRound(null)
    } catch (reason) {
      setError(messageForError(reason))
    } finally {
      setDiceMotion('idle')
      setIsBusy(false)
    }
  }, [gateway, isBusy, isYourTurn, publishRound, round])

  const clearRound = useCallback(() => {
    if (isBusy) return
    publishRound(null)
    setError(null)
  }, [isBusy, publishRound])

  const closeTips = useCallback(() => {
    setTipsOpen(false)
    window.requestAnimationFrame(() => tipsButtonRef.current?.focus())
  }, [])

  useEffect(() => {
    if (!tipsOpen) return
    tipsCloseRef.current?.focus()
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeTips()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [closeTips, tipsOpen])

  const tableStyle = useMemo(() => tableArtworkUrl
    ? ({ '--ff-craps-table-art': `url(${tableArtworkUrl})` }) as CSSProperties
    : undefined, [tableArtworkUrl])
  const serviceReady = status?.available === true
  const isDiceInMotion = diceMotion !== 'idle'
  const message = isDiceInMotion
    ? 'Dice are rolling…'
    : isYourTurn
    ? tableMessage(round, serviceReady)
    : `${activePlayerName} is the shooter. Your turn comes after the dice pass.`
  const lastOutcome = round?.lastOutcome ?? null
  const diceOutcome = (pendingRound ?? round)?.lastOutcome ?? null

  return (
    <div className="ff-craps-page">
      <header className="ff-craps-header">
        <nav className="ff-craps-header__navigation" aria-label="Game navigation">
          <a className="ff-craps-header__brand" href={backHref ?? '/'} aria-label="Fortune Forge home">
            <span className="ff-craps-header__spark" aria-hidden="true">✦</span>
            <strong>Fortune Forge</strong>
          </a>
          <a className="ff-craps-header__other-games" href={backHref ?? '/games'}>Other games</a>
        </nav>
        <div className="ff-craps-header__account">
          <strong>{playerName}</strong>
          <span>{tableLabel}</span>
        </div>
      </header>

      {tipsOpen && <CrapsTipsDialog closeButtonRef={tipsCloseRef} onClose={closeTips} />}

      <main className="ff-craps-main">
        <section className="ff-craps-title" aria-labelledby="ff-craps-title">
          <h1 id="ff-craps-title">Craps</h1>
          <button className="ff-craps-tips-button" type="button" aria-expanded={tipsOpen} onClick={() => setTipsOpen(true)} ref={tipsButtonRef}>
            Tips
          </button>
        </section>

        <section className="ff-craps-table" aria-label="Craps table" style={tableStyle}>
          <div className="ff-craps-table__felt">
            <div className={`ff-craps-turn ${isYourTurn ? 'is-yours' : 'is-waiting'}`} aria-live="polite">
              <span>{isYourTurn ? 'Your turn · You are the shooter' : `${activePlayerName} is the shooter`}</span>
              <strong>{turnInstruction(round, isBusy, isYourTurn)}</strong>
            </div>

            <div className="ff-craps-point-row" aria-label="Point numbers">
              <span className={`ff-craps-puck ${round?.phase === 'point' ? 'is-on' : ''}`}>
                {round?.phase === 'point' ? 'ON' : 'OFF'}
              </span>
              {pointNumbers.map((number) => (
                <span className={round?.point === number ? 'is-point' : ''} key={number}>
                  <small>Point</small>{number}
                </span>
              ))}
            </div>

            <div className="ff-craps-center">
              <div className="ff-craps-callout" aria-live="polite">
                <small>{isDiceInMotion ? 'Dice rolling' : phaseLabel(round)}</small>
                <strong>{message}</strong>
                {!isDiceInMotion && lastOutcome?.totalReturn !== null && lastOutcome?.totalReturn !== undefined && (
                  <span>Return R{lastOutcome.totalReturn.toFixed(2)}</span>
                )}
              </div>

              <div className="ff-craps-dice">
                <DiceThrow
                  className="ff-craps-dice__stage"
                  values={[diceOutcome?.first ?? null, diceOutcome?.second ?? null]}
                  rollKey={(pendingRound ?? round)?.rolls.at(-1)?.rollNumber ?? 'ready'}
                  rolling={diceMotion === 'rolling'}
                  label={isDiceInMotion ? 'Dice rolling' : lastOutcome ? `Latest roll: ${lastOutcome.first} and ${lastOutcome.second}` : 'Two dice ready to roll'}
                />
                <span className="ff-craps-dice__total">
                  {isDiceInMotion ? 'Dice rolling…' : lastOutcome ? `Total ${lastOutcome.total}` : 'Dice ready'}
                </span>
              </div>

              <div className="ff-craps-rolls" aria-label="Roll history">
                {round?.rolls.length
                  ? round.rolls.slice(-6).map((item) => (
                    <span key={item.rollNumber} title={rollResultLabel(item.result)}>
                      <small>#{item.rollNumber}</small>{item.first} + {item.second} = <b>{item.total}</b>
                    </span>
                  ))
                  : <span className="ff-craps-rolls__empty">Your roll history will appear here.</span>}
              </div>
            </div>

            <div className={`ff-craps-pass-line ${round ? 'has-bet' : ''}`}>
              <span>PASS LINE</span>
              <strong>{round ? `R${round.stake.toFixed(2)} · Even money — R${(round.stake * 2).toFixed(2)} total return` : 'Even money (1:1)'}</strong>
            </div>
          </div>

          <div className="ff-craps-controls">
            {!round ? (
              <>
                <label>
                  <span>Pass Line bet</span>
                  <span className="ff-craps-stake-input">
                    <b>R</b>
                    <input
                      aria-label="Pass Line bet in Rand"
                      type="number"
                      min={status?.minimumStake ?? 1}
                      max={status?.maximumStake ?? 100}
                      step={status?.stakeIncrement ?? 1}
                      value={stake}
                      disabled={!serviceReady || isBusy || !isYourTurn}
                      onChange={(event) => setStake(Number(event.target.value))}
                    />
                  </span>
                </label>
                <div className="ff-craps-chip-row" aria-label="Quick stake choices">
                  {[5, 10, 25, 50].map((value) => (
                    <button
                      className={stake === value ? 'is-selected' : ''}
                      type="button"
                      disabled={!serviceReady || isBusy || !isYourTurn}
                      onClick={() => setStake(value)}
                      key={value}
                    >R{value}</button>
                  ))}
                </div>
                <button
                  className="ff-craps-primary"
                  type="button"
                  disabled={!serviceReady || isBusy || !isYourTurn}
                  onClick={() => void startRound()}
                >{isBusy ? 'Placing bet…' : `Bet R${stake} on Pass Line`}</button>
              </>
            ) : round.phase === 'resolved' ? (
              <button className="ff-craps-primary" type="button" disabled={isBusy || !isYourTurn} onClick={clearRound}>
                New Pass Line bet
              </button>
            ) : (
              <button className="ff-craps-primary ff-craps-primary--roll" type="button" disabled={isBusy || !isYourTurn} onClick={() => void roll()}>
                {isBusy ? 'Dice out…' : round.phase === 'come-out' ? 'Roll the come-out' : `Roll for point ${round.point}`}
              </button>
            )}
          </div>
        </section>

        {error && (
          <div className="ff-craps-error" role="alert">
            <strong>{error}</strong>
            {!serviceReady && (
              <button type="button" onClick={() => setStatusRevision((value) => value + 1)}>Check again</button>
            )}
          </div>
        )}

      </main>
    </div>
  )
}

function clampStake(value: number, status: CrapsStatus): number {
  if (!Number.isFinite(value)) return status.minimumStake
  const steps = Math.round((value - status.minimumStake) / status.stakeIncrement)
  const snapped = status.minimumStake + steps * status.stakeIncrement
  return Math.min(status.maximumStake, Math.max(status.minimumStake, snapped))
}

function diceSettleDelay(): Promise<void> {
  const reducedMotion = typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
  return new Promise((resolve) => window.setTimeout(resolve, reducedMotion ? 0 : 780))
}

function phaseLabel(round: CrapsRound | null): string {
  if (!round) return 'Table open'
  if (round.phase === 'come-out') return 'Come-out roll'
  if (round.phase === 'point') return `Point is ${round.point}`
  return 'Round complete'
}

export function tableMessage(round: CrapsRound | null, serviceReady: boolean): string {
  if (!round) return serviceReady ? 'Place a Pass Line bet before the come-out roll.' : 'Checking the table…'
  const result = round.lastOutcome?.result
  if (!result) return 'Come-out roll ready. A 7 or 11 wins the Pass Line.'
  switch (result) {
    case 'natural-win': return `Natural ${round.lastOutcome?.total}. Pass Line wins!`
    case 'craps-loss': return `Craps ${round.lastOutcome?.total}. Pass Line loses.`
    case 'point-established': return `Point is ${round.point}. Roll it again before a 7.`
    case 'point-hit': return `Point ${round.point} made. Pass Line wins!`
    case 'seven-out': return 'Seven-out. Pass Line loses and the dice pass.'
    case 'no-decision': return `${round.lastOutcome?.total}, no decision. Point remains ${round.point}.`
  }
}

function turnInstruction(round: CrapsRound | null, isBusy: boolean, isYourTurn: boolean): string {
  if (!isYourTurn) return 'Watch the shooter. Your controls unlock when the dice pass.'
  if (isBusy) return 'Dice are out. Please wait for the result.'
  if (!round) return 'Betting is open. Place your Pass Line bet.'
  if (round.phase === 'resolved') return 'The hand is over. Start a new Pass Line bet.'
  if (round.phase === 'come-out') return 'You are the shooter. Roll the come-out.'
  return `You are the shooter. Make point ${round.point} before a 7.`
}

function rollResultLabel(result: CrapsRound['rolls'][number]['result']): string {
  switch (result) {
    case 'natural-win': return 'Natural'
    case 'craps-loss': return 'Craps'
    case 'point-established': return 'Point established'
    case 'point-hit': return 'Point made'
    case 'seven-out': return 'Seven-out'
    case 'no-decision': return 'No decision'
    default: return 'Roll'
  }
}

function messageForError(reason: unknown): string {
  if (reason instanceof CrapsGatewayError) return reason.message
  return 'The Craps table is unavailable. Your Pass Line bet was not placed.'
}

function CrapsTipsDialog({
  closeButtonRef,
  onClose,
}: {
  closeButtonRef: RefObject<HTMLButtonElement | null>
  onClose: () => void
}) {
  return (
    <div className="ff-craps-tips-overlay">
      <section className="ff-craps-tips" role="dialog" aria-modal="true" aria-labelledby="ff-craps-tips-title">
        <header>
          <div>
            <small>Table guide</small>
            <h2 id="ff-craps-tips-title">Craps tips &amp; lingo</h2>
          </div>
          <button type="button" aria-label="Close Craps tips" onClick={onClose} ref={closeButtonRef}>×</button>
        </header>

        <div className="ff-craps-tips__grid">
          <article>
            <h3>Who does what?</h3>
            <dl>
              <dt>Shooter</dt><dd>The player rolling the dice. The shooter keeps rolling until a seven-out, then the dice pass clockwise.</dd>
              <dt>Other players</dt><dd>They bet on or against the shooter. Bots would fill these player positions in Fortune Forge.</dd>
              <dt>Table crew</dt><dd>The stickperson calls the roll, dealers handle bets, and the boxperson supervises. Fortune Forge software performs these house roles.</dd>
            </dl>
          </article>

          <article>
            <h3>Pass Line basics</h3>
            <ol>
              <li>Place a Pass Line bet before the come-out roll.</li>
              <li>A natural (7 or 11) wins. Craps (2, 3, or 12) loses.</li>
              <li>Any other total establishes the point: 4, 5, 6, 8, 9, or 10.</li>
              <li>Roll the point again before a 7 to win. A 7 first is a seven-out.</li>
            </ol>
          </article>

          <article>
            <h3>Useful terms</h3>
            <dl>
              <dt>Come-out</dt><dd>The first roll of a new hand.</dd>
              <dt>Point</dt><dd>The number the shooter must repeat before rolling 7.</dd>
              <dt>Even money</dt><dd>A 1:1 payout. A R10 win returns the R10 bet plus R10 winnings.</dd>
              <dt>Seven-out</dt><dd>A 7 rolled after a point is set. The hand ends and the dice pass.</dd>
            </dl>
          </article>

          <aside>
            <strong>Preview scope</strong>
            <span>This build currently models one shooter and one Pass Line bet. A full table will need multiple bettors, shooter rotation, additional bets, and bot player strategies.</span>
          </aside>
        </div>
      </section>
    </div>
  )
}
