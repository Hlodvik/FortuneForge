import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { cancelSpinAnimation, finishSpinAnimation, getSlotKeyboardAction, requestQuickStop, startSpinAnimation,
  stopSpinAnimationWithCadence, usePrefersReducedMotion, useSlotAudio,
  type PaylinePayout, type ReelMotionState, type SpinAnimation } from '@fortuneforge/slot-client-core'
import { createPharaohApi, PharaohRequestError } from './api'
import { COSMIC_FORTUNE_EXPERIENCE_SET as experience } from './manifest'
import { COSMIC_FORTUNE_SOUNDS as sounds } from './sounds'
import { PHARAOH_WAGERING } from './wagering'
import type { PharaohFeature } from './pharaoh'
import { isTombAction, type TombAction } from './tomb'
import type { PharaohAscentGameProps, PharaohSlotState, PharaohSpinResult } from './types'

const rules = experience.rules
const initialReels = () => rules.initialReels.map(reel => [...reel])
const amount = (points: number) => points * rules.pointValueInCents / 100
const errorMessage = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback
const defaultWagerIndex = rules.wagerOptions.indexOf(PHARAOH_WAGERING.defaultWagerPoints)

/** Production paid input and recovery; the server owns every reel and expedition outcome. */
export function usePharaohAscent({ account, authenticatedFetch, onSpinStateChange }: PharaohAscentGameProps) {
  const api = useMemo(() => createPharaohApi(authenticatedFetch, account?.userId), [authenticatedFetch, account?.userId])
  const audio = useSlotAudio(sounds)
  const reducedMotion = usePrefersReducedMotion()
  const [state, setState] = useState<PharaohSlotState | null>(null)
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [displayedReels, setDisplayedReels] = useState<string[][]>(initialReels)
  const [visibleRows, setVisibleRows] = useState(4)
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(Array(5).fill('idle'))
  const [isSpinning, setIsSpinning] = useState(false)
  const [isAutoSpinning, updateAuto] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [wagerIndex, setWagerIndex] = useState(defaultWagerIndex)
  const [spinAvailability, setAvailability] = useState<'checking' | 'available' | 'unavailable'>('checking')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [isGameFeatureUpdating, setFeatureUpdating] = useState(false)
  const [gameFeatureError, setFeatureError] = useState<string | null>(null)
  const [pendingGameAction, setPendingGameAction] = useState<TombAction | null>(null)
  const [lastFeature, setLastFeature] = useState<PharaohFeature | null>(null)
  const [winningPaylines, setWinningPaylines] = useState<readonly PaylinePayout[]>([])
  const [lastWin, setLastWin] = useState(0)
  const [resultKey, setResultKey] = useState(0)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const currentApi = useRef(api)
  currentApi.current = api
  const mounted = useRef(false)
  const epoch = useRef(0)
  const stateRef = useRef<PharaohSlotState | null>(null)
  const spinBusy = useRef(false)
  const actionBusy = useRef(false)
  const auto = useRef(false)
  const stopping = useRef(false)
  const animation = useRef<SpinAnimation<string> | null>(null)
  const spinRef = useRef<() => Promise<void>>(async () => {})
  const modalOpen = isHelpOpen || isSettingsOpen || isReloadPromptOpen
  const modalRef = useRef(modalOpen)
  modalRef.current = modalOpen
  const { playCue, playSequence, startLoop, stopLoop, stopResultCues } = audio

  const setIsAutoSpinning = useCallback((enabled: boolean) => {
    if (enabled && (modalRef.current || actionBusy.current || !stateRef.current ||
      stateRef.current.featureActionRequired || currentApi.current.pendingAction())) return
    auto.current = enabled
    updateAuto(enabled)
  }, [])
  const restore = useCallback((next: PharaohSlotState) => {
    if (stateRef.current && next.gameFeatureState.tomb.revision < stateRef.current.gameFeatureState.tomb.revision) return
    stateRef.current = next
    setState(next)
    if (next.slotsCreditsBalance !== null) setBalance(next.slotsCreditsBalance)
    setPendingGameAction(currentApi.current.pendingAction())
    if (next.featureActionRequired || currentApi.current.pendingAction()) setIsAutoSpinning(false)
  }, [setIsAutoSpinning])
  const showResult = useCallback((result: PharaohSpinResult) => {
    restore(result)
    setDisplayedReels(result.reels.map(reel => [...reel]))
    setVisibleRows(result.reels[0].length)
    setWinningPaylines(result.payout.paylines.filter(line => line.paylineId <= experience.help.paylineCount))
    setLastWin(amount(result.payout.totalPoints))
    setLastFeature(result.spinFeature)
    setHasCompletedSpin(true)
    setResultKey(key => key + 1)
    const resultKind = result.freeSpinsAwarded > 0 ? 'bonus' : result.payout.totalPoints >= result.wagerPoints * 5 ? 'five'
      : result.payout.totalPoints > 0 ? 'single-three' : 'no-win'
    playSequence(sounds.events.results[resultKind] ?? [])
    if (result.featureActionRequired) setIsAutoSpinning(false)
  }, [playSequence, restore, setIsAutoSpinning])

  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false; epoch.current++; if (animation.current) cancelSpinAnimation(animation.current) }
  }, [])
  useEffect(() => {
    const requestEpoch = ++epoch.current
    const abort = new AbortController()
    const current = () => mounted.current && !abort.signal.aborted && epoch.current === requestEpoch && currentApi.current === api
    spinBusy.current = false
    actionBusy.current = false
    stateRef.current = null
    setState(null)
    setBalance(account?.balances.slotsCredits ?? 0)
    setDisplayedReels(initialReels())
    setVisibleRows(4)
    setReelMotion(Array(5).fill('idle'))
    setIsSpinning(false)
    setFeatureUpdating(false)
    setIsAutoSpinning(false)
    setLastFeature(null)
    setWinningPaylines([])
    setLastWin(0)
    setHasCompletedSpin(false)
    setSpinError(null)
    setFeatureError(null)
    setPendingGameAction(null)
    setAvailability('checking')
    stopLoop('ambience')
    stopLoop('special-ambience')
    stopLoop('reel-spin')
    stopLoop('special-reel-spin')
    if (animation.current) { cancelSpinAnimation(animation.current); animation.current = null }
    void (async () => {
      try {
        const loaded = await api.getState(abort.signal)
        if (!current()) return
        restore(loaded)
        if (api.pending()) {
          spinBusy.current = true
          setIsSpinning(true)
          const recovered = await api.resumeSpin()
          if (!current()) return
          showResult(recovered)
          const latest = await api.getState(abort.signal)
          if (!current()) return
          restore(latest)
        }
        if (current()) setAvailability('available')
      } catch (error) {
        if (current()) {
          setSpinError(errorMessage(error, 'Your saved game could not be restored. Reconnect to try again.'))
          setAvailability(stateRef.current ? 'available' : 'unavailable')
          setPendingGameAction(api.pendingAction())
        }
      } finally {
        if (current()) { spinBusy.current = false; setIsSpinning(false) }
      }
    })()
    return () => { abort.abort(); if (animation.current) cancelSpinAnimation(animation.current) }
  }, [api, reloadKey, restore, showResult, setIsAutoSpinning, stopLoop])
  useEffect(() => {
    onSpinStateChange?.(isSpinning || isGameFeatureUpdating)
    return () => onSpinStateChange?.(false)
  }, [isSpinning, isGameFeatureUpdating, onSpinStateChange])

  async function spin() {
    const currentState = stateRef.current
    const pending = api.pending()
    if (currentApi.current !== api || spinBusy.current || actionBusy.current || modalRef.current ||
      spinAvailability !== 'available' || !currentState || api.pendingAction() || !pending && currentState.featureActionRequired) return
    const useFreeSpin = pending?.useFreeSpin ?? currentState.freeSpinsRemaining > 0
    const wager = pending?.wagerPoints ?? (useFreeSpin && currentState.freeSpinWagerPoints !== null
      ? currentState.freeSpinWagerPoints : rules.wagerOptions[wagerIndex]!)
    if (!pending && !useFreeSpin && balance < amount(wager)) {
      setIsAutoSpinning(false); setIsReloadPromptOpen(true); return
    }
    spinBusy.current = true
    stopping.current = false
    setIsStopRequested(false)
    setIsSpinning(true)
    setSpinError(null)
    setFeatureError(null)
    setWinningPaylines([])
    setHasCompletedSpin(false)
    stopResultCues()
    stopLoop(useFreeSpin ? 'ambience' : 'special-ambience')
    startLoop(useFreeSpin ? 'special-ambience' : 'ambience')
    playCue('lever-pull')
    const requestEpoch = epoch.current
    const current = () => mounted.current && epoch.current === requestEpoch && currentApi.current === api
    const rows = useFreeSpin && currentState.freeSpinFeatureMode?.split('-').includes('rows') ? 6 : 4
    setVisibleRows(rows)
    const ownAnimation = startSpinAnimation<string>({ reelCount: 5, rowsPerReel: rows, reducedMotion,
      speedMultiplier: auto.current ? rules.autoSpinSpeedMultiplier : rules.baseSpinSpeedMultiplier,
      travelRows: rules.reelTravelRowCount, symbolIds: experience.symbols.animationSymbolIds ?? [],
      displayFrame: (reel, symbols) => { if (current()) setDisplayedReels(reels => reels.map((column, index) => index === reel ? [...symbols] : column)) },
      setReelMotion: (reel, motion) => { if (current()) setReelMotion(motions => motions.map((value, index) => index === reel ? motion : value)) },
    })
    animation.current = ownAnimation
    const loop = useFreeSpin ? 'special-reel-spin' : 'reel-spin'
    startLoop(loop)
    try {
      const result = pending ? await api.resumeSpin() : await api.spin(wager, useFreeSpin)
      if (!current()) return
      await stopSpinAnimationWithCadence({ animation: ownAnimation, isQuickStopRequested: () => stopping.current,
        shouldSnapToTarget: () => !current(), snapToTarget: () => { if (current()) setDisplayedReels(result.reels.map(reel => [...reel])) },
        targetReels: result.reels, onReelStopped: () => { if (current()) playCue(useFreeSpin ? 'special-reel-stop' : 'reel-stop') },
      })
      if (!current()) return
      if (useFreeSpin && result.freeSpinsRemaining === 0 || result.featureActionRequired) setIsAutoSpinning(false)
      showResult(result)
    } catch (error) {
      if (!current()) return
      setIsAutoSpinning(false)
      setSpinError(errorMessage(error, 'The spin could not be completed. Resume your saved spin.'))
      if (error instanceof PharaohRequestError && (error.status === 402 || error.code === 'insufficient-slot-credits')) setIsReloadPromptOpen(true)
      try { const latest = await api.getState(); if (current()) restore(latest) } catch { /* Keep the exact prepared operation available. */ }
    } finally {
      finishSpinAnimation(ownAnimation)
      if (current()) {
        animation.current = null
        spinBusy.current = false
        stopLoop(loop)
        setReelMotion(Array(5).fill('idle'))
        setIsSpinning(false)
        setIsStopRequested(false)
      }
    }
  }
  spinRef.current = spin
  function handleSpinButtonClick() {
    if (spinBusy.current) {
      if (animation.current) { stopping.current = true; setIsStopRequested(true); requestQuickStop(animation.current) }
      return
    }
    void spin()
  }
  async function performGameFeatureAction(action: unknown) {
    if (!isTombAction(action)) throw new Error('Choose an available expedition action.')
    if (currentApi.current !== api || spinBusy.current || actionBusy.current || modalRef.current || !stateRef.current) return
    if (api.pending()) throw new Error('Resume your saved spin before exploring the tomb.')
    actionBusy.current = true
    setFeatureUpdating(true)
    setFeatureError(null)
    setIsAutoSpinning(false)
    playCue('button-click')
    const requestEpoch = epoch.current
    const current = () => mounted.current && requestEpoch === epoch.current && currentApi.current === api
    try {
      const previous = stateRef.current
      const next = await api.action(action)
      if (current()) {
        restore(next)
        if (!previous.gameFeatureState.tomb.activeRun && next.gameFeatureState.tomb.activeRun) playCue('bonus-entry')
        if (next.gameFeatureState.tomb.lastRun && next.gameFeatureState.tomb.lastRun.id !== previous.gameFeatureState.tomb.lastRun?.id) {
          setLastWin(amount(next.gameFeatureState.tomb.lastRun.payoutPoints))
          playCue('bonus-complete')
        }
      }
    } catch (error) {
      if (current()) {
        setFeatureError(errorMessage(error, 'The expedition could not be updated. Resume the saved action.'))
        try { const latest = await api.getState(); if (current()) restore(latest) } catch { /* Retain action identity for a safe retry. */ }
      }
      throw error
    } finally {
      if (current()) { actionBusy.current = false; setFeatureUpdating(false); setPendingGameAction(api.pendingAction()) }
    }
  }
  function selectWager(index: number) {
    if (spinBusy.current || actionBusy.current || modalRef.current || stateRef.current?.freeSpinsRemaining ||
      stateRef.current?.featureActionRequired || api.pending() || api.pendingAction()) return
    if (!Number.isInteger(index) || index < 0 || index >= rules.wagerOptions.length) return
    setWagerIndex(index)
    playCue('bet-step')
  }
  const isFeatureActionRequired = state?.featureActionRequired === true || pendingGameAction !== null
  const controlsDisabled = spinAvailability !== 'available' || modalOpen || isSpinning || isGameFeatureUpdating
  const spinDisabled = controlsDisabled || isFeatureActionRequired && !api.pending()
  useEffect(() => {
    if (!isAutoSpinning || controlsDisabled || isFeatureActionRequired) return
    const timer = window.setTimeout(() => { if (auto.current) void spinRef.current() }, rules.autoSpinDelayMs)
    return () => window.clearTimeout(timer)
  }, [isAutoSpinning, controlsDisabled, isFeatureActionRequired, resultKey, state?.gameFeatureState.tomb.revision])

  const keyHandler = useRef<(event: KeyboardEvent) => void>(() => {})
  keyHandler.current = event => {
    const target = event.target instanceof Element ? event.target : null
    const control = target?.closest('button,summary,[role="button"],a')
    if (control && control.getAttribute('data-slot-keyboard-controls') !== 'cabinet') return
    const action = getSlotKeyboardAction(event.key, { hasOpenDialog: modalOpen,
      isEditableTarget: Boolean(target?.closest('input,select,textarea,[contenteditable="true"]')),
      modified: event.altKey || event.ctrlKey || event.metaKey, repeated: event.repeat })
    if (!action) return
    event.preventDefault()
    if (action === 'toggle-mute') audio.toggleMuted()
    else if (action === 'open-help') { setIsAutoSpinning(false); setIsHelpOpen(true) }
    else if (action === 'spin-or-stop') handleSpinButtonClick()
    else if (!controlsDisabled) selectWager(wagerIndex + (action === 'increase-wager' ? 1 : -1))
  }
  useEffect(() => {
    const handler = (event: KeyboardEvent) => keyHandler.current(event)
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [])
  function reelStripStyle(index: number): CSSProperties {
    const count = displayedReels[index]?.length ?? visibleRows
    return { '--slot-symbol-rows': visibleRows, '--slot-strip-height': `${count / visibleRows * 100}%`,
      '--slot-spin-travel': `${-(count - visibleRows) / count * 100}%` } as CSSProperties
  }
  const pending = api.pending()
  const wagerPoints = pending?.wagerPoints ?? (state?.freeSpinsRemaining && state.freeSpinWagerPoints !== null
    ? state.freeSpinWagerPoints : rules.wagerOptions[wagerIndex]!)
  const onNavigate = () => { setIsAutoSpinning(false); if (animation.current) requestQuickStop(animation.current) }
  const reload = () => setReloadKey(key => key + 1)
  return { account, state, balance, displayedReels, reelMotion, reelStripStyle, visibleRows, isSpinning, isAutoSpinning,
    setIsAutoSpinning, isStopRequested, handleSpinButtonClick, wagerIndex, selectWager,
    wagerOptions: rules.wagerOptions, wagerPoints, activeWager: amount(wagerPoints),
    isWagerLocked: Boolean(pending || state?.freeSpinsRemaining || isFeatureActionRequired), controlsDisabled, spinDisabled,
    spinAvailability, spinError, isGameFeatureUpdating, gameFeatureError, pendingGameAction, performGameFeatureAction,
    lastFeature, winningPaylines, resultKey, spinKey: resultKey, hasCompletedSpin, lastWin, isFeatureActionRequired,
    isHelpOpen, setIsHelpOpen, isSettingsOpen, setIsSettingsOpen, isReloadPromptOpen, setIsReloadPromptOpen,
    audio, reducedMotion, onNavigate, reload, retryConnection: reload }
}
export type PharaohAscentController = ReturnType<typeof usePharaohAscent>
