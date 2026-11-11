import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  cancelSpinAnimation,
  finishSpinAnimation,
  getSlotKeyboardAction,
  reelLandingSettleDurationMs,
  requestQuickStop,
  shouldUseAnimatedSymbol,
  startSpinAnimation,
  stopSpinAnimationWithCadence,
  usePrefersReducedMotion,
  useSlotAudio,
  waitForPresentationFrame,
  type GridPosition,
  type ReelMotionState,
  type SpinAnimation,
} from '@fortuneforge/slot-client-core'
import { createMidnightApi, MidnightRequestError } from './api.js'
import { MIDNIGHT_VAULT_SOUNDS } from './audio.js'
import { midnightCabinet, midnightRules, midnightSymbols } from './manifest.js'
import { activeMidnightWager, emptyMidnightDemoState, emptyMidnightState } from './roundState.js'
import type {
  MidnightBonusRound,
  MidnightCollectionId,
  MidnightDemoState,
  MidnightPaylinePayout,
  MidnightSpinResult,
  MidnightState,
  MidnightSymbolId,
  MidnightVaultGameProps,
} from './types.js'

type SpinAvailability = 'checking' | 'available' | 'unavailable'
type CollectionAward = { collectionId: MidnightCollectionId; freeSpins: number; featureMode: string | null }
type WinOutcome = { kind: 'loss' | 'win' | 'great-win' | 'big-win' | 'bonus'; title: string; winTier: 'regular' | 'great' | 'big' | null }
type WinAward = { id: number; displayAmount: number; winTier: 'regular' | 'great' | 'big'; tier: 'win' | 'big' | 'jackpot'; isFlying: boolean }

const demoStartingBalance = 10_000
const manualStopBrakeDurationMs = 90
const manualStopSettleDurationMs = 35
const money = (points: number) => points * midnightRules.pointValueInCents / 100
const collectionIds: MidnightCollectionId[] = ['sync', 'rows', 'paw', 'rand']

function normalizedRows(reel: readonly MidnightSymbolId[], fallback: readonly MidnightSymbolId[], rows: number) {
  if (reel.length === rows) return [...reel]
  return Array.from({ length: rows }, (_, index) => reel[index] ?? fallback[index % fallback.length] ?? '3')
}

function bestPayline(paylines: readonly MidnightPaylinePayout[]) {
  return [...paylines].sort((left, right) => right.amountPoints - left.amountPoints || left.paylineId - right.paylineId)[0] ?? null
}

function winTier(award: number, wager: number): 'regular' | 'great' | 'big' {
  if (award >= Math.max(500, wager * 50)) return 'big'
  if (wager > 0 && award >= wager * 10) return 'great'
  return 'regular'
}

function describe(award: number, wager: number, freeSpins: number): WinOutcome {
  const tier = award > 0 ? winTier(award, wager) : null
  if (freeSpins > 0) return { kind: 'bonus', title: `${freeSpins} Vault Breach ${freeSpins === 1 ? 'spin' : 'spins'} won`, winTier: tier }
  if (award <= 0) return { kind: 'loss', title: 'Vault stays locked this spin', winTier: null }
  if (tier === 'big') return { kind: 'big-win', title: 'Midnight Vault grand haul', winTier: tier }
  if (tier === 'great') return { kind: 'great-win', title: 'Vault reward', winTier: tier }
  return { kind: 'win', title: 'Jewels secured', winTier: tier }
}

function resultSound(result: MidnightSpinResult, best: MidnightPaylinePayout | null, outcome: WinOutcome, awarded: number) {
  if (awarded > 0) return 'bonus' as const
  if (outcome.kind === 'loss') return 'no-win' as const
  if (outcome.winTier === 'big') return 'five' as const
  if (outcome.winTier === 'great') return 'premium' as const
  if (best?.matches.some(match => match.match.matchLength === result.reels.length)) return 'five' as const
  return best && best.matches.length > 1 ? 'premium' as const : 'single-three' as const
}

function collectionFromRound(round: MidnightBonusRound | undefined): MidnightCollectionId | null {
  if (!round?.id.includes(':collection:')) return null
  const id = round.id.split(':')[2]
  return collectionIds.includes(id as MidnightCollectionId) ? id as MidnightCollectionId : null
}

