import { useCallback, useEffect, useRef, useState } from 'react'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import {
  cancelSolitaireQueue,
  claimSolitaireResult,
  dismissSolitaireResult,
  getSolitaireSession,
  isUncertainSolitaireFailure,
  joinSolitaireQueue,
  postCommandWithReconciliation,
  SolitaireRequestError,
  stableSolitaireMutation,
  type PendingSolitaireMutation,
} from '../../../games/cards/solitaire/solitaireApi'
import { SolitaireBoard } from '../../../games/cards/solitaire/SolitaireBoard'
import {
  applyLocalSolitaireCommand,
  autoFinishLocalSolitaire,
  createLocalSolitaireGame,
  isLocalSolitaireWon,
  projectRedactedDraw,
  SolitaireRuleError,
} from '../../../games/cards/solitaire/solitaireEngine'
import { freshCardSeed } from '../../../games/cards/shared/cards'
import '../../../games/cards/shared/playingCards.css'
import { formatDuration } from '../../../games/cards/solitaire/solitaireDisplay'
import {
  SOLITAIRE_BUY_INS,
  SOLITAIRE_DRAW_COUNTS,
  SOLITAIRE_PLAYER_COUNTS,
  type SolitaireAvailability,
  type SolitaireBuyIn,
  type SolitaireCommand,
  type SolitaireDrawCount,
  type SolitaireGame,
  type SolitaireMatchSession,
  type SolitaireMutationResponse,
  type SolitairePlayerCount,
  type SolitaireQueueSession,
  type SolitaireResultSession,
  type SolitaireSession,
} from '../../../games/cards/solitaire/solitaireTypes'
import { CardRoomNavigation } from '../CardRoomNavigation'
import './solitaire.css'

export type CompetitiveSolitairePageProps = Readonly<{
  account: AccountSummary
}>

type SolitaireContentProps = Readonly<{
  availability: SolitaireAvailability
  balanceCredits: number
  busy: boolean
  pending: PendingSolitaireMutation | null
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  drawCount: SolitaireDrawCount
  freeGame: SolitaireGame | null
  freePaused: boolean
  freeComplete: boolean
  freeAutoWinning: boolean
  freeSetupOpen: boolean
  competitiveSetupMatchId: string | null
  freeElapsedMilliseconds: number
  freeCanUndo: boolean
  onPlayerCountChange: (value: SolitairePlayerCount) => void
  onBuyInChange: (value: SolitaireBuyIn) => void
  onDrawCountChange: (value: SolitaireDrawCount) => void
  onJoin: () => void
  onCancel: (queue: SolitaireQueueSession) => void
  onCommand: (match: SolitaireMatchSession, command: SolitaireCommand) => void
  onCloseCompleted: (matchId: string) => void
  onNewCompetitive: (matchId: string) => void
  onChooseNewCompetitive: (matchId: string) => void
  onCancelCompetitiveSetup: () => void
  onClaim: (result: SolitaireResultSession) => void
  onStartFree: () => void
  onReplayFree: () => void
  onChooseNewFreeGame: () => void
  onCancelFreeSetup: () => void
  onFreeCommand: (command: SolitaireCommand) => void
  onFreePause: () => void
  onFreeUndo: () => void
  onFreeSubmit: () => void
  onExitFree: () => void
  onRefresh: () => void
}>

type OptimisticSolitaireCommand = Extract<SolitaireCommand, { type: 'move' | 'draw' | 'flip' }>

type QueuedSolitaireCommand = Readonly<{
  command: OptimisticSolitaireCommand
  idempotencyKey: string
}>

