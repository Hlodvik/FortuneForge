import { useCallback, useEffect, useRef, useState, type CSSProperties } from 'react'
import {
  cancelSpinAnimation,
  finishSpinAnimation,
  reelLandingSettleDurationMs,
  requestQuickStop,
  startSpinAnimation,
  stopSpinAnimationWithCadence,
  type SpinAnimation,
  type ReelMotionState,
} from './animation/slotAnimation'
import { AudioSettingsDialog, SlotMachine, SlotSymbol, SpinButton, SymbolValueGuide } from './components'
import { FIVE_MATCH_PATTERNS, THREE_MATCH_PATTERNS } from './config/paylinePatterns'
import {
  DEFAULT_SLOT_EXPERIENCE_SET,
  type SlotExperienceSet,
} from './config/slotExperienceSets'
import type { SlotResultSoundEvent } from './config/soundSets'
import { useMascotPerformance, type MascotOutcome } from './hooks/useMascotPerformance'
import { usePrefersReducedMotion } from './hooks/usePrefersReducedMotion'
import { useSlotAudio } from './hooks/useSlotAudio'
import {
  SlotStateRevisionGuard,
  shouldUseAnimatedSymbol,
  waitForPresentation,
  waitForPresentationFrame,
} from './presentation/spinLifecycle'
import { findBestPayline, selectWinSoundEvent } from './presentation/spinPresentation'
import { requestSlotState, requestSpin, SpinRequestError } from './services/slotsApi'
import type {
  GridPosition,
  PaylinePayout,
  SlotSealCollection,
  SlotSymbolId,
  SpinResult,
} from './types/slots'
import { ForgeCoin } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../components/WukongCompanion'
import type { AccountSummary } from '../landing/services/accountsApi'

const creditFormatter = new Intl.NumberFormat('en-US')
const formatRand = (amount: number) => `R${creditFormatter.format(amount)}`

const defaultSealCollections: SlotSealCollection[] = [
  { sealId: 'sync', count: 0, averageWagerPoints: 0, requiredCount: 44 },
  { sealId: 'rows', count: 0, averageWagerPoints: 0, requiredCount: 44 },
  { sealId: 'paw', count: 0, averageWagerPoints: 0, requiredCount: 44 },
  { sealId: 'rand', count: 0, averageWagerPoints: 0, requiredCount: 44 },
]

const sealLabels: Readonly<Record<string, { label: string; shortLabel: string; symbol: SlotSymbolId }>> = {
  sync: { label: 'Sync reels', shortLabel: 'Sync', symbol: 'SEAL_SYNC' },
  rows: { label: '+2 rows', shortLabel: '+2 rows', symbol: 'SEAL_ROWS' },
  paw: { label: 'Monkey paw odds', shortLabel: 'Paws', symbol: 'SEAL_PAW' },
  rand: { label: 'Rand column', shortLabel: 'Rand', symbol: 'SEAL_RAND' },
}

type EnergyFlyover = {
  id: number
  left: number
  top: number
  width: number
  height: number
  travelX: number
  travelY: number
  durationMs: number
}

type WinAwardFlyover = {
  id: number
  amount: number
  displayAmount: number
  isBigWin: boolean
  isFlying: boolean
  left: number
  top: number
  travelX: number
  travelY: number
  durationMs: number
}

const manualStopBrakeDurationMs = 90
const manualStopSettleDurationMs = 35
const regularWinHoldDurationMs = 380
const regularWinBalanceCountDurationMs = 420
const bigWinMinimumPoints = 500
const bigWinMultiplier = 50
const bigWinCountDurationMs = 1250
const bigWinBalanceCountDurationMs = 720
const winFlyoverDurationMs = 540
const autoSpinWinPresentationMaxMultiplier = 1.35

type SlotsPageProps = {
  account: AccountSummary
  experienceSet?: SlotExperienceSet
  onSpinStateChange?: (isSpinning: boolean) => void
}

