import { useCallback, useEffect, useRef, useState } from 'react'
import { GameOutcomeBanner } from '../../../components/GameOutcomeBanner'
import type { AccountSummary } from '../../../features/account/services/accountsApi'
import { InGameShell } from '../../../components/InGameShell'
import { GameLoadingScreen } from '../../../features/game-loading/GameLoadingScreen'
import { resolveGameLoadingEntry } from '../../../features/game-loading/gameLoadingRegistry'
import {
  cancelSolitaireQueue,
  claimSolitaireResult,
  dismissSolitaireResult,
  getSolitaireHistoryPage,
  getSolitaireSession,
  isUncertainSolitaireFailure,
  joinSolitaireQueue,
  postCommandWithReconciliation,
  SolitaireRequestError,
  stableSolitaireMutation,
  type PendingSolitaireMutation,
} from '../../../games/cards/solitaire/solitaireApi'
import {
  completeSolitaireFreeRun,
  createSolitaireFreeRunRequestId,
  setSolitaireFreeRunPaused,
  startSolitaireFreeRun,
  type SolitaireFreeReplayCommand,
  type SolitaireFreeRun,
} from '../../../games/cards/solitaire/solitaireFreeRunApi'
import { SolitaireBoard } from '../../../games/cards/solitaire/SolitaireBoard'
import {
  applyLocalSolitaireCommand,
  autoFinishLocalSolitaire,
  createLocalSolitaireGame,
  isLocalSolitaireWon,
  projectRedactedDraw,
  SolitaireRuleError,
} from '../../../games/cards/solitaire/solitaireEngine'
import { useCardAudioClick } from '../../../games/cards/shared/cardAudio'
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
  type SolitaireHistoryItem,
  type SolitaireMatchSession,
  type SolitaireMutationResponse,
  type SolitairePlayerCount,
  type SolitaireQueueSession,
  type SolitaireResultSession,
  type SolitaireSession,
} from '../../../games/cards/solitaire/solitaireTypes'
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
  freePauseBusy: boolean
  freeComplete: boolean
  freeAutoWinning: boolean
  freeSubmitting: boolean
  freeSubmissionError: string | null
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