export function CompetitiveSolitairePage({ account }: CompetitiveSolitairePageProps) {
  const [availability, setAvailability] = useState<SolitaireAvailability>({ kind: 'loading' })
  const [balanceCredits, setBalanceCredits] = useState(account.balances.slotsCredits)
  const [playerCount, setPlayerCount] = useState<SolitairePlayerCount>(4)
  const [buyInCredits, setBuyInCredits] = useState<SolitaireBuyIn>(5)
  const [drawCount, setDrawCount] = useState<SolitaireDrawCount>(3)
  const [busy, setBusy] = useState(false)
  const [pending, setPending] = useState<PendingSolitaireMutation | null>(null)
  const [requestError, setRequestError] = useState<string | null>(null)
  const [freeGame, setFreeGame] = useState<SolitaireGame | null>(null)
  const [freePaused, setFreePaused] = useState(false)
  const [freeComplete, setFreeComplete] = useState(false)
  const [freeAutoWinning, setFreeAutoWinning] = useState(false)
  const [freeSetupOpen, setFreeSetupOpen] = useState(false)
  const [competitiveSetupMatchId, setCompetitiveSetupMatchId] = useState<string | null>(null)
  const [freeElapsedMilliseconds, setFreeElapsedMilliseconds] = useState(0)
  const [freeHistory, setFreeHistory] = useState<readonly SolitaireGame[]>([])
  const [freeSeed, setFreeSeed] = useState<number | null>(null)
  const serverMatchRef = useRef<SolitaireMatchSession | null>(null)
  const moveQueueRef = useRef<QueuedSolitaireCommand[]>([])
  const processingMovesRef = useRef(false)
  const deferredCommandRef = useRef(false)
  const autoWinAnimationRef = useRef<Promise<void> | null>(null)
  const autoWinGenerationRef = useRef(0)
  const freeAutoWinGenerationRef = useRef(0)

  useEffect(() => {
    if (freeGame === null || freePaused || freeComplete || freeAutoWinning) return
    let previous = Date.now()
    const timer = window.setInterval(() => {
      const now = Date.now()
      setFreeElapsedMilliseconds((value) => value + now - previous)
      previous = now
    }, 250)
    return () => window.clearInterval(timer)
  }, [freeGame, freePaused, freeComplete, freeAutoWinning])

  const loadSession = useCallback(async (quiet = false, signal?: AbortSignal) => {
    if (!quiet) setAvailability({ kind: 'loading' })
    try {
      const session = await getSolitaireSession(signal)
      if (moveQueueRef.current.length > 0 || processingMovesRef.current) return
      serverMatchRef.current = session.kind === 'match' ? session : null
      setAvailability((current) => {
        if (quiet && current.kind === 'ready' && current.session.kind === 'match'
          && session.kind === 'match' && current.session.matchId === session.matchId
          && current.session.version === session.version) return current
        return { kind: 'ready', session }
      })
      setPending(null)
      setRequestError(null)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (error instanceof SolitaireRequestError && error.code === 'competitive-solitaire-disabled') {
        setAvailability({ kind: 'disabled', message: error.message })
        setPending(null)
        setRequestError(null)
        return
      }
      if (!quiet) setAvailability({ kind: 'error', message: 'Competitive Solitaire could not connect. No buy-in was accepted.' })
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void loadSession(false, controller.signal)
    const refresh = () => {
      if (document.visibilityState === 'visible') void loadSession(true)
    }
    const poll = window.setInterval(refresh, 15_000)
    window.addEventListener('focus', refresh)
    window.addEventListener('online', refresh)
    return () => {
      controller.abort()
      window.clearInterval(poll)
      window.removeEventListener('focus', refresh)
      window.removeEventListener('online', refresh)
    }
  }, [loadSession])

  const beginMutation = (fingerprint: string): PendingSolitaireMutation | null => {
    if (pending !== null && pending.fingerprint !== fingerprint) {
      setRequestError('Finish or retry the pending request before choosing another action.')
      return null
    }
    const next = stableSolitaireMutation(pending, fingerprint)
    setPending(next)
    setRequestError(null)
    return next
  }

  const acceptMutation = (mutation: SolitaireMutationResponse) => {
    serverMatchRef.current = mutation.session.kind === 'match' ? mutation.session : null
    setAvailability({ kind: 'ready', session: mutation.session })
    setBalanceCredits(mutation.balanceCredits)
    setPending(null)
    setRequestError(null)
  }

  const rejectMutation = (error: unknown) => {
    if (!isUncertainSolitaireFailure(error)) setPending(null)
    if (error instanceof SolitaireRequestError && error.code === 'competitive-solitaire-disabled') {
      setAvailability({ kind: 'disabled', message: error.message })
      setPending(null)
      return
    }
    setRequestError(errorMessage(error))
  }

  const runMutation = async (
    fingerprint: string,
    request: (idempotencyKey: string) => Promise<SolitaireMutationResponse>,
  ) => {
    const active = beginMutation(fingerprint)
    if (active === null) return
    setBusy(true)
    try {
      acceptMutation(await request(active.idempotencyKey))
    } catch (error) {
      rejectMutation(error)
    } finally {
      setBusy(false)
    }
  }

  const join = () => void runMutation(
    `join:${playerCount}:${buyInCredits}:${drawCount}`,
    (key) => joinSolitaireQueue(playerCount, buyInCredits, drawCount, key),
  )

  const cancel = (queue: SolitaireQueueSession) => void runMutation(
    `cancel:${queue.ticketId}`,
    (key) => cancelSolitaireQueue(queue.ticketId, key),
  )

  const animateCompetitiveAutoWin = (
    match: SolitaireMatchSession,
    startingGame: SolitaireGame,
    commands: readonly Extract<SolitaireCommand, { type: 'move' }>[],
  ) => {
    const generation = ++autoWinGenerationRef.current
    const showFrame = (game: SolitaireGame) => {
      setAvailability((current) => current.kind === 'ready'
        && current.session.kind === 'match'
        && current.session.matchId === match.matchId
        ? { kind: 'ready', session: {
            ...current.session,
            score: game.score,
            moves: game.moves,
            game: { ...game, message: 'Deck completed!' },
          } }
        : current)
    }
    showFrame(startingGame)
    const animation = (async () => {
      let frame = startingGame
      for (const automatic of commands) {
        await waitForSolitaireFrame(95)
        if (autoWinGenerationRef.current !== generation) return
        frame = applyLocalSolitaireCommand(frame, automatic)
        showFrame(frame)
      }
      await waitForSolitaireFrame(320)
    })()
    autoWinAnimationRef.current = animation
    void animation.then(() => {
      if (autoWinGenerationRef.current === generation) autoWinAnimationRef.current = null
    })
  }

  const processQueuedMoves = async () => {
    if (processingMovesRef.current) return
    processingMovesRef.current = true
    try {
      while (moveQueueRef.current.length > 0) {
        const queued = moveQueueRef.current[0]!
        const serverMatch = serverMatchRef.current
        if (serverMatch === null) {
          moveQueueRef.current = []
          break
        }
        const outcome = await postCommandWithReconciliation(
          serverMatch,
          queued.command,
          queued.idempotencyKey,
        )
        if (outcome.mutation === null) {
          moveQueueRef.current = []
          autoWinGenerationRef.current += 1
          autoWinAnimationRef.current = null
          serverMatchRef.current = outcome.session.kind === 'match' ? outcome.session : null
          setAvailability({ kind: 'ready', session: outcome.session })
          break
        }
        setBalanceCredits(outcome.mutation.balanceCredits)
        moveQueueRef.current.shift()
        serverMatchRef.current = outcome.session.kind === 'match' ? outcome.session : null
        if (outcome.session.kind === 'match'
          && outcome.session.integrityWarning?.acknowledged === false) {
          moveQueueRef.current = []
          autoWinGenerationRef.current += 1
          autoWinAnimationRef.current = null
          setAvailability({ kind: 'ready', session: outcome.session })
          break
        }
        if (moveQueueRef.current.length === 0 && outcome.session.kind === 'match') {
          const automatic = autoFinishLocalSolitaire(outcome.session.game).commands[0]
          if (automatic?.type === 'move') {
            void command(outcome.session, automatic, true)
            continue
          }
        }
        if (moveQueueRef.current.length === 0 || outcome.session.kind !== 'match') {
          const animation = autoWinAnimationRef.current
          if (animation !== null) await animation
          setAvailability({ kind: 'ready', session: outcome.session })
        }
        if (outcome.session.kind !== 'match') {
          moveQueueRef.current = []
          break
        }
      }
      setPending(null)
      setRequestError(null)
    } catch (error) {
      moveQueueRef.current = []
      autoWinGenerationRef.current += 1
      autoWinAnimationRef.current = null
      rejectMutation(error)
      void loadSession(true)
    } finally {
      processingMovesRef.current = false
    }
  }

  const command = async (
    match: SolitaireMatchSession,
    nextCommand: SolitaireCommand,
    beginsAutomaticSequence = false,
  ) => {
    if (nextCommand.type === 'move' || nextCommand.type === 'draw' || nextCommand.type === 'flip') {
      if (match.integrityWarning?.acknowledged === false) return
      let optimisticGame: SolitaireGame
      try {
        optimisticGame = beginsAutomaticSequence
          ? match.game
          : applyLocalSolitaireCommand(match.game, nextCommand)
      } catch {
        if (nextCommand.type !== 'draw') return
        optimisticGame = projectRedactedDraw(match.game)
      }
      const autoFinished = autoFinishLocalSolitaire(optimisticGame)
      const automaticCommands = autoFinished.commands as readonly Extract<SolitaireCommand, { type: 'move' }>[]
      const commands: readonly OptimisticSolitaireCommand[] = beginsAutomaticSequence
        ? automaticCommands
        : [nextCommand, ...automaticCommands]
      const celebratesCompletion = isLocalSolitaireWon(autoFinished.game)
      setAvailability({ kind: 'ready', session: {
        ...match,
        score: celebratesCompletion ? optimisticGame.score : autoFinished.game.score,
        moves: celebratesCompletion ? optimisticGame.moves : autoFinished.game.moves,
        canUndo: true,
        game: celebratesCompletion
          ? { ...optimisticGame, message: 'Deck completed!' }
          : autoFinished.game,
      } })
      for (const queuedCommand of commands) {
        moveQueueRef.current.push({
          command: queuedCommand,
          idempotencyKey: `${queuedCommand.type}_${crypto.randomUUID().replaceAll('-', '')}`,
        })
      }
      if (celebratesCompletion) {
        animateCompetitiveAutoWin(match, optimisticGame, automaticCommands)
      }
      void processQueuedMoves()
      return
    }
    if (deferredCommandRef.current) return
    deferredCommandRef.current = true
    while (processingMovesRef.current || moveQueueRef.current.length > 0) {
      await new Promise((resolve) => window.setTimeout(resolve, 25))
    }
    const synchronizedMatch = serverMatchRef.current ?? match
    const fingerprint = `command:${synchronizedMatch.matchId}:${synchronizedMatch.version}:${JSON.stringify(nextCommand)}`
    const active = beginMutation(fingerprint)
    if (active === null) {
      deferredCommandRef.current = false
      return
    }
    setBusy(true)
    try {
      const outcome = await postCommandWithReconciliation(synchronizedMatch, nextCommand, active.idempotencyKey)
      if (outcome.mutation === null) return void loadSession(true)
      setAvailability({ kind: 'ready', session: outcome.session })
      serverMatchRef.current = outcome.session.kind === 'match' ? outcome.session : null
      setPending(null)
      if (outcome.mutation !== null) setBalanceCredits(outcome.mutation.balanceCredits)
      setRequestError(null)
      if (outcome.session.kind === 'match') {
        const automatic = autoFinishLocalSolitaire(outcome.session.game).commands[0]
        if (automatic?.type === 'move') void command(outcome.session, automatic, true)
      }
    } catch (error) {
      rejectMutation(error)
    } finally {
      setBusy(false)
      deferredCommandRef.current = false
    }
  }

  const closeCompleted = (matchId: string) => {
    setCompetitiveSetupMatchId(null)
    void runMutation(
      `dismiss:${matchId}`,
      (key) => dismissSolitaireResult(matchId, key),
    )
  }
  const restartCompetitive = async (matchId: string) => {
    const dismissal = beginMutation(`dismiss:${matchId}`)
    if (dismissal === null) return
    setCompetitiveSetupMatchId(null)
    setBusy(true)
    try {
      acceptMutation(await dismissSolitaireResult(matchId, dismissal.idempotencyKey))
      const fingerprint = `join:${playerCount}:${buyInCredits}:${drawCount}`
      const next = stableSolitaireMutation(null, fingerprint)
      setPending(next)
      acceptMutation(await joinSolitaireQueue(
        playerCount,
        buyInCredits,
        drawCount,
        next.idempotencyKey,
      ))
    } catch (error) {
      rejectMutation(error)
    } finally {
      setBusy(false)
    }
  }
  const claim = (result: SolitaireResultSession) => void runMutation(
    `claim:${result.matchId}`,
    (key) => claimSolitaireResult(result.matchId, key),
  )
  const resetFreeGame = (seed: number, choice: SolitaireDrawCount) => {
    freeAutoWinGenerationRef.current += 1
    setFreeSeed(seed)
    setFreeGame(createLocalSolitaireGame(seed, choice))
    setFreePaused(false)
    setFreeComplete(false)
    setFreeAutoWinning(false)
    setFreeSetupOpen(false)
    setFreeElapsedMilliseconds(0)
    setFreeHistory([])
    setRequestError(null)
  }
  const startFree = () => resetFreeGame(freshCardSeed(), drawCount)
  const replayFree = () => resetFreeGame(
    freeSeed ?? freshCardSeed(),
    freeGame?.drawCount === 1 ? 1 : 3,
  )
  const animateFreeAutoWin = (
    startingGame: SolitaireGame,
    commands: readonly Extract<SolitaireCommand, { type: 'move' }>[],
  ) => {
    const generation = ++freeAutoWinGenerationRef.current
    setFreeAutoWinning(true)
    setFreeGame({ ...startingGame, message: 'Deck completed!' })
    void (async () => {
      let frame = startingGame
      for (const automatic of commands) {
        await waitForSolitaireFrame(95)
        if (freeAutoWinGenerationRef.current !== generation) return
        frame = applyLocalSolitaireCommand(frame, automatic)
        setFreeGame({ ...frame, message: 'Deck completed!' })
      }
      await waitForSolitaireFrame(320)
      if (freeAutoWinGenerationRef.current !== generation) return
      setFreeAutoWinning(false)
      setFreeComplete(true)
    })()
  }
  const freeCommand = (nextCommand: SolitaireCommand) => {
    if (freeGame === null || freePaused || freeComplete || freeAutoWinning) return
    try {
      const next = applyLocalSolitaireCommand(freeGame, nextCommand)
      const autoFinished = autoFinishLocalSolitaire(next)
      setFreeHistory((history) => [...history.slice(-49), freeGame])
      if (isLocalSolitaireWon(autoFinished.game)) {
        animateFreeAutoWin(
          next,
          autoFinished.commands as readonly Extract<SolitaireCommand, { type: 'move' }>[],
        )
      } else {
        setFreeGame(autoFinished.game)
      }
    } catch (error) {
      if (!(error instanceof SolitaireRuleError)) setRequestError(errorMessage(error))
    }
  }
  const undoFree = () => {
    const previous = freeHistory[freeHistory.length - 1]
    if (previous === undefined || freeComplete) return
    setFreeGame(previous)
    setFreeHistory((history) => history.slice(0, -1))
    setRequestError(null)
  }
  const refresh = () => {
    setPending(null)
    setRequestError(null)
    void loadSession(false)
  }

  return (
    <div className="solitaire-page">
      <CardRoomNavigation
        playerName={account.playerName}
        balanceCredits={balanceCredits}
        onBalanceChange={setBalanceCredits}
      />
      {requestError && (
        <div className="solitaire-request-error" role="alert">
          <span>{requestError}</span>
          <button type="button" onClick={refresh}>Refresh game</button>
        </div>
      )}
      <SolitaireContent
        availability={availability}
        balanceCredits={balanceCredits}
        busy={busy}
        pending={pending}
        playerCount={playerCount}
        buyInCredits={buyInCredits}
        drawCount={drawCount}
        freeGame={freeGame}
        freePaused={freePaused}
        freeComplete={freeComplete}
        freeAutoWinning={freeAutoWinning}
        freeSetupOpen={freeSetupOpen}
        competitiveSetupMatchId={competitiveSetupMatchId}
        freeElapsedMilliseconds={freeElapsedMilliseconds}
        freeCanUndo={freeHistory.length > 0}
        onPlayerCountChange={setPlayerCount}
        onBuyInChange={setBuyInCredits}
        onDrawCountChange={setDrawCount}
        onJoin={join}
        onCancel={cancel}
        onCommand={(match, nextCommand) => void command(match, nextCommand)}
        onCloseCompleted={closeCompleted}
        onNewCompetitive={(matchId) => void restartCompetitive(matchId)}
        onChooseNewCompetitive={setCompetitiveSetupMatchId}
        onCancelCompetitiveSetup={() => setCompetitiveSetupMatchId(null)}
        onClaim={claim}
        onStartFree={startFree}
        onReplayFree={replayFree}
        onChooseNewFreeGame={() => setFreeSetupOpen(true)}
        onCancelFreeSetup={() => setFreeSetupOpen(false)}
        onFreeCommand={freeCommand}
        onFreePause={() => {
          if (!freeAutoWinning) setFreePaused((value) => !value)
        }}
        onFreeUndo={undoFree}
        onFreeSubmit={() => setFreeComplete(true)}
        onExitFree={() => {
          freeAutoWinGenerationRef.current += 1
          setFreeGame(null)
          setFreeSetupOpen(false)
          setFreeAutoWinning(false)
        }}
        onRefresh={refresh}
      />
    </div>
  )
}

export function SolitaireContent(props: SolitaireContentProps) {
  if (props.freeGame !== null) return <FreePanel {...props} game={props.freeGame} />
  const { availability } = props
  if (availability.kind === 'loading') return <main className="solitaire-state" role="status">Checking competitive Solitaire…</main>
  if (availability.kind === 'disabled') {
    return (
      <main className="solitaire-state solitaire-state--disabled" role="status">
        <span aria-hidden="true">♠</span>
        <p>Competitive play unavailable</p>
        <h2>No buy-ins are being accepted.</h2>
        <strong>{availability.message}</strong>
        <button className="solitaire-primary-action" type="button" onClick={props.onStartFree}>Play free Solitaire</button>
      </main>
    )
  }
  if (availability.kind === 'error') {
    return (
      <main className="solitaire-state solitaire-state--error" role="alert">
        <h2>Competitive play is offline.</h2>
        <p>{availability.message}</p>
        <button type="button" onClick={props.onRefresh}>Try again</button>
        <button type="button" onClick={props.onStartFree}>Play free Solitaire</button>
      </main>
    )
  }
  return <main className="solitaire-main"><SessionPanel {...props} session={availability.session} /></main>
}

function SessionPanel(props: SolitaireContentProps & { session: SolitaireSession }) {
  const { session } = props
  if (session.kind === 'idle') {
    const retrying = props.pending?.fingerprint === `join:${props.playerCount}:${props.buyInCredits}:${props.drawCount}`
    return (
      <section className="solitaire-panel solitaire-lobby">
        <p>Each competitive player gets the same deal and ten minutes of play.</p>
        <div className="solitaire-lobby__controls">
          <label><span>Players</span><select value={props.playerCount} disabled={props.busy || props.pending !== null}
            onChange={(event) => props.onPlayerCountChange(Number(event.target.value) as SolitairePlayerCount)}>
            {SOLITAIRE_PLAYER_COUNTS.map((value) => <option value={value} key={value}>{value} players</option>)}
          </select></label>
          <label><span>Buy-in</span><select value={props.buyInCredits} disabled={props.busy || props.pending !== null}
            onChange={(event) => props.onBuyInChange(Number(event.target.value) as SolitaireBuyIn)}>
            {SOLITAIRE_BUY_INS.map((value) => <option value={value} key={value}>R{value}</option>)}
          </select></label>
          <label><span>Cards per turn</span><select value={props.drawCount} disabled={props.busy || props.pending !== null}
            onChange={(event) => props.onDrawCountChange(Number(event.target.value) as SolitaireDrawCount)}>
            {SOLITAIRE_DRAW_COUNTS.map((value) => <option value={value} key={value}>Turn {value}</option>)}
          </select></label>
        </div>
        <div className="solitaire-lobby__actions">
          <button className="solitaire-primary-action" type="button" disabled={props.busy || (props.pending !== null && !retrying)} onClick={props.onJoin}>
            {props.busy ? 'Starting…' : retrying ? 'Retry same request' : `Competitive · R${props.buyInCredits}`}
          </button>
          <button type="button" disabled={props.busy} onClick={props.onStartFree}>Play free</button>
        </div>
        <small>Balance R{props.balanceCredits.toFixed(2)} · Your turn choice is locked when the game starts.</small>
      </section>
    )
  }
  if (session.kind === 'queued') return <QueuePanel queue={session} busy={props.busy} onCancel={() => props.onCancel(session)} />
  if (session.kind === 'result') return <ResultPanel result={session} busy={props.busy}
    onClaim={() => props.onClaim(session)} onReturn={() => props.onCloseCompleted(session.matchId)} />
  return <MatchPanel match={session} busy={props.busy}
    onCommand={(command) => props.onCommand(session, command)}
    onClose={() => props.onCloseCompleted(session.matchId)}
    setupOpen={props.competitiveSetupMatchId === session.matchId}
    playerCount={props.playerCount}
    buyInCredits={props.buyInCredits}
    drawCount={props.drawCount}
    onPlayerCountChange={props.onPlayerCountChange}
    onBuyInChange={props.onBuyInChange}
    onDrawCountChange={props.onDrawCountChange}
    onChooseNewGame={() => props.onChooseNewCompetitive(session.matchId)}
    onCancelSetup={props.onCancelCompetitiveSetup}
    onNewGame={() => props.onNewCompetitive(session.matchId)} />
}

function QueuePanel({ queue, busy, onCancel }: { queue: SolitaireQueueSession, busy: boolean, onCancel: () => void }) {
  const seats = Array.from({ length: queue.playerCount }, (_, index) => queue.players[index] ?? null)
  return (
    <section className="solitaire-panel solitaire-queue">
      <p className="solitaire-eyebrow">Queue position {queue.position}</p>
      <div className="solitaire-queue__progress" aria-label={`${queue.players.length} of ${queue.playerCount} players ready`}>
        <span style={{ width: `${queue.players.length / queue.playerCount * 100}%` }} />
      </div>
      <div className="solitaire-seat-grid">
        {seats.map((player, index) => (
          <div className={player ? 'is-filled' : ''} key={player?.playerId ?? index}>
            <span>{player?.displayName.slice(0, 1).toUpperCase() ?? '·'}</span>
            <strong>{player?.displayName ?? 'Open seat'}</strong>
            <small>{player?.isCurrentPlayer ? 'You' : `Seat ${index + 1}`}</small>
          </div>
        ))}
      </div>
      <p>Pool R{queue.prizePoolCredits.toFixed(2)} · Winner R{queue.winnerPayoutCredits.toFixed(2)}</p>
      <button type="button" disabled={busy} onClick={onCancel}>{busy ? 'Leaving…' : 'Leave queue'}</button>
    </section>
  )
}

function MatchPanel({
  match,
  busy,
  setupOpen,
  playerCount,
  buyInCredits,
  drawCount,
  onCommand,
  onClose,
  onPlayerCountChange,
  onBuyInChange,
  onDrawCountChange,
  onChooseNewGame,
  onCancelSetup,
  onNewGame,
}: {
  match: SolitaireMatchSession
  busy: boolean
  setupOpen: boolean
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  drawCount: SolitaireDrawCount
  onCommand: (command: SolitaireCommand) => void
  onClose: () => void
  onChooseNewGame: () => void
  onCancelSetup: () => void
  onNewGame: () => void
  onPlayerCountChange: (value: SolitairePlayerCount) => void
  onBuyInChange: (value: SolitaireBuyIn) => void
  onDrawCountChange: (value: SolitaireDrawCount) => void
}) {
  const current = match.players.find((player) => player.isCurrentPlayer)
  const playing = current?.status === 'playing'
  const integrityFailed = current?.status === 'integrity-failed'
  const autoWinning = match.game.message === 'Deck completed!'
  const warning = match.integrityWarning?.acknowledged === false
    ? match.integrityWarning
    : null
  return (
    <section className="solitaire-match">
      <div className="solitaire-match__status">
        <div><span>Score</span><strong>{match.score.toLocaleString()}</strong></div>
        <div><span>Moves</span><strong>{match.moves}</strong></div>
        <div><span>Time</span><strong><RemainingTime match={match} /></strong></div>
      </div>
      {warning !== null && (
        <div className="solitaire-integrity-warning" role="alert">
          <div>
            <strong>Move reversed</strong>
            <p>{warning.reason}</p>
            <small>{warning.purpose}</small>
            <small>Contact customer support if you think we got it wrong.</small>
          </div>
          <button className="solitaire-primary-action" type="button" disabled={busy}
            onClick={() => onCommand({ type: 'acknowledge-warning' })}>Acknowledge</button>
        </div>
      )}
      <SolitaireBoard game={match.game} autoWinning={autoWinning}
        busy={busy || !playing || match.isPaused || autoWinning || warning !== null} onCommand={onCommand} />
      <div className="solitaire-match__controls">
        {playing && !autoWinning && warning === null ? (
          <>
            <button type="button" disabled={busy || !match.canUndo || match.isPaused}
              onClick={() => onCommand({ type: 'undo' })}>Undo</button>
            <button type="button" disabled={busy || match.pauseRemainingMilliseconds <= 0}
              onClick={() => onCommand({ type: match.isPaused ? 'resume' : 'pause' })}>
              {match.isPaused ? 'Resume' : 'Pause'} · <PauseRemainingTime match={match} /> left
            </button>
            <button className="solitaire-primary-action" type="button" disabled={busy}
              onClick={() => onCommand({ type: 'submit' })}>Submit game</button>
          </>
        ) : null}
      </div>
      {!playing && <CompetitiveCompletionDialog match={match} busy={busy}
        integrityFailed={integrityFailed} setupOpen={setupOpen}
        playerCount={playerCount} buyInCredits={buyInCredits} drawCount={drawCount}
        onPlayerCountChange={onPlayerCountChange} onBuyInChange={onBuyInChange}
        onDrawCountChange={onDrawCountChange} onChooseNewGame={onChooseNewGame}
        onCancelSetup={onCancelSetup} onNewGame={onNewGame} onReturn={onClose} />}
    </section>
  )
}

function RemainingTime({ match }: { match: SolitaireMatchSession }) {
  const [elapsed, setElapsed] = useState(0)
  useEffect(() => {
    setElapsed(0)
    if (match.isPaused) return
    const started = Date.now()
    const timer = window.setInterval(() => setElapsed(Date.now() - started), 1_000)
    return () => window.clearInterval(timer)
  }, [match.matchId, match.version, match.isPaused, match.remainingMilliseconds])
  return match.isPaused ? 'Paused' : formatMilliseconds(Math.max(0, match.remainingMilliseconds - elapsed))
}

function PauseRemainingTime({ match }: { match: SolitaireMatchSession }) {
  const [elapsed, setElapsed] = useState(0)
  useEffect(() => {
    setElapsed(0)
    if (!match.isPaused) return
    const started = Date.now()
    const timer = window.setInterval(() => setElapsed(Date.now() - started), 250)
    return () => window.clearInterval(timer)
  }, [match.matchId, match.version, match.isPaused, match.pauseRemainingMilliseconds])
  return formatMilliseconds(Math.max(0, match.pauseRemainingMilliseconds - elapsed))
}

function FreePanel(props: SolitaireContentProps & { game: SolitaireGame }) {
  return (
    <main className="solitaire-main">
      <section className="solitaire-match solitaire-match--free">
        <div className="solitaire-match__status">
          <div><span>Score</span><strong>{props.game.score.toLocaleString()}</strong></div>
          <div><span>Moves</span><strong>{props.game.moves}</strong></div>
          <div><span>Time</span><strong>{formatElapsed(props.freeElapsedMilliseconds)}</strong></div>
        </div>
        <SolitaireBoard game={props.game} autoWinning={props.freeAutoWinning}
          busy={props.freePaused || props.freeComplete || props.freeAutoWinning} onCommand={props.onFreeCommand} />
        <div className="solitaire-match__controls">
          <button type="button" disabled={!props.freeCanUndo || props.freePaused || props.freeComplete || props.freeAutoWinning}
            onClick={props.onFreeUndo}>Undo</button>
          <button type="button" disabled={props.freeComplete || props.freeAutoWinning}
            onClick={props.onFreePause}>{props.freePaused ? 'Resume' : 'Pause'}</button>
          <button className="solitaire-primary-action" type="button"
            disabled={props.freeComplete || props.freeAutoWinning} onClick={props.onFreeSubmit}>Submit game</button>
        </div>
        {props.freeComplete && (
          <div className="solitaire-result-dialog" role="dialog" aria-modal="true" aria-labelledby="free-result-title">
            {props.freeSetupOpen ? (
              <div>
                <p className="solitaire-eyebrow">New game</p>
                <h2 id="free-result-title">Choose your draw</h2>
                <p>Your last choice stays selected.</p>
                <div className="solitaire-new-game-options" role="group" aria-label="Cards per turn">
                  {SOLITAIRE_DRAW_COUNTS.map((value) => (
                    <button className={props.drawCount === value ? 'is-selected' : ''} type="button"
                      aria-pressed={props.drawCount === value} key={value}
                      onClick={() => props.onDrawCountChange(value)}>Turn {value}</button>
                  ))}
                </div>
                <div className="solitaire-results__actions">
                  <button className="solitaire-primary-action" type="button" onClick={props.onStartFree}>Start new game</button>
                  <button type="button" onClick={props.onCancelFreeSetup}>Back</button>
                </div>
              </div>
            ) : (
              <div>
                <p className="solitaire-eyebrow">Game complete</p>
                <h2 id="free-result-title">{props.game.score.toLocaleString()} points</h2>
                <p>{props.game.moves} moves · {formatElapsed(props.freeElapsedMilliseconds)}</p>
                <div className="solitaire-results__actions solitaire-results__actions--three">
                  <button className="solitaire-primary-action" type="button" onClick={props.onReplayFree}>Replay</button>
                  <button type="button" onClick={props.onChooseNewFreeGame}>New game</button>
                  <button type="button" onClick={props.onExitFree}>Return</button>
                </div>
              </div>
            )}
          </div>
        )}
      </section>
    </main>
  )
}

function ResultPanel({ result, busy, onClaim, onReturn }: {
  result: SolitaireResultSession
  busy: boolean
  onClaim: () => void
  onReturn: () => void
}) {
  const current = result.standings.find((standing) => standing.isCurrentPlayer)
  const won = (current?.payoutCredits ?? 0) > 0
  return (
    <section className="solitaire-panel solitaire-results">
      <p>{current ? `You placed #${current.rank}` : 'Game complete'}</p>
      {current && <p>{current.score.toLocaleString()} points · {current.moves} moves · R{current.payoutCredits.toFixed(2)} ready to claim</p>}
      <ol>
        {result.standings.map((standing) => (
          <li className={standing.isCurrentPlayer ? 'is-current' : ''} key={standing.playerId}>
            <strong>#{standing.rank} {standing.displayName}</strong>
            <span>{standing.score.toLocaleString()} pts · {formatDuration(standing.elapsedSeconds)}</span>
            <small>R{standing.payoutCredits.toFixed(2)}</small>
          </li>
        ))}
      </ol>
      <div className="solitaire-results__actions">
        <button className="solitaire-primary-action" type="button" disabled={busy || !result.canClaim} onClick={onClaim}>
          {busy
            ? (won ? 'Claiming…' : 'Accepting…')
            : result.claimStatus === 'completed'
              ? (won ? 'Reward claimed' : 'Accepted')
              : (won ? 'Claim reward' : 'Accept')}
        </button>
        <button type="button" disabled={busy} onClick={onReturn}>Return</button>
      </div>
    </section>
  )
}

function CompetitiveCompletionDialog({
  match,
  busy,
  integrityFailed,
  setupOpen,
  playerCount,
  buyInCredits,
  drawCount,
  onPlayerCountChange,
  onBuyInChange,
  onDrawCountChange,
  onChooseNewGame,
  onCancelSetup,
  onNewGame,
  onReturn,
}: {
  match: SolitaireMatchSession
  busy: boolean
  integrityFailed: boolean
  setupOpen: boolean
  playerCount: SolitairePlayerCount
  buyInCredits: SolitaireBuyIn
  drawCount: SolitaireDrawCount
  onPlayerCountChange: (value: SolitairePlayerCount) => void
  onBuyInChange: (value: SolitaireBuyIn) => void
  onDrawCountChange: (value: SolitaireDrawCount) => void
  onChooseNewGame: () => void
  onCancelSetup: () => void
  onNewGame: () => void
  onReturn: () => void
}) {
  const current = match.players.find((player) => player.isCurrentPlayer)
  return (
    <div className="solitaire-result-dialog" role="dialog" aria-modal="true" aria-labelledby="competitive-result-title">
      {setupOpen ? (
        <div>
          <p className="solitaire-eyebrow">New competitive game</p>
          <h2 id="competitive-result-title">Choose your table</h2>
          <div className="solitaire-completion-setup">
            <label><span>Players</span><select value={playerCount} disabled={busy}
              onChange={(event) => onPlayerCountChange(Number(event.target.value) as SolitairePlayerCount)}>
              {SOLITAIRE_PLAYER_COUNTS.map((value) => <option value={value} key={value}>{value} players</option>)}
            </select></label>
            <label><span>Wager</span><select value={buyInCredits} disabled={busy}
              onChange={(event) => onBuyInChange(Number(event.target.value) as SolitaireBuyIn)}>
              {SOLITAIRE_BUY_INS.map((value) => <option value={value} key={value}>R{value}</option>)}
            </select></label>
            <label><span>Cards per turn</span><select value={drawCount} disabled={busy}
              onChange={(event) => onDrawCountChange(Number(event.target.value) as SolitaireDrawCount)}>
              {SOLITAIRE_DRAW_COUNTS.map((value) => <option value={value} key={value}>Turn {value}</option>)}
            </select></label>
          </div>
          <div className="solitaire-results__actions">
            <button className="solitaire-primary-action" type="button" disabled={busy}
              onClick={onNewGame}>{busy ? 'Starting…' : `Start · R${buyInCredits}`}</button>
            <button type="button" disabled={busy} onClick={onCancelSetup}>Back</button>
          </div>
        </div>
      ) : (
        <div>
        <p className="solitaire-eyebrow">{integrityFailed ? 'Game ended' : 'Game complete'}</p>
        <h2 id="competitive-result-title">{match.score.toLocaleString()} points</h2>
        <p>{match.moves} moves · {formatDuration(current?.elapsedSeconds ?? 0)}</p>
        <ol>
          {match.players.filter((player) => player.status !== 'open').map((player) => (
            <li className={player.isCurrentPlayer ? 'is-current' : ''} key={player.playerId}>
              <strong>{player.displayName}</strong>
              <span>{player.status === 'finished' || player.status === 'integrity-failed'
                ? `${(player.score ?? 0).toLocaleString()} pts`
                : 'Still playing'}</span>
            </li>
          ))}
        </ol>
        <div className="solitaire-results__actions">
          <button className="solitaire-primary-action" type="button" disabled={busy}
            onClick={onChooseNewGame}>New game</button>
          <button type="button" disabled={busy} onClick={onReturn}>Return</button>
        </div>
        </div>
      )}
    </div>
  )
}

function formatMilliseconds(value: number): string {
  const seconds = Math.max(0, Math.ceil(value / 1_000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

function formatElapsed(value: number): string {
  const seconds = Math.max(0, Math.floor(value / 1_000))
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

function waitForSolitaireFrame(milliseconds: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds))
}

function errorMessage(error: unknown): string {
  if (error instanceof SolitaireRuleError || error instanceof Error) return error.message
  return 'Solitaire could not complete the request.'
}
