import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { cancelSpinAnimation, finishSpinAnimation, getSlotKeyboardAction, requestQuickStop,
  startSpinAnimation, stopSpinAnimationWithCadence, usePrefersReducedMotion, useSlotAudio,
  type PaylinePayout, type ReelMotionState, type SpinAnimation } from '@fortuneforge/slot-client-core'
import { createCrookedRoadApi, CrookedRoadRequestError } from './api'
import { crookedRoadRules, crookedRoadSymbols } from './manifest'
import { CROOKED_ROAD_SOUNDS } from './sounds'
import type { RoadAction, RoadFeatureEvent } from './road'
import type { CrookedRoadGameProps, CrookedRoadState, CrookedRoadSpinResult } from './types'

const initialReels = () => crookedRoadRules.initialReels.map(reel => [...reel])
const currency = (points: number) => points * crookedRoadRules.pointValueInCents / 100
const message = (error: unknown, fallback: string) => error instanceof Error ? error.message : fallback

/** Owns paid input, recovery and presentation. Only the server decides outcomes and money. */
export function useCrookedRoad({ account, authenticatedFetch, onSpinStateChange }: CrookedRoadGameProps) {
  const api = useMemo(() => createCrookedRoadApi(authenticatedFetch, account?.userId), [authenticatedFetch, account?.userId])
  const recovery = (() => {
    try { return { pending: api.pending(), error: null } }
    catch (error) { return { pending: undefined, error: message(error, 'The saved play requires account recovery.') } }
  })()
  const audio = useSlotAudio(CROOKED_ROAD_SOUNDS)
  const reducedMotion = usePrefersReducedMotion()
  const [state, setState] = useState<CrookedRoadState | null>(null)
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [displayedReels, setDisplayedReels] = useState<string[][]>(initialReels)
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(Array(5).fill('idle'))
  const [isSpinning, setIsSpinning] = useState(false)
  const [isAutoSpinning, updateAuto] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [wagerIndex, setWagerIndex] = useState(() => Math.max(0, crookedRoadRules.wagerOptions.indexOf(300)))
  const [spinAvailability, setAvailability] = useState<'checking' | 'available' | 'unavailable'>('checking')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [isGameFeatureUpdating, setFeatureUpdating] = useState(false)
  const [gameFeatureError, setFeatureError] = useState<string | null>(null)
  const [lastFeature, setLastFeature] = useState<RoadFeatureEvent | null>(null)
  const [winningPaylines, setWinningPaylines] = useState<readonly PaylinePayout[]>([])
  const [lastWin, setLastWin] = useState(0)
  const [resultKey, setResultKey] = useState(0)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [isFeaturePresenting, setPresenting] = useState(false)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const currentApi = useRef(api)
  currentApi.current = api
  const mounted = useRef(false)
  const stateRef = useRef<CrookedRoadState | null>(null)
  const spinBusy = useRef(false)
  const actionBusy = useRef(false)
  const presenting = useRef(false)
  const auto = useRef(false)
  const stopping = useRef(false)
  const animation = useRef<SpinAnimation<string> | null>(null)
  const epoch = useRef(0)
  const spinRef = useRef<() => Promise<void>>(async () => {})
  const modalOpen = isHelpOpen || isSettingsOpen || isReloadPromptOpen
  const modalRef = useRef(modalOpen)
  modalRef.current = modalOpen
  const { playCue, startLoop, stopLoop, stopResultCues } = audio

  const setIsAutoSpinning = useCallback((enabled: boolean) => {
    if (enabled && (modalRef.current || presenting.current || actionBusy.current ||
      !stateRef.current || stateRef.current.featureActionRequired)) return
    auto.current = enabled
    updateAuto(enabled)
  }, [])
  const setFeaturePresenting = useCallback((busy: boolean) => {
    presenting.current = busy
    setPresenting(busy)
  }, [])
  const restore = useCallback((next: CrookedRoadState) => {
    if (stateRef.current && next.gameFeatureState.revision < stateRef.current.gameFeatureState.revision) return
    stateRef.current = next
    setState(next)
    if (next.slotsCreditsBalance != null) setBalance(next.slotsCreditsBalance)
    if (next.featureActionRequired) setIsAutoSpinning(false)
  }, [setIsAutoSpinning])
  const showResult = useCallback((result: CrookedRoadSpinResult) => {
    restore(result)
    setDisplayedReels(result.reels.map(reel => [...reel]))
    setWinningPaylines(result.payout.paylines)
    setLastWin(currency(result.payout.totalPoints))
    setLastFeature(result.spinFeature)
    setHasCompletedSpin(true)
    setResultKey(key => key + 1)
    if (result.spinFeature?.completed) {
      setIsAutoSpinning(false)
      setFeaturePresenting(true)
      playCue('journey-end')
    } else if (result.payout.totalPoints > 0) {
      playCue(result.payout.totalPoints >= result.wagerPoints * 5 ? 'big-win' : 'small-win')
    }
  }, [playCue, restore, setFeaturePresenting, setIsAutoSpinning])

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
    setReelMotion(Array(5).fill('idle'))
    setIsSpinning(false)
    setFeatureUpdating(false)
    setIsAutoSpinning(false)
    setFeaturePresenting(false)
    setLastFeature(null)
    setWinningPaylines([])
    setLastWin(0)
    setHasCompletedSpin(false)
    setSpinError(null)
    setFeatureError(null)
    setAvailability('checking')
    stopLoop('reel-roll')
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
          const latest = await api.getState(abort.signal)
          if (!current()) return
          if (latest.gameFeatureState.revision === recovered.gameFeatureState.revision) showResult(recovered)
          restore(latest)
        }
        if (current()) setAvailability('available')
      } catch (error) {
        if (current()) {
          setSpinError(message(error, 'The saved game could not be restored. Retry the connection.'))
          setAvailability(stateRef.current ? 'available' : 'unavailable')
        }
      } finally {
        if (current()) { spinBusy.current = false; setIsSpinning(false) }
      }
    })()
    return () => { abort.abort(); if (animation.current) cancelSpinAnimation(animation.current) }
  }, [api, reloadKey, restore, showResult, setFeaturePresenting, setIsAutoSpinning, stopLoop])
  useEffect(() => {
    onSpinStateChange?.(isSpinning)
    return () => onSpinStateChange?.(false)
  }, [isSpinning, onSpinStateChange])

  async function spin() {
    const currentState = stateRef.current
    let pending
    try { pending = api.pending() }
    catch (error) { setSpinError(message(error, 'The saved play requires account recovery.')); setIsAutoSpinning(false); return }
    if (currentApi.current !== api || spinBusy.current || actionBusy.current || presenting.current || modalRef.current ||
      spinAvailability !== 'available' || !currentState || !pending && currentState.featureActionRequired) return
    const useFreeSpin = pending?.useFreeSpin ?? currentState.freeSpinsRemaining > 0
    const wager = pending?.wagerPoints ?? (useFreeSpin ? currentState.freeSpinWagerPoints! : crookedRoadRules.wagerOptions[wagerIndex]!)
    if (!pending && !useFreeSpin && balance < currency(wager)) {
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
    startLoop('ambience')
    playCue('spin-start')
    const requestEpoch = epoch.current
    const current = () => mounted.current && epoch.current === requestEpoch && currentApi.current === api
    const ownAnimation = startSpinAnimation<string>({ reelCount: 5, rowsPerReel: 3, reducedMotion,
      speedMultiplier: auto.current ? crookedRoadRules.autoSpinSpeedMultiplier : 1,
      travelRows: crookedRoadRules.reelTravelRowCount, symbolIds: crookedRoadSymbols.animationSymbolIds ?? [],
      displayFrame: (reel, symbols) => { if (current()) setDisplayedReels(reels => reels.map((column, index) => index === reel ? [...symbols] : column)) },
      setReelMotion: (reel, motion) => { if (current()) setReelMotion(motions => motions.map((value, index) => index === reel ? motion : value)) },
    })
    animation.current = ownAnimation
    startLoop('reel-roll')
    try {
      const result = pending ? await api.resumeSpin() : await api.spin(wager, useFreeSpin)
      if (!current()) return
      await stopSpinAnimationWithCadence({ animation: ownAnimation,
        isQuickStopRequested: () => stopping.current,
        shouldSnapToTarget: () => !current(), snapToTarget: () => { if (current()) setDisplayedReels(result.reels.map(reel => [...reel])) },
        targetReels: result.reels,
        onReelStopped: () => { if (current()) playCue('reel-land') },
      })
      if (!current()) return
      if (useFreeSpin && result.freeSpinsRemaining === 0 || result.featureActionRequired) setIsAutoSpinning(false)
      showResult(result)
    } catch (error) {
      if (!current()) return
      setIsAutoSpinning(false)
      setSpinError(message(error, 'The spin could not be completed. Retry the saved spin.'))
      if (error instanceof CrookedRoadRequestError && (error.status === 402 || error.code === 'insufficient-slot-credits')) setIsReloadPromptOpen(true)
      try { const latest = await api.getState(); if (current()) restore(latest) } catch { /* Keep exact pending terms available for retry. */ }
    } finally {
      finishSpinAnimation(ownAnimation)
      if (current()) {
        animation.current = null
        spinBusy.current = false
        stopLoop('reel-roll')
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
  async function performGameFeatureAction(input: unknown) {
    if (currentApi.current !== api || spinBusy.current || actionBusy.current || modalRef.current || !stateRef.current) return
    if (api.pending()) throw new Error('Resume the saved spin before changing the road.')
    if (typeof input !== 'object' || input === null) throw new Error('Choose a road action.')
    actionBusy.current = true
    setFeatureUpdating(true)
    setFeatureError(null)
    setIsAutoSpinning(false)
    playCue('stone-step')
    startLoop('ambience')
    const requestEpoch = epoch.current
    const current = () => mounted.current && requestEpoch === epoch.current && currentApi.current === api
    try {
      const before = stateRef.current
      const next = await api.action(input as RoadAction)
      if (current()) {
        restore(next)
        if (!before.gameFeatureState.activeBonus && next.gameFeatureState.activeBonus) {
          setLastFeature(null)
          playCue('journey-start')
        }
      }
    } catch (error) {
      if (current()) setFeatureError(message(error, 'The road could not be updated.'))
      if (current() && error instanceof CrookedRoadRequestError && error.status === 409) {
        try { const latest = await api.getState(); if (current()) restore(latest) } catch { /* Keep the failed intent available for recovery. */ }
      }
      throw error
    } finally {
      if (current()) { actionBusy.current = false; setFeatureUpdating(false) }
    }
  }

  function selectWager(index: number) {
    if (recovery.error || spinBusy.current || actionBusy.current || presenting.current || stateRef.current?.freeSpinsRemaining || api.pending()) return
    if (!Number.isInteger(index) || index < 0 || index >= crookedRoadRules.wagerOptions.length) return
    setWagerIndex(index)
    playCue('stone-click')
  }
  const isFeatureActionRequired = state?.featureActionRequired === true
  const controlsDisabled = recovery.error !== null || spinAvailability !== 'available' || modalOpen || isSpinning || isGameFeatureUpdating || isFeaturePresenting
  const spinDisabled = controlsDisabled || isFeatureActionRequired && !recovery.pending
  useEffect(() => {
    if (!isAutoSpinning || controlsDisabled || isFeatureActionRequired) return
    const timer = window.setTimeout(() => { if (auto.current) void spinRef.current() }, crookedRoadRules.autoSpinDelayMs)
    return () => window.clearTimeout(timer)
  }, [isAutoSpinning, controlsDisabled, isFeatureActionRequired, resultKey, state?.gameFeatureState.revision])

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
    else if (action === 'open-help') setIsHelpOpen(true)
    else if (action === 'spin-or-stop') handleSpinButtonClick()
    else if (!controlsDisabled) selectWager(wagerIndex + (action === 'increase-wager' ? 1 : -1))
  }
  useEffect(() => {
    const handler = (event: KeyboardEvent) => keyHandler.current(event)
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [])
  function reelStripStyle(index: number): CSSProperties {
    const count = displayedReels[index]?.length ?? 3
    return { '--slot-symbol-rows': count, '--slot-strip-height': `${count / 3 * 100}%`,
      '--slot-spin-travel': `${-(count - 3) / count * 100}%` } as CSSProperties
  }
  const pending = recovery.pending
  const wagerPoints = pending?.wagerPoints ?? (state?.freeSpinsRemaining ? state.freeSpinWagerPoints! : crookedRoadRules.wagerOptions[wagerIndex]!)
  const onNavigate = () => { setIsAutoSpinning(false); if (animation.current) requestQuickStop(animation.current) }
  return { account, state, balance, displayedReels, reelMotion, reelStripStyle, isSpinning, isAutoSpinning,
    setIsAutoSpinning, isStopRequested, handleSpinButtonClick, wagerIndex, selectWager,
    wagerOptions: crookedRoadRules.wagerOptions, wagerPoints, activeWager: currency(wagerPoints),
    isWagerLocked: Boolean(recovery.error || pending || state?.freeSpinsRemaining), controlsDisabled, spinDisabled,
    spinAvailability, spinError: recovery.error ?? spinError, isGameFeatureUpdating, gameFeatureError, performGameFeatureAction,
    lastFeature, winningPaylines, resultKey, spinKey: resultKey, hasCompletedSpin, lastWin,
    isFeaturePresenting, setFeaturePresenting, isFeatureActionRequired,
    isHelpOpen, setIsHelpOpen, isSettingsOpen, setIsSettingsOpen, isReloadPromptOpen, setIsReloadPromptOpen,
    audio, reducedMotion, onNavigate, retryConnection: () => setReloadKey(key => key + 1) }
}

export type CrookedRoadController = ReturnType<typeof useCrookedRoad>