type FreeHistoryEntry = Readonly<{
  game: SolitaireGame
  commands: readonly SolitaireFreeReplayCommand[]
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
  const [freePauseBusy, setFreePauseBusy] = useState(false)
  const [freeComplete, setFreeComplete] = useState(false)
  const [freeAutoWinning, setFreeAutoWinning] = useState(false)
  const [freeSubmitting, setFreeSubmitting] = useState(false)
  const [freeSubmissionError, setFreeSubmissionError] = useState<string | null>(null)
  const [freeSetupOpen, setFreeSetupOpen] = useState(false)
  const [competitiveSetupMatchId, setCompetitiveSetupMatchId] = useState<string | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [history, setHistory] = useState<readonly SolitaireHistoryItem[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyLoadingMore, setHistoryLoadingMore] = useState(false)
  const [historyNextCursor, setHistoryNextCursor] = useState<string | null>(null)
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [historyRefresh, setHistoryRefresh] = useState(0)
  const [claimingHistoryMatchId, setClaimingHistoryMatchId] = useState<string | null>(null)
  const [freeElapsedMilliseconds, setFreeElapsedMilliseconds] = useState(0)
  const [freeHistory, setFreeHistory] = useState<readonly FreeHistoryEntry[]>([])
  const freeRunRef = useRef<SolitaireFreeRun | null>(null)
  const historyOlderAbortRef = useRef<AbortController | null>(null)
  const freeCommandsRef = useRef<readonly SolitaireFreeReplayCommand[]>([])
  const freeStartRequestRef = useRef<{
    drawCount: SolitaireDrawCount
    idempotencyKey: string
    replayRunId: string | null
  } | null>(null)
  const onCardAudioClick = useCardAudioClick()
  const serverMatchRef = useRef<SolitaireMatchSession | null>(null)
  const moveQueueRef = useRef<QueuedSolitaireCommand[]>([])
  const processingMovesRef = useRef(false)
  const deferredCommandRef = useRef(false)
  const autoWinAnimationRef = useRef<Promise<void> | null>(null)
  const autoWinGenerationRef = useRef(0)
  const freeAutoWinGenerationRef = useRef(0)
  const sessionReadGenerationRef = useRef(0)

  useEffect(() => {
    if (freeGame === null || freePaused || freeComplete || freeAutoWinning || freeSubmitting) return
    let previous = Date.now()
    const timer = window.setInterval(() => {
      const now = Date.now()
      setFreeElapsedMilliseconds((value) => value + now - previous)
      previous = now
    }, 250)
    return () => window.clearInterval(timer)
  }, [freeGame, freePaused, freeComplete, freeAutoWinning, freeSubmitting])

  const loadSession = useCallback(async (quiet = false, signal?: AbortSignal) => {
    // A read started before a mutation must not restore an older queue or board.
    const generation = sessionReadGenerationRef.current
    if (!quiet) setAvailability({ kind: 'loading' })
    try {
      const session = await getSolitaireSession(signal)
      if (signal?.aborted || generation !== sessionReadGenerationRef.current) return
      if (moveQueueRef.current.length > 0 || processingMovesRef.current) return
      if (session.kind === 'match' && serverMatchRef.current?.matchId === session.matchId
        && session.version < serverMatchRef.current.version) return
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
      if (signal?.aborted || generation !== sessionReadGenerationRef.current) return
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
    return () => controller.abort()
  }, [loadSession])

  useEffect(() => {
    if (!historyOpen) return
    const controller = new AbortController()
    const readHistory = async () => {
      try {
        const page = await getSolitaireHistoryPage(30, undefined, controller.signal)
        if (controller.signal.aborted) return
        setHistory(page.items)
        setHistoryNextCursor(page.nextCursor)
        setHistoryError(null)
      } catch (error) {
        if (controller.signal.aborted) return
        setHistoryError(errorMessage(error))
      } finally {
        if (!controller.signal.aborted) setHistoryLoading(false)
      }
    }
    setHistoryLoading(true)
    void readHistory()
    return () => {
      controller.abort()
      historyOlderAbortRef.current?.abort()
    }
  }, [historyOpen, historyRefresh])

  const loadOlderHistory = async () => {
    if (historyNextCursor === null || historyLoading || historyLoadingMore) return
    const controller = new AbortController()
    historyOlderAbortRef.current = controller
    setHistoryLoadingMore(true)
    try {
      const page = await getSolitaireHistoryPage(30, historyNextCursor, controller.signal)
      if (controller.signal.aborted) return
      setHistory((current) => {
        const seen = new Set(current.map((item) => item.matchId))
        return [...current, ...page.items.filter((item) => !seen.has(item.matchId))]
      })
      setHistoryNextCursor(page.nextCursor)
      setHistoryError(null)
    } catch (error) {
      if (!controller.signal.aborted) setHistoryError(errorMessage(error))
    } finally {
      if (historyOlderAbortRef.current === controller) {
        historyOlderAbortRef.current = null
        setHistoryLoadingMore(false)
      }
    }
  }

  const sessionKind = availability.kind === 'ready' ? availability.session.kind : null
  useEffect(() => {
    if (sessionKind === null || sessionKind === 'idle' || busy || pending !== null) return
    const controller = new AbortController()
    const interval = sessionKind === 'queued' ? 1_250 : 15_000
    let stopped = false
    let timer = 0
    let inFlight: Promise<void> | null = null
    const refresh = () => {
      if (stopped || document.visibilityState !== 'visible') return Promise.resolve()
      if (inFlight !== null) return inFlight
      inFlight = loadSession(true, controller.signal).finally(() => { inFlight = null })
      return inFlight
    }
    const poll = async () => {
      await refresh()
      if (!stopped) timer = window.setTimeout(poll, interval)
    }
    timer = window.setTimeout(poll, interval)
    window.addEventListener('focus', refresh)
    window.addEventListener('online', refresh)
    return () => {
      stopped = true
      controller.abort()
      window.clearTimeout(timer)
      window.removeEventListener('focus', refresh)
      window.removeEventListener('online', refresh)
    }
  }, [sessionKind, busy, pending, loadSession])

  const beginMutation = (fingerprint: string): PendingSolitaireMutation | null => {
    if (pending !== null && pending.fingerprint !== fingerprint) {
      setRequestError('Finish or retry the pending request before choosing another action.')
      return null
    }
    const next = stableSolitaireMutation(pending, fingerprint)
    sessionReadGenerationRef.current += 1
    setPending(next)
    setRequestError(null)
    return next
  }

  const acceptMutation = (mutation: SolitaireMutationResponse) => {
    sessionReadGenerationRef.current += 1
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
        sessionReadGenerationRef.current += 1
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
      sessionReadGenerationRef.current += 1
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
  const restartCompetitive = async (_matchId: string) => {
    const fingerprint = `join:${playerCount}:${buyInCredits}:${drawCount}`
    const request = beginMutation(fingerprint)
    if (request === null) return
    setCompetitiveSetupMatchId(null)
    setBusy(true)
    try {
      acceptMutation(await joinSolitaireQueue(
        playerCount,
        buyInCredits,
        drawCount,
        request.idempotencyKey,
      ))
      setHistoryRefresh((value) => value + 1)
    } catch (error) {
      rejectMutation(error)
    } finally {
      setBusy(false)
    }
  }
  const claimHistory = async (matchId: string) => {
    if (claimingHistoryMatchId !== null) return
    const request = beginMutation(`claim:${matchId}`)
    if (request === null) return
    setClaimingHistoryMatchId(matchId)
    try {
      const mutation = await claimSolitaireResult(matchId, request.idempotencyKey)
      setBalanceCredits(mutation.balanceCredits)
      setPending(null)
      setRequestError(null)
      setHistory((items) => items.map((item) => item.matchId === matchId
        ? { ...item, canClaim: false, claimStatus: 'completed' }
        : item))
    } catch (error) {
      rejectMutation(error)
    } finally {
      setClaimingHistoryMatchId(null)
    }
  }
  const claim = (result: SolitaireResultSession) => void runMutation(
    `claim:${result.matchId}`,
    (key) => claimSolitaireResult(result.matchId, key),
  )
  const resetFreeGame = (run: SolitaireFreeRun) => {
    freeAutoWinGenerationRef.current += 1
    freeRunRef.current = run
    freeCommandsRef.current = []
    setDrawCount(run.drawCount)
    setFreeGame(createLocalSolitaireGame(run.seed, run.drawCount))
    setFreePaused(false)
    setFreePauseBusy(false)
    setFreeComplete(false)
    setFreeAutoWinning(false)
    setFreeSubmitting(false)
    setFreeSubmissionError(null)
    setFreeSetupOpen(false)
    setFreeElapsedMilliseconds(0)
    setFreeHistory([])
    setRequestError(null)
  }
  const startFree = async (replaySource: SolitaireFreeRun | null = null) => {
    const requestedDrawCount = replaySource?.drawCount ?? drawCount
    const replayRunId = replaySource?.runId ?? null
    const existing = freeStartRequestRef.current
    const request = existing?.drawCount === requestedDrawCount && existing.replayRunId === replayRunId
      ? existing
      : { drawCount: requestedDrawCount, idempotencyKey: createSolitaireFreeRunRequestId(), replayRunId }
    freeStartRequestRef.current = request
    setBusy(true)
    setRequestError(null)
    try {
      const run = await startSolitaireFreeRun(request.drawCount, request.idempotencyKey, request.replayRunId ?? undefined)
      freeStartRequestRef.current = null
      resetFreeGame(run)
    } catch (error) {
      setRequestError(errorMessage(error))
    } finally {
      setBusy(false)
    }
  }
  const replayFree = () => {
    const run = freeRunRef.current
    if (run !== null) void startFree(run)
  }
  const toggleFreePause = async () => {
    const run = freeRunRef.current
    if (run === null || freePauseBusy || freeComplete || freeAutoWinning || freeSubmitting) return
    const pause = !freePaused
    setFreePauseBusy(true)
    setRequestError(null)
    try {
      const result = await setSolitaireFreeRunPaused(run.runId, pause)
      if (freeRunRef.current?.runId !== run.runId) return
      setFreeElapsedMilliseconds(result.elapsedMilliseconds)
      setFreePaused(result.isPaused)
    } catch (error) {
      setRequestError(errorMessage(error))
    } finally {
      setFreePauseBusy(false)
    }
  }
  const submitFreeRun = async (commands = freeCommandsRef.current) => {
    const run = freeRunRef.current
    if (run === null || freeSubmitting || freePauseBusy) return
    const wasPaused = freePaused
    setFreeSubmitting(true)
    setFreePaused(true)
    setFreeSubmissionError(null)
    try {
      const result = await completeSolitaireFreeRun(run.runId, commands)
      setFreeGame((game) => game === null ? null : {
        ...game,
        score: result.score,
        moves: result.moves,
        message: result.terminal === 'won' ? 'Deck completed!' : 'Game submitted',
      })
      setFreeElapsedMilliseconds(result.elapsedMilliseconds)
      setFreeComplete(true)
    } catch (error) {
      setFreeSubmissionError(errorMessage(error))
      setFreePaused(wasPaused)
    } finally {
      setFreeSubmitting(false)
    }
  }
  const animateFreeAutoWin = (
    startingGame: SolitaireGame,
    commands: readonly Extract<SolitaireCommand, { type: 'move' }>[],
    replayCommands: readonly SolitaireFreeReplayCommand[],
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
      void submitFreeRun(replayCommands)
    })()
  }
  const freeCommand = (nextCommand: SolitaireCommand) => {
    if (freeGame === null || freePaused || freePauseBusy || freeComplete || freeAutoWinning || freeSubmitting) return
    if (nextCommand.type !== 'draw' && nextCommand.type !== 'flip' && nextCommand.type !== 'move') return
    try {
      const next = applyLocalSolitaireCommand(freeGame, nextCommand)
      const autoFinished = autoFinishLocalSolitaire(next)
      const automatic = autoFinished.commands as readonly Extract<SolitaireCommand, { type: 'move' }>[]
      const replayCommands = [...freeCommandsRef.current, nextCommand, ...automatic]
      setFreeHistory((history) => [...history.slice(-49), {
        game: freeGame,
        commands: freeCommandsRef.current,
      }])
      freeCommandsRef.current = replayCommands
      setFreeSubmissionError(null)
      if (isLocalSolitaireWon(autoFinished.game)) {
        animateFreeAutoWin(next, automatic, replayCommands)
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
    freeCommandsRef.current = previous.commands
    setFreeGame(previous.game)
    setFreeHistory((history) => history.slice(0, -1))
    setFreeSubmissionError(null)
    setRequestError(null)
  }
  const refresh = () => {
    setPending(null)
    setRequestError(null)
    void loadSession(false)
  }

  const activePaidRun = availability.kind === 'ready'
    && availability.session.kind === 'match'
    && availability.session.players.some((player) => player.isCurrentPlayer && player.status === 'playing')

  useEffect(() => {
    if (activePaidRun) setHistoryOpen(false)
  }, [activePaidRun])

  return (
    <InGameShell
      account={{ ...account, balances: { ...account.balances, slotsCredits: balanceCredits } }}
      title="Competitive Solitaire"
      theme="cards"
      bodyClassName="solitaire-shell-body"
    >
      <div className="solitaire-page" onClickCapture={onCardAudioClick}>
      {requestError && (
        <div className="solitaire-request-error" role="alert">
          <span>{requestError}</span>
          <button type="button" onClick={refresh}>Refresh game</button>
        </div>
      )}
      {availability.kind === 'ready' && !activePaidRun && !busy && (
        <div className="solitaire-history-access">
          <button type="button" onClick={() => {
            setHistory([])
            setHistoryNextCursor(null)
            setHistoryError(null)
            setHistoryOpen(true)
          }}>Match history</button>
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
        freePauseBusy={freePauseBusy}
        freeComplete={freeComplete}
        freeAutoWinning={freeAutoWinning}
        freeSubmitting={freeSubmitting}
        freeSubmissionError={freeSubmissionError}
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
        onStartFree={() => void startFree()}
        onReplayFree={replayFree}
        onChooseNewFreeGame={() => setFreeSetupOpen(true)}
        onCancelFreeSetup={() => setFreeSetupOpen(false)}
        onFreeCommand={freeCommand}
        onFreePause={() => void toggleFreePause()}
        onFreeUndo={undoFree}
        onFreeSubmit={() => void submitFreeRun()}
        onExitFree={() => {
          freeAutoWinGenerationRef.current += 1
          freeRunRef.current = null
          freeCommandsRef.current = []
          setFreeGame(null)
          setFreeSetupOpen(false)
          setFreeAutoWinning(false)
          setFreeSubmitting(false)
          setFreeSubmissionError(null)
        }}
        onRefresh={refresh}
        />
      {historyOpen && !activePaidRun && (
        <SolitaireHistoryPanel
          items={history}
          loading={historyLoading}
          loadingMore={historyLoadingMore}
          hasMore={historyNextCursor !== null}
          error={historyError}
          claimingMatchId={claimingHistoryMatchId}
          onClose={() => setHistoryOpen(false)}
          onRefresh={() => setHistoryRefresh((value) => value + 1)}
          onLoadMore={() => void loadOlderHistory()}
          onClaim={(matchId) => void claimHistory(matchId)}
        />
      )}
      </div>
    </InGameShell>
  )
}

export function SolitaireContent(props: SolitaireContentProps) {
  if (props.freeGame !== null) return <FreePanel {...props} game={props.freeGame} />
  const { availability } = props
  if (availability.kind === 'loading') return <GameLoadingScreen entry={resolveGameLoadingEntry('/cards/solitaire')} />
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
  void queue
  return (
    <section className="solitaire-panel solitaire-queue" role="status">
      <h2>Starting match…</h2>
      <button type="button" disabled={busy} onClick={onCancel}>{busy ? 'Leaving…' : 'Cancel'}</button>
    </section>
  )
}

export function SolitaireHistoryPanel({ items, loading, loadingMore = false, hasMore = false, error, claimingMatchId, onClose, onRefresh, onLoadMore, onClaim }: {
  items: readonly SolitaireHistoryItem[]
  loading: boolean
  loadingMore?: boolean
  hasMore?: boolean
  error: string | null
  claimingMatchId: string | null
  onClose: () => void
  onRefresh: () => void
  onLoadMore?: () => void
  onClaim: (matchId: string) => void
}) {
  const closeRef = useRef<HTMLButtonElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose
  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    closeRef.current?.focus()
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab' || dialogRef.current === null) return
      const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])',
      ))
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (first === undefined || last === undefined) {
        event.preventDefault()
        return
      }
      if (event.shiftKey && (document.activeElement === first || !dialogRef.current.contains(document.activeElement))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (document.activeElement === last || !dialogRef.current.contains(document.activeElement))) {
        event.preventDefault()
        first.focus()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      if (previousFocus?.isConnected) previousFocus.focus()
    }
  }, [])

  return (
    <div ref={dialogRef} className="solitaire-history-layer" role="dialog" aria-modal="true" aria-labelledby="solitaire-history-title">
      <section className="solitaire-history">
        <header className="solitaire-history__header">
          <h2 id="solitaire-history-title">Match history</h2>
          <div>
            <button type="button" onClick={onRefresh} disabled={loading}>Refresh</button>
            <button ref={closeRef} type="button" onClick={onClose} aria-label="Close match history">×</button>
          </div>
        </header>
        {error && <p className="solitaire-history__error" role="alert">{error}</p>}
        {loading && items.length === 0 ? <p role="status">Loading matches…</p> : null}
        {!loading && items.length === 0 && !error ? <p>No matches yet.</p> : null}
        <ol>
          {items.map((item) => (
            <li key={item.matchId}>
              <div className="solitaire-history__summary">
                <strong>{new Date(item.startedAtUtc).toLocaleString()}</strong>
                <span>{item.status === 'settled' ? 'Settled' : 'In progress'}</span>
              </div>
              <ol className="solitaire-history__seats">
                {item.players.map((player, index) => (
                  <li key={`${item.matchId}-${player.seat}`} className={player.isCurrentPlayer ? 'is-current' : ''}>
                    <span>#{index + 1}</span>
                    <strong>{player.status === 'open' ? 'Waiting for players...' : player.displayName}</strong>
                    {player.status === 'finished' && player.score != null
                      ? <small>{player.score.toLocaleString()} pts</small>
                      : null}
                  </li>
                ))}
              </ol>
              {item.status === 'settled' && (
                <div className="solitaire-history__outcome">
                  <span>#{item.placement} · {item.score.toLocaleString()} pts</span>
                  <span>{item.payoutCredits > 0 ? `R${item.payoutCredits.toFixed(2)} won` : 'No payout'}</span>
                  {item.canClaim && item.claimStatus !== 'completed' && (
                    <button type="button" disabled={claimingMatchId !== null}
                      onClick={() => onClaim(item.matchId)}>
                      {claimingMatchId === item.matchId ? 'Claiming…' : 'Claim reward'}
                    </button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ol>
        {hasMore && onLoadMore && (
          <button type="button" className="solitaire-history__more" onClick={onLoadMore}
            disabled={loading || loadingMore}>
            {loadingMore ? 'Loading…' : 'Older matches'}
          </button>
        )}
      </section>
    </div>
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
  const [hintRequestId, setHintRequestId] = useState(0)
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
        <div className="solitaire-match__metric"><span>Score</span><strong>{match.score.toLocaleString()}</strong></div>
        <div className="solitaire-match__metric"><span>Moves</span><strong>{match.moves}</strong></div>
        <div className="solitaire-match__metric"><span>Time</span><strong><RemainingTime match={match} /></strong></div>
        <SolitaireBoardUtilities
          undoDisabled={busy || !playing || !match.canUndo || match.isPaused || autoWinning || warning !== null}
          hintDisabled={busy || !playing || match.isPaused || autoWinning || warning !== null}
          onUndo={() => onCommand({ type: 'undo' })}
          onHint={() => setHintRequestId((value) => value + 1)}
        />
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
        busy={busy || !playing || match.isPaused || autoWinning || warning !== null}
        hintRequestId={hintRequestId} onCommand={onCommand} />
      <div className="solitaire-match__controls">
        {playing && !autoWinning && warning === null ? (
          <>
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

function SolitaireBoardUtilities({
  undoDisabled,
  hintDisabled,
  onUndo,
  onHint,
}: {
  undoDisabled: boolean
  hintDisabled: boolean
  onUndo: () => void
  onHint: () => void
}) {
  return (
    <div className="solitaire-match__utilities" role="group" aria-label="Board controls">
      <button type="button" aria-label="Undo" title="Undo" disabled={undoDisabled} onClick={onUndo}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M9 14 4 9l5-5" /><path d="M4 9h9a7 7 0 0 1 0 14h-2" />
        </svg>
        <span>Undo</span>
      </button>
      <button type="button" aria-label="Hint" title="Hint" disabled={hintDisabled} onClick={onHint}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M9 18h6m-5 3h4m-5.3-6.5A7 7 0 1 1 15.3 14c-.8.7-1.3 1.4-1.3 2.5h-4c0-1.1-.5-1.8-1.3-2.5Z" />
        </svg>
        <span>Hint</span>
      </button>
    </div>
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
  const [hintRequestId, setHintRequestId] = useState(0)
  return (
    <main className="solitaire-main">
      <section className="solitaire-match solitaire-match--free">
        <div className="solitaire-match__status">
          <div className="solitaire-match__metric"><span>Score</span><strong>{props.game.score.toLocaleString()}</strong></div>
          <div className="solitaire-match__metric"><span>Moves</span><strong>{props.game.moves}</strong></div>
          <div className="solitaire-match__metric"><span>Time</span><strong>{formatElapsed(props.freeElapsedMilliseconds)}</strong></div>
          <SolitaireBoardUtilities
            undoDisabled={!props.freeCanUndo || props.freePaused || props.freePauseBusy || props.freeComplete || props.freeAutoWinning || props.freeSubmitting}
            hintDisabled={props.freePaused || props.freePauseBusy || props.freeComplete || props.freeAutoWinning || props.freeSubmitting}
            onUndo={props.onFreeUndo}
            onHint={() => setHintRequestId((value) => value + 1)}
          />
        </div>
        <SolitaireBoard game={props.game} autoWinning={props.freeAutoWinning}
          busy={props.freePaused || props.freePauseBusy || props.freeComplete || props.freeAutoWinning || props.freeSubmitting}
          hintRequestId={hintRequestId}
          onCommand={props.onFreeCommand} />
        <div className="solitaire-match__controls">
          <button type="button" disabled={props.freePauseBusy || props.freeComplete || props.freeAutoWinning || props.freeSubmitting}
            onClick={props.onFreePause}>{props.freePaused ? 'Resume' : 'Pause'}</button>
          <button className="solitaire-primary-action" type="button"
            disabled={props.freePauseBusy || props.freeComplete || props.freeAutoWinning || props.freeSubmitting}
            onClick={props.onFreeSubmit}>{props.freeSubmitting ? 'Saving…' : 'Submit game'}</button>
        </div>
        {props.freeSubmissionError && (
          <div className="solitaire-request-error" role="alert">
            <span>{props.freeSubmissionError}</span>
            <button type="button" disabled={props.freeSubmitting} onClick={props.onFreeSubmit}>Retry submission</button>
          </div>
        )}
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
                  <button className="solitaire-primary-action" type="button" disabled={props.busy} onClick={props.onStartFree}>Start new game</button>
                  <button type="button" onClick={props.onCancelFreeSetup}>Back</button>
                </div>
              </div>
            ) : (
              <div>
                <p className="solitaire-eyebrow">Game complete · deck cleared</p>
                <h2 id="free-result-title">{props.game.score.toLocaleString()} points</h2>
                <GameOutcomeBanner
                  className="solitaire-result-dialog__outcome"
                  label="Deck cleared"
                  title="Every card reached a foundation"
                  detail={`${props.game.score.toLocaleString()} points · ${props.game.moves} moves · ${formatElapsed(props.freeElapsedMilliseconds)}`}
                  nextAction="Replay this draw, choose a new game, or return to the card room."
                  tone="win"
                  significance="major"
                />
                <div className="solitaire-results__actions solitaire-results__actions--three">
                  <button className="solitaire-primary-action" type="button" disabled={props.busy} onClick={props.onReplayFree}>Replay</button>
                  <button type="button" disabled={props.busy} onClick={props.onChooseNewFreeGame}>New game</button>
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
      <GameOutcomeBanner
        className="solitaire-results__outcome"
        label={won ? 'Reward earned' : 'Match settled'}
        title={won && current
          ? `R${current.payoutCredits.toFixed(2)} ready to claim`
          : current
            ? `You finished in place #${current.rank}`
            : 'Your result is ready'}
        detail={current
          ? `${current.score.toLocaleString()} points in ${current.moves} moves.`
          : 'Review the final standings before you leave the table.'}
        nextAction={won ? 'Claim your reward, then return to the card room.' : 'Accept this result, then return to the card room.'}
        tone={won ? 'win' : 'neutral'}
        significance={won ? 'major' : 'standard'}
      />
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
  const hasOpenSeats = match.players.some((player) => player.status === 'open')
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
        <GameOutcomeBanner
          className="solitaire-result-dialog__outcome"
          label={integrityFailed ? 'Game ended' : 'Your run is complete'}
          title={integrityFailed ? 'This result cannot be scored' : hasOpenSeats ? 'Waiting for players...' : 'Waiting for results'}
          detail={integrityFailed
            ? 'No reward was issued for this run.'
            : `${match.score.toLocaleString()} points in ${match.moves} moves.`}
          nextAction="Start a new game or return to the card room."
          tone={integrityFailed ? 'loss' : 'milestone'}
          significance="standard"
        />
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