/** Owns Midnight sequencing; the application host supplies identity, wallet chrome and transport only. */
export function useMidnightVault({ account, demoMode = false, authenticatedFetch, onSpinStateChange }: MidnightVaultGameProps) {
  const api = useMemo(() => createMidnightApi(authenticatedFetch), [authenticatedFetch])
  const reducedMotion = usePrefersReducedMotion()
  const audio = useSlotAudio(MIDNIGHT_VAULT_SOUNDS)
  const [state, setState] = useState<MidnightState>(emptyMidnightState)
  const [demoState, setDemoState] = useState<MidnightDemoState | null>(() => demoMode ? emptyMidnightDemoState() : null)
  const [displayedReels, setDisplayedReels] = useState<MidnightSymbolId[][]>(() => midnightRules.initialReels.map(reel => [...reel]))
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(Array(5).fill('idle'))
  const [balance, setBalance] = useState(demoMode ? demoStartingBalance : account?.balances.slotsCredits ?? 0)
  const [wagerIndex, setWagerIndex] = useState(0)
  const [spinAvailability, setSpinAvailability] = useState<SpinAvailability>('checking')
  const [isSpinning, setIsSpinning] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isAutoSpinCoolingDown, setIsAutoSpinCoolingDown] = useState(false)
  const [isFastSpinActive, setIsFastSpinActive] = useState(false)
  const [spinStage, setSpinStage] = useState<'requesting' | 'stopping'>('requesting')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [lastSpinOutcomeKey, setLastSpinOutcomeKey] = useState(0)
  const [lastSpinOutcome, setLastSpinOutcome] = useState<WinOutcome | null>(null)
  const [lastWin, setLastWin] = useState(0)
  const [bestWin, setBestWin] = useState<MidnightPaylinePayout | null>(null)
  const [winningPaylines, setWinningPaylines] = useState<MidnightPaylinePayout[]>([])
  const [bonusPositions, setBonusPositions] = useState<GridPosition[]>([])
  const [specialRoundWinnings, setSpecialRoundWinnings] = useState(0)
  const [hasSpecialRoundStarted, setHasSpecialRoundStarted] = useState(false)
  const [isSpecialSpinInProgress, setIsSpecialSpinInProgress] = useState(false)
  const [isSpecialSpinDelayActive, setIsSpecialSpinDelayActive] = useState(false)
  const [collectionAwardPresentation, setCollectionAwardPresentation] = useState<CollectionAward | null>(null)
  const [heldCompletedCollectionId, setHeldCompletedCollectionId] = useState<MidnightCollectionId | null>(null)
  const [sealImpactId, setSealImpactId] = useState<MidnightCollectionId | null>(null)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isSymbolGuideOpen, setIsSymbolGuideOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const [resultAtmosphereId, setResultAtmosphereId] = useState(0)
  const [winAward, setWinAward] = useState<WinAward | null>(null)
  const helpCloseButtonRef = useRef<HTMLButtonElement>(null)
  const reloadPromptCloseButtonRef = useRef<HTMLButtonElement>(null)
  const mounted = useRef(true)
  const busy = useRef(false)
  const stopRequested = useRef(false)
  const animation = useRef<SpinAnimation<MidnightSymbolId> | null>(null)
  const specialTimer = useRef<number | null>(null)
  const impactTimer = useRef<number | null>(null)
  const handleSpinRef = useRef<() => Promise<void>>(async () => {})
  const reducedMotionRef = useRef(reducedMotion)
  reducedMotionRef.current = reducedMotion

  const selectedWagerPoints = midnightRules.wagerOptions[wagerIndex]
  const activeWagerPoints = activeMidnightWager(state, selectedWagerPoints)
  const activeWagerDisplay = money(activeWagerPoints)
  const useFreeSpin = state.freeSpinsRemaining > 0
  const isSpecialGameActive = hasSpecialRoundStarted && (useFreeSpin || isSpecialSpinInProgress)
  const midnightBonusVisible = useFreeSpin || isSpecialGameActive
  const blocked = isHelpOpen || isSettingsOpen || isSymbolGuideOpen || isReloadPromptOpen

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      if (animation.current) cancelSpinAnimation(animation.current)
      if (specialTimer.current !== null) window.clearTimeout(specialTimer.current)
      if (impactTimer.current !== null) window.clearTimeout(impactTimer.current)
    }
  }, [])
  useEffect(() => { onSpinStateChange?.(isSpinning); return () => onSpinStateChange?.(false) }, [isSpinning, onSpinStateChange])

  useEffect(() => {
    const abort = new AbortController()
    setSpinAvailability('checking')
    const load = demoMode ? api.availability(abort.signal) : api.state().then(loaded => {
      if (abort.signal.aborted) return
      setState(loaded)
      if (loaded.freeSpinWagerPoints !== null) {
        const index = midnightRules.wagerOptions.indexOf(loaded.freeSpinWagerPoints)
        if (index >= 0) setWagerIndex(index)
      }
      const rows = loaded.freeSpinFeatureMode === 'rows-paw' && loaded.freeSpinsRemaining > 0 ? 6 : 4
      setDisplayedReels(current => current.map((reel, index) => normalizedRows(reel, midnightRules.initialReels[index], rows)))
    })
    void load.then(() => { if (!abort.signal.aborted) setSpinAvailability('available') }, () => {
      if (!abort.signal.aborted) { setSpinAvailability('unavailable'); setIsAutoSpinning(false) }
    })
    return () => abort.abort()
  }, [api, demoMode])

  const { playCue, playSequence, startLoop, stopLoop, stopResultCues, toggleMuted } = audio
  useEffect(() => {
    const startMusic = () => { if (!document.hidden) startLoop(MIDNIGHT_VAULT_SOUNDS.events.ambience) }
    startMusic()
    const gesture = (event: Event) => { if (event.isTrusted) startMusic() }
    document.addEventListener('pointerdown', gesture)
    document.addEventListener('keydown', gesture)
    document.addEventListener('visibilitychange', startMusic)
    window.addEventListener('focus', startMusic)
    return () => {
      document.removeEventListener('pointerdown', gesture)
      document.removeEventListener('keydown', gesture)
      document.removeEventListener('visibilitychange', startMusic)
      window.removeEventListener('focus', startMusic)
      stopLoop(MIDNIGHT_VAULT_SOUNDS.events.ambience)
    }
  }, [audio.preferences.mode, startLoop, stopLoop])

  useEffect(() => {
    const click = (event: MouseEvent) => {
      if (!event.isTrusted || !(event.target instanceof Element)) return
      const control = event.target.closest('button, summary, [role="button"]')
      if (!control || control.matches('.spin-button, :disabled, [aria-disabled="true"]') || control.closest('[inert]')) return
      playCue('button-click')
    }
    document.addEventListener('click', click, true)
    return () => document.removeEventListener('click', click, true)
  }, [playCue])

  useEffect(() => {
    if (!isAutoSpinCoolingDown) return
    const timer = window.setTimeout(() => setIsAutoSpinCoolingDown(false), midnightRules.autoSpinDelayMs)
    return () => window.clearTimeout(timer)
  }, [isAutoSpinCoolingDown])

  useEffect(() => {
    if (!isAutoSpinning || spinAvailability !== 'available' || busy.current || isSpinning || isAutoSpinCoolingDown ||
      blocked || collectionAwardPresentation || isSpecialGameActive) return
    if (!useFreeSpin && balance < money(selectedWagerPoints)) {
      setIsAutoSpinning(false); setSpinError(null); setIsReloadPromptOpen(true); return
    }
    const timer = window.setTimeout(() => { void handleSpinRef.current() }, 0)
    return () => window.clearTimeout(timer)
  }, [balance, blocked, collectionAwardPresentation, isAutoSpinCoolingDown, isAutoSpinning, isSpecialGameActive, isSpinning, selectedWagerPoints, spinAvailability, useFreeSpin])

  useEffect(() => {
    const canQueue = isSpecialGameActive && state.freeSpinsRemaining > 0 && !isSpinning && !blocked && !collectionAwardPresentation
    if (!canQueue) {
      if (specialTimer.current !== null) window.clearTimeout(specialTimer.current)
      specialTimer.current = null
      setIsSpecialSpinDelayActive(false)
      return
    }
    if (specialTimer.current !== null) return
    setIsSpecialSpinDelayActive(true)
    specialTimer.current = window.setTimeout(() => {
      specialTimer.current = null
      setIsSpecialSpinDelayActive(false)
      void handleSpinRef.current()
    }, midnightRules.specialSpinDelayMs)
    return () => {
      if (specialTimer.current !== null) window.clearTimeout(specialTimer.current)
      specialTimer.current = null
    }
  }, [blocked, collectionAwardPresentation, isSpecialGameActive, isSpinning, state.freeSpinsRemaining])

  const setImpact = (id: MidnightCollectionId | null) => {
    if (impactTimer.current !== null) window.clearTimeout(impactTimer.current)
    setSealImpactId(id)
    if (id) impactTimer.current = window.setTimeout(() => { impactTimer.current = null; setSealImpactId(null) }, 650)
  }

  const changeWager = useCallback((direction: -1 | 1) => {
    if (state.freeSpinsRemaining > 0) { setSpinError('Vault Breach spins are locked to the wager that won them.'); return }
    setIsAutoSpinning(false)
    setWagerIndex(current => Math.min(midnightRules.wagerOptions.length - 1, Math.max(0, current + direction)))
    setSpinError(null)
  }, [state.freeSpinsRemaining])

  const selectWager = useCallback((nextIndex: number) => {
    if (state.freeSpinsRemaining > 0) { setSpinError('Vault Breach spins are locked to the wager that won them.'); return }
    setIsAutoSpinning(false)
    setWagerIndex(Math.min(midnightRules.wagerOptions.length - 1, Math.max(0, Math.round(nextIndex))))
    setSpinError(null)
  }, [state.freeSpinsRemaining])

  async function handleSpin() {
    if (blocked || busy.current || isSpinning || collectionAwardPresentation || spinAvailability !== 'available') return
    const expectedFreeSpin = state.freeSpinsRemaining > 0
    const wagerPoints = activeMidnightWager(state, selectedWagerPoints)
    const wagerRand = money(wagerPoints)
    if (!expectedFreeSpin && balance < wagerRand) {
      setIsAutoSpinning(false); setSpinError(null); setIsReloadPromptOpen(true); return
    }
    const fast = isAutoSpinning
    const expectedRows = expectedFreeSpin && state.freeSpinFeatureMode === 'rows-paw' ? 6 : 4
    const reelsBefore = midnightRules.initialReels.map((fallback, index) => normalizedRows(displayedReels[index] ?? fallback, fallback, expectedRows))
    const optimisticCharge = expectedFreeSpin ? 0 : wagerRand
    const visibleBalanceBeforeAward = balance - optimisticCharge
    busy.current = true
    stopRequested.current = false
    setIsStopRequested(false)
    setIsFastSpinActive(fast)
    setBalance(current => current - optimisticCharge)
    if (expectedFreeSpin) {
      setState(current => ({ ...current, freeSpinsRemaining: Math.max(0, current.freeSpinsRemaining - 1) }))
      setHasSpecialRoundStarted(true)
      setIsSpecialSpinInProgress(true)
    }
    setLastSpinOutcome(null); setLastWin(0); setBestWin(null); setWinningPaylines([]); setBonusPositions([])
    setHasCompletedSpin(false); setSpinError(null); setWinAward(null)
    if (!fast && !expectedFreeSpin) playCue(MIDNIGHT_VAULT_SOUNDS.events.leverPull)
    const spinCue = expectedFreeSpin ? MIDNIGHT_VAULT_SOUNDS.events.specialReelSpin ?? MIDNIGHT_VAULT_SOUNDS.events.reelSpin : MIDNIGHT_VAULT_SOUNDS.events.reelSpin
    const stopCue = expectedFreeSpin ? MIDNIGHT_VAULT_SOUNDS.events.specialReelStop ?? MIDNIGHT_VAULT_SOUNDS.events.reelStop : MIDNIGHT_VAULT_SOUNDS.events.reelStop
    startLoop(spinCue)
    setIsSpinning(true)
    setSpinStage('requesting')
    setDisplayedReels(reelsBefore)
    setReelMotion(Array(5).fill('idle'))
    const currentAnimation = startSpinAnimation({
      reelCount: 5, rowsPerReel: expectedRows, symbolIds: midnightSymbols.animationSymbolIds ?? [],
      displayFrame: (reelIndex, symbols) => setDisplayedReels(current => current.map((reel, index) => index === reelIndex ? [...symbols] : reel)),
      setReelMotion: (reelIndex, motion) => setReelMotion(current => current.map((value, index) => index === reelIndex ? motion : value)),
      reducedMotion: reducedMotionRef.current, speedMultiplier: fast ? midnightRules.autoSpinSpeedMultiplier : midnightRules.baseSpinSpeedMultiplier,
      travelRows: midnightRules.reelTravelRowCount,
    })
    animation.current = currentAnimation
    const settleReels = async (target: readonly (readonly MidnightSymbolId[])[]) => {
      await stopSpinAnimationWithCadence({
        animation: currentAnimation, isQuickStopRequested: () => stopRequested.current,
        onReelStopped: (_, stopped) => { playCue(stopCue); if (stopped === target.length) stopLoop(spinCue) },
        quickBrakeDurationMs: manualStopBrakeDurationMs, quickSettleAfterStopMs: manualStopSettleDurationMs,
        settleAfterLastReelMs: reelLandingSettleDurationMs / (fast ? midnightRules.autoSpinSpeedMultiplier : midnightRules.baseSpinSpeedMultiplier),
        shouldSnapToTarget: () => reducedMotionRef.current,
        snapToTarget: () => { setDisplayedReels(target.map(reel => [...reel])); setReelMotion(Array(5).fill('stopped')); stopLoop(spinCue) },
        targetReels: target,
      })
    }
    let resultStarted = false
    try {
      const result = await api.spin({ wagerPoints, useFreeSpin: expectedFreeSpin, state, demoState }, demoMode)
      if (result.reels.some(reel => reel.length !== expectedRows)) throw new Error(`Expected ${expectedRows} visible rows per reel but received an invalid slot grid.`)
      setSpinStage('stopping')
      resultStarted = true
      await settleReels(result.reels)
      if (!mounted.current) return
      const awarded = result.awardedBonusRounds.reduce((total, round) => total + round.totalSpins, 0) || result.freeSpinsAwarded
      const awardRand = money(result.payout.totalPoints)
      const outcome = describe(awardRand, wagerRand, awarded)
      const best = bestPayline(result.payout.paylines)
      const collectionRound = result.awardedBonusRounds.find(round =>
        round.id === result.bonusRound?.id && round.id.includes(':collection:'))
      const completedCollection = collectionFromRound(collectionRound)
      const triggerPositions = result.freeSpinsAwarded > 0 ? result.reels.flatMap((reel, reelIndex) =>
        reel.flatMap((symbol, row) => symbol === 'FREE' ? [{ reel: reelIndex, row }] : [])) : []
      setState({
        bonusRound: result.bonusRound, queuedBonusRounds: result.queuedBonusRounds,
        freeSpinsRemaining: result.freeSpinsRemaining, freeSpinWagerPoints: result.freeSpinsRemaining > 0 ? result.freeSpinWagerPoints ?? result.wagerPoints : null,
        freeSpinFeatureMode: result.freeSpinFeatureMode, specialPointsBalance: result.specialPointsBalance,
        energyBalance: result.energyBalance, sealCollections: result.sealCollections,
      })
      if (demoMode) setDemoState(result.midnightBonusState ?? demoState)
      setDisplayedReels(result.reels.map(reel => [...reel]))
      setBestWin(best); setWinningPaylines(result.payout.paylines); setBonusPositions(triggerPositions)
      setLastWin(awardRand); setLastSpinOutcome(outcome); setHasCompletedSpin(true)
      setLastSpinOutcomeKey(current => current + 1); setResultAtmosphereId(current => current + 1)
      if (expectedFreeSpin) setSpecialRoundWinnings(current => current + awardRand)
      else if (awarded > 0) setSpecialRoundWinnings(0)
      setBalance(result.slotsCreditsBalance ?? visibleBalanceBeforeAward + awardRand)
      if (awardRand > 0) setWinAward({ id: Date.now(), displayAmount: awardRand,
        winTier: outcome.winTier ?? 'regular', tier: awardRand >= Math.max(500, wagerRand * 50) ? 'jackpot' : awardRand >= Math.max(25, wagerRand * 10) ? 'big' : 'win', isFlying: false })
      const impacted = collectionIds.find(id => (result.sealsAwarded[`SEAL_${id === 'sync' ? 'SYNC' : id === 'rows' ? 'ROWS' : id === 'paw' ? 'PAW' : 'RAND'}`] ?? 0) > 0) ?? null
      setImpact(impacted)
      if (completedCollection && !result.isFreeSpin) {
        setIsAutoSpinning(false)
        setHasSpecialRoundStarted(true)
        setHeldCompletedCollectionId(completedCollection)
        setCollectionAwardPresentation({ collectionId: completedCollection, freeSpins: collectionRound?.totalSpins ?? awarded, featureMode: collectionRound?.featureMode ?? result.freeSpinFeatureMode })
      }
      const event = resultSound(result, best, outcome, awarded)
      if (!stopRequested.current && (!fast || event !== 'no-win') && await waitForPresentationFrame(new AbortController().signal)) {
        playSequence((expectedFreeSpin ? MIDNIGHT_VAULT_SOUNDS.events.specialResults?.[event] : undefined) ?? MIDNIGHT_VAULT_SOUNDS.events.results[event])
      }
      setIsAutoSpinCoolingDown(fast)
    } catch (error) {
      setIsAutoSpinning(false)
      if (demoMode) setSpinAvailability('unavailable')
      const insufficient = error instanceof MidnightRequestError && error.code === 'insufficient-slot-credits'
      const freeUnavailable = error instanceof MidnightRequestError && error.code === 'free-spins-unavailable'
      if (insufficient) setIsReloadPromptOpen(true)
      if (!resultStarted) {
        setSpinStage('stopping')
        try { await settleReels(reelsBefore) } catch { setDisplayedReels(reelsBefore) }
      } else setDisplayedReels(reelsBefore)
      if (expectedFreeSpin && !freeUnavailable) setState(current => ({ ...current, freeSpinsRemaining: current.freeSpinsRemaining + 1 }))
      if (freeUnavailable) {
        setState(current => ({ ...current, bonusRound: null, queuedBonusRounds: [],
          freeSpinsRemaining: error.freeSpinsRemaining ?? 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null }))
        setHeldCompletedCollectionId(null)
        setSpecialRoundWinnings(0)
        setHasSpecialRoundStarted(false)
      }
      if (insufficient && error.available !== undefined) setBalance(error.available)
      else setBalance(current => current + optimisticCharge)
      setSpinError(error instanceof Error ? error.message : 'The spin could not be completed.')
      setBestWin(null); setWinningPaylines([]); setBonusPositions([]); setLastSpinOutcome(null); setWinAward(null)
    } finally {
      stopLoop(spinCue)
      if (mounted.current) {
        finishSpinAnimation(currentAnimation)
        setIsSpinning(false); setIsFastSpinActive(false); setIsStopRequested(false); setIsSpecialSpinInProgress(false)
      } else cancelSpinAnimation(currentAnimation)
      if (animation.current === currentAnimation) animation.current = null
      stopRequested.current = false
      busy.current = false
    }
  }
  handleSpinRef.current = handleSpin

  const handleSpinButtonClick = useCallback(() => {
    if (blocked) return
    if (busy.current || isSpinning) {
      stopRequested.current = true
      if (animation.current) requestQuickStop(animation.current)
      stopResultCues(); setIsStopRequested(true); setSpinStage('stopping'); setIsAutoSpinning(false); setIsFastSpinActive(true)
      return
    }
    if (collectionAwardPresentation) { setCollectionAwardPresentation(null); return }
    if (spinAvailability !== 'available') return
    if (isSpecialSpinDelayActive) {
      if (specialTimer.current !== null) window.clearTimeout(specialTimer.current)
      specialTimer.current = null; setIsSpecialSpinDelayActive(false)
    }
    void handleSpinRef.current()
  }, [blocked, collectionAwardPresentation, isSpecialSpinDelayActive, isSpinning, spinAvailability, stopResultCues])

  useEffect(() => {
    const keyboard = (event: KeyboardEvent) => {
      const target = event.target instanceof Element ? event.target : null
      const action = getSlotKeyboardAction(event.key, { hasOpenDialog: blocked,
        isEditableTarget: target?.closest('a, button, input, select, textarea, summary, [contenteditable="true"], [role="button"], [role="slider"]') !== null,
        modified: event.altKey || event.ctrlKey || event.metaKey, repeated: event.repeat })
      if (!action) return
      event.preventDefault()
      if (action === 'spin-or-stop') handleSpinButtonClick()
      else if (action === 'decrease-wager') changeWager(-1)
      else if (action === 'increase-wager') changeWager(1)
      else if (action === 'toggle-mute') toggleMuted()
      else { setIsAutoSpinning(false); setIsHelpOpen(true) }
    }
    window.addEventListener('keydown', keyboard)
    return () => window.removeEventListener('keydown', keyboard)
  }, [blocked, changeWager, handleSpinButtonClick, toggleMuted])

  useEffect(() => {
    if (!useFreeSpin && !isSpecialSpinInProgress) {
      setHasSpecialRoundStarted(false)
      setHeldCompletedCollectionId(null)
      setSpecialRoundWinnings(0)
    }
  }, [isSpecialSpinInProgress, useFreeSpin])

  useEffect(() => {
    if (!winAward) return
    const timer = window.setTimeout(() => setWinAward(current => current?.id === winAward.id ? null : current), 1_250)
    return () => window.clearTimeout(timer)
  }, [winAward])

  const reelStripStyle = (reelIndex: number) => {
    const symbolRows = Math.max(1, displayedReels[reelIndex]?.length ?? 4)
    const moving = ['accelerating', 'spinning', 'braking'].includes(reelMotion[reelIndex])
    const visibleRows = moving && symbolRows > midnightRules.reelTravelRowCount ? symbolRows - midnightRules.reelTravelRowCount : symbolRows
    return { '--slot-symbol-rows': symbolRows, '--slot-strip-height': `${symbolRows / visibleRows * 100}%`,
      '--slot-spin-travel': `${-Math.max(0, symbolRows - visibleRows) / symbolRows * 100}%` } as CSSProperties
  }
  const pageStyle = {
    '--slot-page-backdrop': midnightCabinet.backdropImageSet ?? `url("${midnightCabinet.pageBackdropImage}")`,
    '--slot-result-primary': midnightCabinet.palette.trimBright,
    '--slot-result-secondary': midnightCabinet.palette.accent,
    '--slot-result-glow': midnightCabinet.palette.glow,
  } as CSSProperties
  const winningPositions = [...(bestWin?.matches.flatMap(match => match.match.positions) ?? []), ...bonusPositions]
  const slotsPageClassName = ['slots-page', isFastSpinActive ? 'slots-page--fast-spin' : '',
    isSpecialGameActive ? 'slots-page--special-game' : '', 'slots-page--theme-backdrop'].filter(Boolean).join(' ')

  return {
    account, activeWagerDisplay, audio, balance, bestWin, blocked, changeWager, collectionAwardPresentation,
    demoMode, displayedReels, handleSpinButtonClick, hasCompletedSpin, heldCompletedCollectionId,
    helpCloseButtonRef, isAutoSpinning, isFastSpinActive, isHelpOpen, isReloadPromptOpen,
    isSettingsOpen, isSpecialGameActive, isSpecialSpinDelayActive, isSpinning, isStopRequested,
    isSymbolGuideOpen, lastSpinOutcome, lastSpinOutcomeKey, lastWin, midnightBonusVisible,
    pageStyle, reducedMotion, reelMotion, reelStripStyle, reloadPromptCloseButtonRef, resultAtmosphereId,
    sealImpactId, selectWager, selectedWagerPoints, setCollectionAwardPresentation, setIsAutoSpinning,
    setIsHelpOpen, setIsReloadPromptOpen, setIsSettingsOpen, setIsSymbolGuideOpen, setSpinError,
    slotsPageClassName, specialRoundWinnings, spinAvailability, spinError, spinStage, state, wagerIndex,
    winAward, winningPaylines, winningPositions,
  }
}

export type MidnightVaultController = ReturnType<typeof useMidnightVault>
export { shouldUseAnimatedSymbol }