export function SlotsPage({
  account,
  experienceSet = DEFAULT_SLOT_EXPERIENCE_SET,
  onSpinStateChange,
}: SlotsPageProps) {
  // The page coordinates gameplay. Presentation assets and tunable rules enter
  // through this single composed set rather than direct file imports.
  const {
    cabinet: cabinetTheme,
    mascot: mascotSet,
    rules,
    sounds: soundSet,
    symbols: symbolSet,
  } = experienceSet
  const {
    autoSpinDelayMs,
    autoSpinSpeedMultiplier,
    energyMeterCapacity,
    gameId,
    initialReels,
    wagerOptions,
  } = rules
  const {
    preferences: audioPreferences,
    playCue,
    playSequence,
    setVolume,
    startLoop,
    stopLoop,
    stopResultCues,
    toggleMuted,
    toggleResultsOnly,
  } = useSlotAudio(soundSet)
  const prefersReducedMotion = usePrefersReducedMotion()
  const {
    actionKey: mascotActionKey,
    beginPerformance,
    completePerformance,
    phase: mascotPhase,
    successFrame: mascotSuccessFrame,
  } = useMascotPerformance(mascotSet)
  const [displayedReels, setDisplayedReels] = useState<SlotSymbolId[][]>(() =>
    initialReels.map((reel) => [...reel]),
  )
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(() =>
    initialReels.map(() => 'idle'),
  )
  const [isSpinning, setIsSpinning] = useState(false)
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isAutoSpinCoolingDown, setIsAutoSpinCoolingDown] = useState(false)
  const [isFastSpinActive, setIsFastSpinActive] = useState(false)
  const [spinStage, setSpinStage] = useState<'requesting' | 'stopping'>('requesting')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [bestWin, setBestWin] = useState<PaylinePayout | null>(null)
  const [bonusPositions, setBonusPositions] = useState<GridPosition[]>([])
  const [balance, setBalance] = useState(account.balances.slotsCredits)
  const [lastWin, setLastWin] = useState(0)
  const [lastFreeSpinsAwarded, setLastFreeSpinsAwarded] = useState(0)
  const [lastEnergyAwarded, setLastEnergyAwarded] = useState(0)
  const [lastEnergyMultiplierApplied, setLastEnergyMultiplierApplied] = useState(false)
  const [freeSpinsRemaining, setFreeSpinsRemaining] = useState(0)
  const [freeSpinWagerPoints, setFreeSpinWagerPoints] = useState<number | null>(null)
  const [isFreeSpinBadgePopping, setIsFreeSpinBadgePopping] = useState(false)
  const [energyBalance, setEnergyBalance] = useState(0)
  const [sealCollections, setSealCollections] =
    useState<SlotSealCollection[]>(defaultSealCollections)
  const [energyFlyover, setEnergyFlyover] = useState<EnergyFlyover | null>(null)
  const [energyImpactKey, setEnergyImpactKey] = useState(0)
  const [winAwardFlyover, setWinAwardFlyover] = useState<WinAwardFlyover | null>(null)
  const [wagerIndex, setWagerIndex] = useState(0)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const spinInProgressRef = useRef(false)
  const stopSpinRequestedRef = useRef(false)
  const prefersReducedMotionRef = useRef(prefersReducedMotion)
  const presentationAbortControllerRef = useRef<AbortController | null>(null)
  const activeSpinAnimationRef = useRef<SpinAnimation | null>(null)
  const slotStateRevisionGuardRef = useRef(new SlotStateRevisionGuard())
  const isMountedRef = useRef(true)
  const handleSpinRef = useRef<() => Promise<void>>(async () => undefined)
  const helpCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const reloadPromptCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const energyMeterRef = useRef<HTMLDivElement | null>(null)
  const creditTileRef = useRef<HTMLDivElement | null>(null)
  const freeSpinBadgeTimerRef = useRef<number | null>(null)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const selectedWager = wagerOptions[wagerIndex] ?? wagerOptions[0] ?? 0
  const expectedServerSymbolSetId = symbolSet.serverSymbolSetId ?? symbolSet.id
  const useFreeGameForNextSpin = freeSpinsRemaining > 0
  const closeSettings = useCallback(() => setIsSettingsOpen(false), [])

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
      presentationAbortControllerRef.current?.abort()
      if (activeSpinAnimationRef.current !== null) {
        cancelSpinAnimation(activeSpinAnimationRef.current)
        activeSpinAnimationRef.current = null
      }
      stopResultCues()
    }
  }, [stopResultCues])

  useEffect(() => {
    prefersReducedMotionRef.current = prefersReducedMotion
    if (!prefersReducedMotion || !spinInProgressRef.current) {
      return
    }

    stopSpinRequestedRef.current = true
    presentationAbortControllerRef.current?.abort()
    stopResultCues()
    stopLoop(soundSet.events.reelSpin)
    if (activeSpinAnimationRef.current !== null) {
      cancelSpinAnimation(activeSpinAnimationRef.current)
      activeSpinAnimationRef.current = null
    }
    setIsStopRequested(true)
    setIsFastSpinActive(true)
    setReelMotion((current) => current.map(() => 'idle'))
  }, [
    prefersReducedMotion,
    soundSet.events.reelSpin,
    stopLoop,
    stopResultCues,
  ])

  useEffect(() => {
    if (
      !isAutoSpinning ||
      isSpinning ||
      isAutoSpinCoolingDown ||
      mascotPhase !== 'idle' ||
      isHelpOpen ||
      isSettingsOpen ||
      isReloadPromptOpen
    ) {
      return undefined
    }

    if (!useFreeGameForNextSpin && balance < selectedWager) {
      setIsAutoSpinning(false)
      setSpinError(null)
      setIsReloadPromptOpen(true)
      return undefined
    }

    const timer = window.setTimeout(() => {
      void handleSpinRef.current()
    }, 0)
    return () => window.clearTimeout(timer)
  }, [
    balance,
    freeSpinsRemaining,
    isAutoSpinning,
    isAutoSpinCoolingDown,
    isHelpOpen,
    isReloadPromptOpen,
    isSettingsOpen,
    isSpinning,
    selectedWager,
    mascotPhase,
    useFreeGameForNextSpin,
  ])

  useEffect(() => {
    if (!isAutoSpinCoolingDown) {
      return undefined
    }

    const timer = window.setTimeout(() => {
      setIsAutoSpinCoolingDown(false)
    }, autoSpinDelayMs)
    return () => window.clearTimeout(timer)
  }, [autoSpinDelayMs, isAutoSpinCoolingDown])

  useEffect(() => {
    onSpinStateChange?.(isSpinning)
    return () => {
      if (isSpinning) {
        onSpinStateChange?.(false)
      }
    }
  }, [isSpinning, onSpinStateChange])

  useEffect(() => () => {
    if (freeSpinBadgeTimerRef.current !== null) {
      window.clearTimeout(freeSpinBadgeTimerRef.current)
    }
  }, [])

  useEffect(() => {
    let isCurrent = true
    slotStateRevisionGuardRef.current.advance()
    const requestedRevision = slotStateRevisionGuardRef.current.capture()
    setFreeSpinsRemaining(0)
    setFreeSpinWagerPoints(null)
    setIsFreeSpinBadgePopping(false)
    setEnergyBalance(0)
    setSealCollections(defaultSealCollections)
    setLastFreeSpinsAwarded(0)
    setLastEnergyAwarded(0)
    setLastEnergyMultiplierApplied(false)
    setEnergyFlyover(null)
    setWinAwardFlyover(null)

    void requestSlotState(gameId)
      .then((state) => {
        if (
          !isCurrent ||
          !slotStateRevisionGuardRef.current.isCurrent(requestedRevision)
        ) {
          return
        }

        setFreeSpinsRemaining(state.freeSpinsRemaining)
        setFreeSpinWagerPoints(state.freeSpinWagerPoints)
        setEnergyBalance(state.energyBalance)
        setSealCollections(state.sealCollections.length > 0 ? state.sealCollections : defaultSealCollections)
        if (state.freeSpinWagerPoints !== null) {
          const matchingWagerIndex = wagerOptions.findIndex(
            (wager) => wager === state.freeSpinWagerPoints,
          )
          if (matchingWagerIndex >= 0) {
            setWagerIndex(matchingWagerIndex)
          }
        }
      })
      .catch(() => undefined)

    return () => {
      isCurrent = false
    }
  }, [gameId, wagerOptions])

  function isCurrentPresentation(signal: AbortSignal): boolean {
    return (
      isMountedRef.current &&
      presentationAbortControllerRef.current?.signal === signal
    )
  }

  async function animateEnergyCollection(
    result: SpinResult,
    isFastAutoSpin: boolean,
    signal: AbortSignal,
  ) {
    if (!isCurrentPresentation(signal)) {
      return
    }

    const settleEnergy = () => {
      if (!isCurrentPresentation(signal)) {
        return
      }
      setEnergyFlyover(null)
      setEnergyBalance(result.energyBalance)
    }

    setLastEnergyAwarded(result.energyAwarded)
    const meterTargetBeforeReset = result.energyMultiplierApplied
      ? energyMeterCapacity
      : result.energyBalance
    if (result.energyAwarded <= 0) {
      if (result.energyMultiplierApplied) {
        setEnergyBalance(energyMeterCapacity)
        setEnergyImpactKey((current) => current + 1)
        const completed = await waitForPresentation(
          isFastAutoSpin ? 120 : 320,
          signal,
        )
        if (!completed) {
          settleEnergy()
          return
        }
      }
      settleEnergy()
      return
    }

    const boltPositions = result.reels
      .flatMap((reel, reelIndex) => reel.flatMap((symbol, rowIndex) =>
        symbol === 'BOLT' ? [{ reel: reelIndex, row: rowIndex }] : [],
      ))
      .sort((left, right) => left.reel - right.reel || left.row - right.row)
    if (boltPositions.length === 0 || prefersReducedMotionRef.current || signal.aborted) {
      settleEnergy()
      return
    }

    const delayCompleted = await waitForPresentation(
      isFastAutoSpin ? 90 : 280,
      signal,
    )
    const frameCompleted = delayCompleted
      ? await waitForPresentationFrame(signal)
      : false
    if (!frameCompleted) {
      settleEnergy()
      return
    }

    let animatedBalance = result.energyMultiplierApplied
      ? Math.min(energyMeterCapacity, Math.max(0, energyBalance))
      : Math.max(0, result.energyBalance - result.energyAwarded)
    setEnergyBalance(animatedBalance)
    const awardPerBolt = Math.floor(result.energyAwarded / boltPositions.length)
    let remainder = result.energyAwarded % boltPositions.length

    for (const [index, position] of boltPositions.entries()) {
      const source = document.querySelector<HTMLElement>(
        `.slot-symbol--bolt[data-reel-index="${position.reel}"][data-row-index="${position.row}"]`,
      )
      const destination = energyMeterRef.current
      const sourceRect = source?.getBoundingClientRect()
      const destinationRect = destination?.getBoundingClientRect()
      const increment = awardPerBolt + (remainder > 0 ? 1 : 0)
      remainder = Math.max(0, remainder - 1)

      if (sourceRect && destinationRect) {
        const durationMs = isFastAutoSpin ? 390 : 680
        setEnergyFlyover({
          id: Date.now() + index,
          left: sourceRect.left,
          top: sourceRect.top,
          width: sourceRect.width,
          height: sourceRect.height,
          travelX:
            destinationRect.left + destinationRect.width / 2 -
            (sourceRect.left + sourceRect.width / 2),
          travelY:
            destinationRect.top + destinationRect.height / 2 -
            (sourceRect.top + sourceRect.height / 2),
          durationMs,
        })
        const completed = await waitForPresentation(durationMs, signal)
        if (!completed) {
          settleEnergy()
          return
        }
      }

      if (!isCurrentPresentation(signal)) {
        return
      }
      animatedBalance = Math.min(meterTargetBeforeReset, animatedBalance + increment)
      setEnergyBalance(animatedBalance)
      setEnergyImpactKey((current) => current + 1)
      setEnergyFlyover(null)
      const completed = await waitForPresentation(
        isFastAutoSpin ? 45 : 100,
        signal,
      )
      if (!completed) {
        settleEnergy()
        return
      }
    }

    if (result.energyMultiplierApplied) {
      setEnergyBalance(energyMeterCapacity)
      setEnergyImpactKey((current) => current + 1)
      const completed = await waitForPresentation(
        isFastAutoSpin ? 120 : 320,
        signal,
      )
      if (!completed) {
        settleEnergy()
        return
      }
    }
    settleEnergy()
  }

  async function animateNumberValue(
    fromValue: number,
    toValue: number,
    durationMs: number,
    signal: AbortSignal,
    onValue: (value: number) => void,
  ): Promise<boolean> {
    if (signal.aborted || durationMs <= 0 || fromValue === toValue) {
      onValue(toValue)
      return !signal.aborted
    }

    return new Promise<boolean>((resolve) => {
      const startTime = performance.now()
      const valueDelta = toValue - fromValue
      let frameId = 0
      let settled = false
      const finish = (completed: boolean) => {
        if (settled) {
          return
        }
        settled = true
        window.cancelAnimationFrame(frameId)
        signal.removeEventListener('abort', handleAbort)
        onValue(toValue)
        resolve(completed)
      }
      const handleAbort = () => finish(false)

      const step = (timestamp: number) => {
        if (signal.aborted) {
          finish(false)
          return
        }
        const progress = Math.min(1, (timestamp - startTime) / durationMs)
        const easedProgress = 1 - Math.pow(1 - progress, 3)
        onValue(Math.round(fromValue + valueDelta * easedProgress))

        if (progress < 1) {
          frameId = window.requestAnimationFrame(step)
        } else {
          finish(true)
        }
      }

      signal.addEventListener('abort', handleAbort, { once: true })
      frameId = window.requestAnimationFrame(step)
    })
  }

  async function animateCreditWinAward(
    result: SpinResult,
    visibleBalanceBeforeAward: number,
    isFastAutoSpin: boolean,
    signal: AbortSignal,
  ) {
    const awardedCredits = Math.max(0, result.payout.totalPoints)
    const finalBalance = result.slotsCreditsBalance ?? visibleBalanceBeforeAward + awardedCredits
    const settleAward = () => {
      if (!isCurrentPresentation(signal)) {
        return
      }
      setBalance(finalBalance)
      setWinAwardFlyover(null)
    }

    if (awardedCredits <= 0) {
      settleAward()
      return
    }

    if (prefersReducedMotionRef.current || signal.aborted) {
      settleAward()
      return
    }

    const speedMultiplier = isFastAutoSpin
      ? Math.min(Math.max(1, autoSpinSpeedMultiplier), autoSpinWinPresentationMaxMultiplier)
      : 1
    const isBigWin = awardedCredits >= Math.max(
      bigWinMinimumPoints,
      result.wagerPoints * bigWinMultiplier,
    )
    const initialHoldDuration = isBigWin
      ? bigWinCountDurationMs / speedMultiplier
      : regularWinHoldDurationMs / speedMultiplier
    const flyoverDuration = winFlyoverDurationMs / speedMultiplier
    const balanceCountDuration = isBigWin
      ? bigWinBalanceCountDurationMs / speedMultiplier
      : regularWinBalanceCountDurationMs / speedMultiplier

    const delayCompleted = await waitForPresentation(
      isFastAutoSpin ? 40 : 110,
      signal,
    )
    const frameCompleted = delayCompleted
      ? await waitForPresentationFrame(signal)
      : false
    if (!frameCompleted || !isCurrentPresentation(signal)) {
      settleAward()
      return
    }

    const frameRect = document.querySelector<HTMLElement>('.slot-game-frame')?.getBoundingClientRect()
    const stageRect = document.querySelector<HTMLElement>('.slots-page__stage')?.getBoundingClientRect()
    const sourceRect = frameRect ?? stageRect
    const destinationRect = creditTileRef.current?.getBoundingClientRect()
    const startLeft = sourceRect ? sourceRect.left + sourceRect.width / 2 : window.innerWidth / 2
    const startTop = sourceRect ? sourceRect.top + sourceRect.height * 0.38 : window.innerHeight * 0.38
    const destinationLeft = destinationRect
      ? destinationRect.left + destinationRect.width / 2
      : startLeft
    const destinationTop = destinationRect
      ? destinationRect.top + destinationRect.height / 2
      : startTop
    const flyoverId = Date.now()

    setWinAwardFlyover({
      id: flyoverId,
      amount: awardedCredits,
      displayAmount: isBigWin ? 0 : awardedCredits,
      isBigWin,
      isFlying: false,
      left: startLeft,
      top: startTop,
      travelX: 0,
      travelY: 0,
      durationMs: initialHoldDuration,
    })

    if (isBigWin) {
      const completed = await animateNumberValue(
        0,
        awardedCredits,
        initialHoldDuration,
        signal,
        (displayAmount) => {
          if (!isCurrentPresentation(signal)) {
            return
          }
          setWinAwardFlyover((currentAward) =>
            currentAward?.id === flyoverId
              ? { ...currentAward, displayAmount }
              : currentAward,
          )
        },
      )
      if (!completed) {
        settleAward()
        return
      }
    } else {
      const completed = await waitForPresentation(initialHoldDuration, signal)
      if (!completed) {
        settleAward()
        return
      }
    }

    if (!isCurrentPresentation(signal)) {
      return
    }
    const flyingAwardId = Date.now() + 1
    setWinAwardFlyover((currentAward) =>
      currentAward
        ? {
            ...currentAward,
            id: flyingAwardId,
            displayAmount: awardedCredits,
            isFlying: true,
            travelX: destinationLeft - startLeft,
            travelY: destinationTop - startTop,
            durationMs: flyoverDuration,
          }
        : currentAward,
    )

    const balanceCountDelay = flyoverDuration * 0.28
    const balanceCount = (async () => {
      const completed = await waitForPresentation(balanceCountDelay, signal)
      if (!completed) {
        return false
      }
      return animateNumberValue(
        visibleBalanceBeforeAward,
        finalBalance,
        balanceCountDuration,
        signal,
        (nextBalance) => {
          if (isCurrentPresentation(signal)) {
            setBalance(nextBalance)
          }
        },
      )
    })()

    const [flyoverCompleted, balanceCompleted] = await Promise.all([
      waitForPresentation(flyoverDuration, signal),
      balanceCount,
    ])
    if (!flyoverCompleted || !balanceCompleted) {
      settleAward()
      return
    }
    settleAward()
  }

  useEffect(() => {
    if (!isHelpOpen) {
      return undefined
    }

    helpCloseButtonRef.current?.focus()
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsHelpOpen(false)
      }
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [isHelpOpen])

  useEffect(() => {
    if (!isReloadPromptOpen) {
      return undefined
    }

    reloadPromptCloseButtonRef.current?.focus()
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsReloadPromptOpen(false)
      }
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [isReloadPromptOpen])

  async function handleSpin() {
    if (spinInProgressRef.current || isSpinning) {
      return
    }

    if (!useFreeGameForNextSpin && balance < selectedWager) {
      setIsAutoSpinning(false)
      setSpinError(null)
      setIsReloadPromptOpen(true)
      return
    }

    const isFastAutoSpin = isAutoSpinning
    const expectedFreeSpin = useFreeGameForNextSpin
    const requestedSpecialBoost = false
    const wagerForSpin = expectedFreeSpin
      ? freeSpinWagerPoints ?? selectedWager
      : selectedWager
    const optimisticCharge = expectedFreeSpin ? 0 : wagerForSpin
    const visibleBalanceBeforeAward = balance - optimisticCharge
    spinInProgressRef.current = true
    stopSpinRequestedRef.current = false
    presentationAbortControllerRef.current?.abort()
    const presentationController = new AbortController()
    const presentationSignal = presentationController.signal
    presentationAbortControllerRef.current = presentationController
    setIsStopRequested(false)
    setIsFastSpinActive(isFastAutoSpin)
    setBalance((currentBalance) => currentBalance - optimisticCharge)
    if (expectedFreeSpin) {
      setFreeSpinsRemaining((current) => Math.max(0, current - 1))
    }
    setLastWin(0)
    setLastFreeSpinsAwarded(0)
    setLastEnergyAwarded(0)
    setLastEnergyMultiplierApplied(false)
    setEnergyFlyover(null)
    setWinAwardFlyover(null)
    if (!expectedFreeSpin) {
      setIsFreeSpinBadgePopping(false)
    }
    beginPerformance(!isFastAutoSpin)
    if (!isFastAutoSpin) {
      playCue(soundSet.events.leverPull)
    }
    startLoop(soundSet.events.reelSpin)
    setIsSpinning(true)
    setSpinStage('requesting')
    setSpinError(null)
    setBestWin(null)
    setBonusPositions([])
    const reelsBeforeSpin = displayedReels.map((reel) => [...reel])
    const displayFrame = (reelIndex: number, symbols: readonly SlotSymbolId[]) => {
      setDisplayedReels((currentReels) =>
        currentReels.map((reel, index) =>
          index === reelIndex ? [...symbols] : reel,
        ),
      )
    }
    const animation = startSpinAnimation({
      reelCount: displayedReels.length,
      rowsPerReel: displayedReels[0]?.length ?? 4,
      displayFrame,
      setReelMotion: (reelIndex, state) => {
        setReelMotion((currentMotion) =>
          currentMotion.map((currentState, index) =>
            index === reelIndex ? state : currentState,
          ),
        )
      },
      reducedMotion: prefersReducedMotionRef.current,
      speedMultiplier: isFastAutoSpin ? autoSpinSpeedMultiplier : 1,
    })
    activeSpinAnimationRef.current = animation
    const stopReelsWithCadence = async (
      targetReels: readonly (readonly SlotSymbolId[])[],
    ) => {
      const settleWithoutMotion = () => {
        if (!isMountedRef.current) {
          return
        }
        setDisplayedReels(targetReels.map((reel) => [...reel]))
        setReelMotion(targetReels.map(() => 'stopped'))
        stopLoop(soundSet.events.reelSpin)
      }
      await stopSpinAnimationWithCadence({
        animation,
        isQuickStopRequested: () => stopSpinRequestedRef.current,
        onReelStopped: (_, stoppedReelCount) => {
          playCue(soundSet.events.reelStop)
          if (stoppedReelCount === targetReels.length) {
            stopLoop(soundSet.events.reelSpin)
          }
        },
        quickBrakeDurationMs: manualStopBrakeDurationMs,
        quickSettleAfterStopMs: manualStopSettleDurationMs,
        settleAfterLastReelMs:
          reelLandingSettleDurationMs /
          (isFastAutoSpin ? autoSpinSpeedMultiplier : 1),
        shouldSnapToTarget: () => prefersReducedMotionRef.current,
        snapToTarget: settleWithoutMotion,
        targetReels,
      })
    }
    let hasStartedStoppingResult = false
    let mascotOutcome: MascotOutcome = 'neutral'
    let shouldStartAutoSpinCooldown = false

    try {
      const result = await requestSpin({
        gameId,
        wagerPoints: wagerForSpin,
        useFreeSpin: expectedFreeSpin,
        useSpecialBoost: requestedSpecialBoost,
      })

      if (result.reels.length !== displayedReels.length) {
        throw new Error(`Expected ${displayedReels.length} reels but received ${result.reels.length}.`)
      }
      if (result.symbolSetId !== expectedServerSymbolSetId) {
        throw new Error(
          `The spin used symbol set '${result.symbolSetId}' instead of '${expectedServerSymbolSetId}'.`,
        )
      }

      slotStateRevisionGuardRef.current.advance()
      setSpinStage('stopping')
      hasStartedStoppingResult = true
      await stopReelsWithCadence(result.reels)
      if (!isMountedRef.current) {
        return
      }
      const bestPayline = findBestPayline(result.payout.paylines)
      const winSoundCue = selectWinSoundEvent(bestPayline, displayedReels.length)
      const triggeredBonusPositions = result.freeSpinsAwarded > 0
        ? result.reels.flatMap((reel, reelIndex) =>
            reel.flatMap((symbol, row) => symbol === 'FREE' ? [{ reel: reelIndex, row }] : []),
          )
        : []
      if (
        (result.payout.totalPoints > 0 ||
          result.freeSpinsAwarded > 0)
      ) {
        mascotOutcome = 'win'
      }
      setBestWin(bestPayline)
      setBonusPositions(triggeredBonusPositions)
      setLastWin(result.payout.totalPoints)
      setLastFreeSpinsAwarded(result.freeSpinsAwarded)
      setLastEnergyMultiplierApplied(result.energyMultiplierApplied)
      setFreeSpinsRemaining(result.freeSpinsRemaining)
      setSealCollections(result.sealCollections.length > 0 ? result.sealCollections : defaultSealCollections)
      setFreeSpinWagerPoints(
        result.freeSpinsRemaining > 0
          ? result.freeSpinWagerPoints ?? freeSpinWagerPoints ?? result.wagerPoints
          : null,
      )
      let resultSoundEvent: SlotResultSoundEvent | null = winSoundCue
      if (result.freeSpinsAwarded > 0) {
        resultSoundEvent = 'bonus'
      } else if (result.payout.totalPoints === 0) {
        mascotOutcome = 'loss'
        resultSoundEvent = 'no-win'
      }
      const revealPainted = resultSoundEvent !== null && !presentationSignal.aborted
        ? await waitForPresentationFrame(presentationSignal)
        : false
      if (resultSoundEvent !== null && revealPainted && !stopSpinRequestedRef.current) {
        playSequence(soundSet.events.results[resultSoundEvent])
      }
      await animateCreditWinAward(
        result,
        visibleBalanceBeforeAward,
        isFastAutoSpin,
        presentationSignal,
      )
      await animateEnergyCollection(result, isFastAutoSpin, presentationSignal)
      shouldStartAutoSpinCooldown = isFastAutoSpin
    } catch (error) {
      if (!isMountedRef.current) {
        return
      }
      setIsAutoSpinning(false)
      const isInsufficientBalance =
        error instanceof SpinRequestError && error.code === 'insufficient-slot-credits'
      const isFreeSpinUnavailable =
        error instanceof SpinRequestError && error.code === 'free-spins-unavailable'
      if (isInsufficientBalance) {
        setIsReloadPromptOpen(true)
      }
      if (isFreeSpinUnavailable) {
        setFreeSpinsRemaining(error.freeSpinsRemaining ?? 0)
        setFreeSpinWagerPoints(null)
        setIsFreeSpinBadgePopping(false)
      }

      if (!hasStartedStoppingResult) {
        setSpinStage('stopping')
        try {
          await stopReelsWithCadence(reelsBeforeSpin)
        } catch {
          setDisplayedReels(reelsBeforeSpin)
        }
      } else {
        setDisplayedReels(reelsBeforeSpin)
      }
      setBestWin(null)
      setBonusPositions([])
      setLastFreeSpinsAwarded(0)
      setLastEnergyAwarded(0)
      setLastEnergyMultiplierApplied(false)
      setEnergyFlyover(null)
      setWinAwardFlyover(null)
      if (expectedFreeSpin && !isFreeSpinUnavailable) {
        setFreeSpinsRemaining((current) => current + 1)
      }
      if (isInsufficientBalance && error.available !== undefined) {
        setBalance(error.available)
      } else {
        setBalance((currentBalance) => currentBalance + optimisticCharge)
      }
      setSpinError(error instanceof Error ? error.message : 'The spin could not be completed.')
    } finally {
      stopLoop(soundSet.events.reelSpin)
      if (isMountedRef.current) {
        finishSpinAnimation(animation)
        completePerformance(mascotOutcome, !isFastAutoSpin)
        setIsAutoSpinCoolingDown(shouldStartAutoSpinCooldown)
        setIsSpinning(false)
        setIsFastSpinActive(false)
        setIsStopRequested(false)
      } else {
        cancelSpinAnimation(animation)
      }
      if (activeSpinAnimationRef.current === animation) {
        activeSpinAnimationRef.current = null
      }
      if (presentationAbortControllerRef.current === presentationController) {
        presentationAbortControllerRef.current = null
      }
      stopSpinRequestedRef.current = false
      spinInProgressRef.current = false
    }
  }

  handleSpinRef.current = handleSpin

  const winningPositions = [
    ...(bestWin?.matches.flatMap((match) => match.match.positions) ?? []),
    ...bonusPositions,
  ]
  const canAffordSelectedWager = useFreeGameForNextSpin
    ? freeSpinsRemaining > 0
    : balance >= selectedWager
  const activeWagerDisplay = useFreeGameForNextSpin
    ? freeSpinWagerPoints ?? selectedWager
    : selectedWager
  const showFreeSpinBadge = isFreeSpinBadgePopping || (!isSpinning && freeSpinsRemaining > 0)
  const visibleSealCollections = defaultSealCollections.map((fallback) => {
    const current = sealCollections.find((collection) => collection.sealId === fallback.sealId)
    return current ?? fallback
  })
  const pageBackdropImage = cabinetTheme.pageBackdropImage ?? cabinetTheme.visualsBackdropImage
  const pageBackdropStyle = pageBackdropImage
    ? ({
        '--slot-page-backdrop': `url("${pageBackdropImage}")`,
      } as CSSProperties)
    : undefined
  const slotsPageClassName = [
    'slots-page',
    isFastSpinActive ? 'slots-page--fast-spin' : '',
    pageBackdropImage ? 'slots-page--theme-backdrop' : '',
  ].filter(Boolean).join(' ')

  function changeWager(direction: -1 | 1) {
    if (freeSpinsRemaining > 0) {
      setSpinError('Free spins are locked to the wager that won them.')
      return
    }

    setIsAutoSpinning(false)
    setWagerIndex((currentIndex) =>
      Math.min(wagerOptions.length - 1, Math.max(0, currentIndex + direction)),
    )
    setSpinError(null)
  }

  function handleSpinButtonClick() {
    if (spinInProgressRef.current || isSpinning) {
      stopSpinRequestedRef.current = true
      if (activeSpinAnimationRef.current !== null) {
        requestQuickStop(activeSpinAnimationRef.current)
      }
      presentationAbortControllerRef.current?.abort()
      stopResultCues()
      setIsStopRequested(true)
      setSpinStage('stopping')
      setIsAutoSpinning(false)
      setIsFastSpinActive(true)
      return
    }

    if (freeSpinsRemaining > 0) {
      if (freeSpinBadgeTimerRef.current !== null) {
        window.clearTimeout(freeSpinBadgeTimerRef.current)
      }
      setIsFreeSpinBadgePopping(true)
      freeSpinBadgeTimerRef.current = window.setTimeout(() => {
        setIsFreeSpinBadgePopping(false)
        freeSpinBadgeTimerRef.current = null
      }, 420)
    }

    void handleSpin()
  }

  function reelStripStyle(reelIndex: number) {
    const symbolRowCount = Math.max(1, displayedReels[reelIndex]?.length ?? 4)
    const visibleRowCount = symbolRowCount > 8 ? symbolRowCount - 8 : symbolRowCount
    const stripHeight = symbolRowCount / Math.max(1, visibleRowCount) * 100
    const travel = -Math.max(0, symbolRowCount - visibleRowCount) / symbolRowCount * 100
    return {
      '--slot-symbol-rows': symbolRowCount,
      '--slot-strip-height': `${stripHeight}%`,
      '--slot-spin-travel': `${travel}%`,
    } as CSSProperties
  }

  return (
    <div className={slotsPageClassName} style={pageBackdropStyle} data-slot-theme={cabinetTheme.id}>
      <header className="slots-page__topbar">
        <a
          className="slots-page__brand"
          href="/"
          aria-label="Return to the Fortune Forge landing page"
        >
          <span className="slots-page__brand-name">Fortune Forge</span>
        </a>
        <span className="slots-page__brand-actions">
            <a
              className="slots-page__purchase-credits"
              href="/home/credits"
              aria-label="Add balance"
              onClick={() => setIsAutoSpinning(false)}
            >
              <ForgeCoin className="slots-page__purchase-credits-coin" />
              <span>Add balance</span>
            </a>
            <PaymentAlertsMenu />
            <button
              className="slots-page__help-button"
              type="button"
              aria-label="How to win"
              aria-haspopup="dialog"
              aria-expanded={isHelpOpen}
              onClick={() => {
                setIsAutoSpinning(false)
                setIsHelpOpen(true)
              }}
            >
              ?
            </button>
            <button
              className="slots-page__settings-button"
              type="button"
              aria-label="Open settings"
              aria-haspopup="dialog"
              aria-expanded={isSettingsOpen}
              onClick={() => {
                setIsAutoSpinning(false)
                setIsSettingsOpen(true)
              }}
            >
              <span aria-hidden="true">&#9881;</span>
            </button>
        </span>

      </header>

      <main className="slots-page__main">
        <div className="slots-page__layout">
          <SymbolValueGuide symbolSet={symbolSet} />

          <div className="slots-page__stage">
          <div className="slots-page__meter-stack">
            <div className="slots-page__seal-collections" aria-label="Power seal collections">
              {visibleSealCollections.map((collection) => {
                const seal = sealLabels[collection.sealId] ?? sealLabels.sync
                const progress = Math.min(100, collection.count / collection.requiredCount * 100)
                return (
                  <div
                    className={`slots-page__seal-collection slots-page__seal-collection--${collection.sealId}`}
                    key={collection.sealId}
                    role="progressbar"
                    aria-label={`${seal.label}: ${collection.count} of ${collection.requiredCount} seals`}
                    aria-valuemin={0}
                    aria-valuemax={collection.requiredCount}
                    aria-valuenow={collection.count}
                  >
                    <img src={symbolSet.definitions[seal.symbol].image} alt="" aria-hidden="true" />
                    <span className="slots-page__seal-copy">
                      <span className="slots-page__seal-row">
                        <strong>{seal.shortLabel}</strong>
                        <em>{collection.count}/{collection.requiredCount}</em>
                      </span>
                      <span className="slots-page__seal-meter" aria-hidden="true">
                        <span style={{ width: `${progress}%` }} />
                      </span>
                      <small>
                        {collection.averageWagerPoints > 0
                          ? `avg ${formatRand(collection.averageWagerPoints)}`
                          : 'collect any'}
                      </small>
                    </span>
                  </div>
                )
              })}
            </div>

            <div
              key={`energy-meter-${energyImpactKey}`}
              ref={energyMeterRef}
              className={`slots-page__energy-meter${energyImpactKey > 0 ? ' slots-page__energy-meter--impact' : ''}`}
              role="progressbar"
              aria-label={`Energy: ${creditFormatter.format(energyBalance)}`}
              aria-valuemin={0}
              aria-valuemax={energyMeterCapacity}
              aria-valuenow={Math.min(energyMeterCapacity, energyBalance)}
            >
              <img src={symbolSet.definitions.BOLT.image} alt="" aria-hidden="true" />
              <span className="slots-page__energy-copy">
                <span className="slots-page__energy-label">Energy</span>
                <span className="slots-page__energy-track" aria-hidden="true">
                  <span
                    className="slots-page__energy-fill"
                    style={{ width: `${Math.min(100, energyBalance / energyMeterCapacity * 100)}%` }}
                  />
                </span>
                <strong>{creditFormatter.format(energyBalance)}/{energyMeterCapacity}</strong>
              </span>
            </div>
          </div>

          <SlotMachine
            cabinetTheme={cabinetTheme}
            reelCount={displayedReels.length}
            renderReel={(reelIndex) => (
              <div
                className={`slot-reel__symbols slot-reel__symbols--${reelMotion[reelIndex]}`}
                style={reelStripStyle(reelIndex)}
              >
                {displayedReels[reelIndex].map((symbol, rowIndex) => (
                  <SlotSymbol
                    key={`row-${rowIndex}`}
                    symbol={symbol}
                    symbolSet={symbolSet}
                    reelIndex={reelIndex}
                     rowIndex={rowIndex}
                    animated={shouldUseAnimatedSymbol(
                      prefersReducedMotion,
                      reelMotion[reelIndex],
                    )}
                    highlighted={winningPositions.some(
                      (position) => position.reel === reelIndex && position.row === rowIndex,
                    )}
                    highlightOrder={winningPositions.findIndex(
                      (position) => position.reel === reelIndex && position.row === rowIndex,
                    )}
                  />
                ))}
              </div>
            )}
          />

          <div className="slots-page__playbar" aria-label="Balance, wager, and spin controls">
            <div
              ref={creditTileRef}
              className="slots-page__balance slots-page__control-tile"
              aria-label={`Balance: ${formatRand(balance)}`}
            >
              <span className="slots-page__balance-label">Balance</span>
              <span className="slots-page__balance-line">
                <span className="slots-page__balance-value">{formatRand(balance)}</span>
              </span>
            </div>

            <div className="slots-page__spin-controls" aria-label="Spin, autospin, and wager controls">
              <button
                className="slots-page__wager-nudge"
                type="button"
                aria-label="Decrease wager"
                disabled={isSpinning || isAutoSpinning || freeSpinsRemaining > 0 || wagerIndex === 0}
                onClick={() => changeWager(-1)}
              >
                <svg viewBox="0 0 100 100" aria-hidden="true">
                  <path d="M28 50H72" />
                </svg>
              </button>

              <div className="slots-page__spin-stack">
                <div className="slots-page__spin-button-shell">
                  <SpinButton
                    isSpinning={isSpinning}
                    isStopRequested={isStopRequested}
                    onSpin={handleSpinButtonClick}
                  />
                  {showFreeSpinBadge && (
                    <span
                      className={`slots-page__free-spin-badge${isFreeSpinBadgePopping ? ' slots-page__free-spin-badge--popping' : ''}`}
                      aria-hidden="true"
                    >
                      <strong>Free spin!</strong>
                      {freeSpinsRemaining > 1 && <span>×{freeSpinsRemaining}</span>}
                    </span>
                  )}
                </div>
                <button
                  className={`slots-page__auto-spin${isAutoSpinning ? ' slots-page__auto-spin--active' : ''}`}
                  type="button"
                  aria-pressed={isAutoSpinning}
                  onClick={() => {
                    setSpinError(null)
                    setIsAutoSpinning((current) => !current)
                  }}
                  aria-label={isAutoSpinning ? 'Stop autospin' : 'Start autospin'}
                >
                  <strong>Autospin</strong>
                </button>
                <button
                  className={`slots-page__spin-wager${!useFreeGameForNextSpin ? ' slots-page__spin-wager--selected' : ''}`}
                  type="button"
                  aria-pressed={!useFreeGameForNextSpin}
                  aria-label={`${useFreeGameForNextSpin ? 'Locked free spin wager' : 'Wager'}: ${formatRand(activeWagerDisplay)}`}
                  disabled={isSpinning || isAutoSpinning || useFreeGameForNextSpin}
                  onClick={() => {
                    setSpinError(null)
                  }}
                >
                  <span className="slots-page__wager-label">
                    {useFreeGameForNextSpin ? 'Free wager' : 'Wager'}
                  </span>
                  <span className="slots-page__wager-value">{formatRand(activeWagerDisplay)}</span>
                </button>
              </div>

              <button
                className="slots-page__wager-nudge"
                type="button"
                aria-label="Increase wager"
                disabled={
                  isSpinning ||
                  isAutoSpinning ||
                  freeSpinsRemaining > 0 ||
                  wagerIndex === wagerOptions.length - 1
                }
                onClick={() => changeWager(1)}
              >
                <svg viewBox="0 0 100 100" aria-hidden="true">
                  <path d="M28 50H72" />
                  <path d="M50 28V72" />
                </svg>
              </button>
            </div>
          </div>
        </div>
        </div>
      </main>

      {energyFlyover && (
        <img
          key={energyFlyover.id}
          className="slots-page__energy-flyover"
          src={symbolSet.definitions.BOLT.image}
          alt=""
          aria-hidden="true"
          style={{
            left: energyFlyover.left,
            top: energyFlyover.top,
            width: energyFlyover.width,
            height: energyFlyover.height,
            animationDuration: `${energyFlyover.durationMs}ms`,
            '--energy-travel-x': `${energyFlyover.travelX}px`,
            '--energy-travel-y': `${energyFlyover.travelY}px`,
          } as CSSProperties}
        />
      )}

      {winAwardFlyover && (
        <div
          key={winAwardFlyover.id}
          className={[
            'slots-page__win-award',
            winAwardFlyover.isBigWin ? 'slots-page__win-award--big' : '',
            winAwardFlyover.isFlying ? 'slots-page__win-award--flying' : '',
          ].filter(Boolean).join(' ')}
          aria-hidden="true"
          style={{
            left: winAwardFlyover.left,
            top: winAwardFlyover.top,
            animationDuration: `${winAwardFlyover.durationMs}ms`,
            '--win-travel-x': `${winAwardFlyover.travelX}px`,
            '--win-travel-y': `${winAwardFlyover.travelY}px`,
          } as CSSProperties}
        >
          {winAwardFlyover.isBigWin && <span>Big win</span>}
          <strong>+{formatRand(winAwardFlyover.displayAmount)}</strong>
        </div>
      )}

      <footer
        className={`slots-page__footer${spinError ? ' slots-page__footer--error' : ''}`}
        aria-live="polite"
      >
        {spinError
          ?? (isSpinning
            ? spinStage === 'requesting'
              ? 'The jewel reels are spinning'
              : ''
            : lastFreeSpinsAwarded > 0
              ? `${lastFreeSpinsAwarded} free games won — ${freeSpinsRemaining} ready`
              : lastEnergyMultiplierApplied
                ? 'Energy boost ×1.5 — meter reset'
              : lastEnergyAwarded > 0
                ? ''
              : useFreeGameForNextSpin
                ? ''
            : !canAffordSelectedWager
              ? 'Choose a smaller wager'
              : lastWin > 0
                ? `Win ${formatRand(lastWin)}`
                : '')}
      </footer>

      <AudioSettingsDialog
        isOpen={isSettingsOpen}
        preferences={audioPreferences}
        onClose={closeSettings}
        onToggleMuted={toggleMuted}
        onToggleResultsOnly={toggleResultsOnly}
        onVolumeChange={setVolume}
      />

      {isHelpOpen && (
        <div
          className="win-help__backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              setIsHelpOpen(false)
            }
          }}
        >
          <section
            className="win-help"
            role="dialog"
            aria-modal="true"
            aria-labelledby="win-help-title"
          >
            <button
              ref={helpCloseButtonRef}
              className="win-help__close"
              type="button"
              aria-label="Close win guide"
              onClick={() => setIsHelpOpen(false)}
            >
              ×
            </button>

            <p className="win-help__eyebrow">Fortune guide</p>
            <h2 id="win-help-title">What counts as a win?</h2>
            <p className="win-help__intro">
              Matching symbols must connect across neighboring reels. {symbolSet.definitions.ACE.label}
              is the highest-value symbol and can substitute on a full five-symbol payline.
            </p>

            <div className="win-help__rules">
              <article className="win-help__rule">
                <span className="win-help__rule-number">3</span>
                <div>
                  <h3>Three symbols</h3>
                  <p>
                    The first win begins on reel 1 and crosses reels 1–3 in a straight row or one
                    clean diagonal direction.
                  </p>
                  <div className="win-help__pictograms" aria-label="Allowed three-symbol paths">
                    {THREE_MATCH_PATTERNS.map((pattern) => (
                      <svg
                        key={pattern.label}
                        className="win-help__pictogram"
                        viewBox="0 0 60 70"
                        role="img"
                        aria-label={pattern.label}
                      >
                        <title>{pattern.label}</title>
                        <rect x="1" y="1" width="58" height="68" rx="9" />
                        {Array.from({ length: 12 }, (_, index) => {
                          const column = index % 3
                          const row = Math.floor(index / 3)
                          return (
                            <circle
                              key={`${column}-${row}`}
                              className="win-help__pictogram-dot"
                              cx={10 + column * 20}
                              cy={8 + row * 18}
                              r="2.4"
                            />
                          )
                        })}
                        <polyline
                          points={pattern.rows
                            .map((row, column) => `${10 + column * 20},${8 + row * 18}`)
                            .join(' ')}
                        />
                        {pattern.rows.map((row, column) => (
                          <circle
                            key={`${column}-${row}-win`}
                            className="win-help__pictogram-win"
                            cx={10 + column * 20}
                            cy={8 + row * 18}
                            r="5"
                          />
                        ))}
                      </svg>
                    ))}
                  </div>
                  <p className="win-help__note">Three-symbol wins begin on reel 1.</p>
                </div>
              </article>

              <article className="win-help__rule">
                <span className="win-help__rule-number">5</span>
                <div>
                  <h3>Five symbols</h3>
                  <p>
                    A matching treasure across all five reels wins on any of the game’s 23 full
                    payline patterns. Wukong medallions may substitute here, and the more central
                    patterns pay more. Four-symbol runs do not pay.
                  </p>
                  <div
                    className="win-help__five-pictograms"
                    aria-label="All valid five-symbol paylines"
                  >
                    {FIVE_MATCH_PATTERNS.map((rows, patternIndex) => (
                      <svg
                        key={rows.join('-')}
                        className="win-help__pictogram win-help__pictogram--five"
                        viewBox="0 0 100 70"
                        role="img"
                        aria-label={`Valid five-symbol payline ${patternIndex + 1}`}
                      >
                        <title>{`Valid five-symbol payline ${patternIndex + 1}`}</title>
                        <rect x="1" y="1" width="98" height="68" rx="9" />
                        {Array.from({ length: 20 }, (_, index) => {
                          const column = index % 5
                          const row = Math.floor(index / 5)
                          return (
                            <circle
                              key={`${column}-${row}`}
                              className="win-help__pictogram-dot"
                              cx={10 + column * 20}
                              cy={8 + row * 18}
                              r="2.4"
                            />
                          )
                        })}
                        <polyline
                          points={rows
                            .map((row, column) => `${10 + column * 20},${8 + row * 18}`)
                            .join(' ')}
                        />
                        {rows.map((row, column) => (
                          <circle
                            key={`${column}-${row}-win`}
                            className="win-help__pictogram-win"
                            cx={10 + column * 20}
                            cy={8 + row * 18}
                            r="5"
                          />
                        ))}
                      </svg>
                    ))}
                  </div>
                  <p className="win-help__note">
                    Every winning route connects one matching symbol on each of the five reels.
                  </p>
                </div>
              </article>

              <article className="win-help__rule">
                <span className="win-help__rule-number">FREE</span>
                <div>
                  <h3>Free games</h3>
                  <p>
                    Land three or more FREE GAME symbols anywhere in the window to receive five
                    free games. Free games use the wager that triggered them.
                  </p>
                </div>
              </article>

              <article className="win-help__rule">
                <span className="win-help__rule-number">PAW</span>
                <div>
                  <h3>Monkey paw money grab</h3>
                  <p>
                    A monkey paw anywhere on screen grabs every Rand multiplier coin showing in
                    the window. Two paws are much rarer and double the grabbed amount. Three
                    bananas in a row, column, or diagonal pay 3× the wager.
                  </p>
                </div>
              </article>

              <article className="win-help__rule">
                <span className="win-help__rule-number">44</span>
                <div>
                  <h3>Power seal collections</h3>
                  <p>
                    Sync, Rows, Paw, and Rand seals collect from anywhere visible. A completed
                    44-seal collection awards ten free spins tied to that collection’s average
                    wager. Energy at 25%, 50%, and 75% improves seal odds; a full energy meter
                    boosts the payout by 1.5×, resets, and finishes the nearest seal track.
                  </p>
                </div>
              </article>
            </div>

            <p className="win-help__fine-print">
              Repeated copies of the same short path pay once. When wilds create several possible
              symbol matches, the highest-paying valid match is used.
            </p>
          </section>
        </div>
      )}

      {isReloadPromptOpen && (
        <div
          className="fortune-prompt__backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              setIsReloadPromptOpen(false)
            }
          }}
        >
          <section
            className="fortune-prompt reload-prompt"
            role="dialog"
            aria-modal="true"
            aria-labelledby="reload-prompt-title"
          >
            <button
              ref={reloadPromptCloseButtonRef}
              className="fortune-prompt__close"
              type="button"
              aria-label="Close insufficient fortune message"
              onClick={() => setIsReloadPromptOpen(false)}
            >
              ×
            </button>
            <div className="reload-prompt__icon" aria-hidden="true">!</div>
            <p className="fortune-prompt__eyebrow">More fortune needed</p>
            <h2 id="reload-prompt-title">Not enough fortune</h2>
            <p className="reload-prompt__copy">
              This spin costs {formatRand(selectedWager)}, but your current balance is {formatRand(balance)}.
              Choose a smaller wager to continue.
            </p>
            <div className="reload-prompt__actions">
              <button className="reload-prompt__primary" type="button" onClick={() => setIsReloadPromptOpen(false)}>
                Choose another wager
              </button>
              <a href="/home/credits">Add balance</a>
            </div>
          </section>
        </div>
      )}

      {mascotSet !== null && (
        <MascotCompanion
          variant="game"
          mascotSet={mascotSet}
          phase={mascotPhase}
          actionKey={mascotActionKey}
          successFrame={mascotSuccessFrame}
        />
      )}
    </div>
  )
}
