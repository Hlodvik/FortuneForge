import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
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
import type { SlotExperienceSet } from './config/slotExperienceSets'
import type { SlotResultSoundEvent } from './config/soundSets'
import { useMascotPerformance, type MascotOutcome } from './hooks/useMascotPerformance'
import { usePrefersReducedMotion } from './hooks/usePrefersReducedMotion'
import { useSlotAudio } from './hooks/useSlotAudio'
import {
  SlotStateRevisionGuard,
  waitForPresentation,
  waitForPresentationFrame,
} from './presentation/spinLifecycle'
import { findBestPayline, selectWinSoundEvent } from './presentation/spinPresentation'
import { slotPointsToRand } from './slotPagePresentation'
import {
  requestDemoAvailability,
  requestDemoSpin,
  requestSlotState,
  requestSpin,
  SpinRequestError,
} from './services/slotsApi'
import type { GridPosition, PaylinePayout, SlotSealCollection, SlotSymbolId, SpinResult } from './types/slots'
import type { AccountSummary } from '../account/services/accountsApi'

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

type SealFlyover = EnergyFlyover & {
  collectionId: string
  symbol: SlotSymbolId
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

type MoneyGrabTokenFlyover = {
  id: number
  symbol: SlotSymbolId
  reel: number
  row: number
  left: number
  top: number
  width: number
  height: number
  travelX: number
  travelY: number
  delayMs: number
  durationMs: number
}

type MoneyGrabPresentation = {
  id: number
  amount: number
  pawLeft: number
  pawTop: number
  pawSize: number
  pawTravelX: number
  pawTravelY: number
  pawDurationMs: number
  popupLeft: number
  popupTop: number
  popupDelayMs: number
  popupDurationMs: number
  tokens: MoneyGrabTokenFlyover[]
}

const manualStopBrakeDurationMs = 90
const manualStopSettleDurationMs = 35
const regularWinHoldDurationMs = 380
const regularWinBalanceCountDurationMs = 420
const bigWinMinimumRand = 500
const bigWinMultiplier = 50
const bigWinCountDurationMs = 1250
const bigWinBalanceCountDurationMs = 720
const winFlyoverDurationMs = 540
const autoSpinWinPresentationMaxMultiplier = 1.35

export type SlotsPageProps = {
  account?: AccountSummary
  demoMode?: boolean
  experienceSet: SlotExperienceSet
  onSpinStateChange?: (isSpinning: boolean) => void
}

const demoStartingBalance = 10_000
type DemoAvailability = 'checking' | 'available' | 'unavailable'

export function useSlotsPageController({
  account,
  demoMode = false,
  experienceSet,
  onSpinStateChange,
}: SlotsPageProps) {
  // This hook coordinates gameplay. Presentation assets and tunable rules enter
  // through this single composed set rather than direct file imports.
  const {
    cabinet: cabinetTheme,
    features: featureSet,
    help,
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
    pointValueInCents,
    wagerOptions,
  } = rules
  const defaultSealCollections = useMemo<SlotSealCollection[]>(
    () => featureSet.collections?.entries.map((collection) => ({
      sealId: collection.id,
      count: 0,
      averageWagerPoints: 0,
      requiredCount: collection.requiredCount,
    })) ?? [],
    [featureSet.collections],
  )
  const animationSymbolIds = useMemo(
    () => Object.values(symbolSet.definitions).flatMap((definition) =>
      definition ? [definition.id] : []),
    [symbolSet],
  )
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
  const [demoAvailability, setDemoAvailability] = useState<DemoAvailability>(
    demoMode ? 'checking' : 'available',
  )
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isAutoSpinCoolingDown, setIsAutoSpinCoolingDown] = useState(false)
  const [isFastSpinActive, setIsFastSpinActive] = useState(false)
  const [spinStage, setSpinStage] = useState<'requesting' | 'stopping'>('requesting')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [bestWin, setBestWin] = useState<PaylinePayout | null>(null)
  const [bonusPositions, setBonusPositions] = useState<GridPosition[]>([])
  const [balance, setBalance] = useState(
    demoMode ? demoStartingBalance : account?.balances.slotsCredits ?? 0,
  )
  const [lastWin, setLastWin] = useState(0)
  const [lastFreeSpinsAwarded, setLastFreeSpinsAwarded] = useState(0)
  const [lastEnergyAwarded, setLastEnergyAwarded] = useState(0)
  const [lastEnergyMultiplierApplied, setLastEnergyMultiplierApplied] = useState(false)
  const [freeSpinsRemaining, setFreeSpinsRemaining] = useState(0)
  const [freeSpinWagerPoints, setFreeSpinWagerPoints] = useState<number | null>(null)
  const [freeSpinFeatureMode, setFreeSpinFeatureMode] = useState<string | null>(null)
  const [isFreeSpinBadgePopping, setIsFreeSpinBadgePopping] = useState(false)
  const [energyBalance, setEnergyBalance] = useState(0)
  const [sealCollections, setSealCollections] =
    useState<SlotSealCollection[]>(defaultSealCollections)
  const [energyFlyover, setEnergyFlyover] = useState<EnergyFlyover | null>(null)
  const [energyImpactKey, setEnergyImpactKey] = useState(0)
  const [sealFlyover, setSealFlyover] = useState<SealFlyover | null>(null)
  const [sealImpactId, setSealImpactId] = useState<string | null>(null)
  const [winAwardFlyover, setWinAwardFlyover] = useState<WinAwardFlyover | null>(null)
  const [moneyGrabPresentation, setMoneyGrabPresentation] =
    useState<MoneyGrabPresentation | null>(null)
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
  const isDemoSpinDisabled = demoMode && demoAvailability !== 'available'
  const demoAvailabilityMessage = !demoMode || demoAvailability === 'available'
    ? null
    : demoAvailability === 'checking'
      ? 'Checking demo service availability…'
      : 'Demo service unavailable — Spin is disabled. Reload after the API is restored.'
  const selectedWagerPoints = wagerOptions[wagerIndex] ?? wagerOptions[0] ?? 0
  const selectedWagerRand = slotPointsToRand(selectedWagerPoints, pointValueInCents)
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
    if (!demoMode) {
      setDemoAvailability('available')
      return undefined
    }

    const controller = new AbortController()
    setDemoAvailability('checking')
    void requestDemoAvailability(gameId, controller.signal)
      .then(() => {
        if (!controller.signal.aborted) {
          setDemoAvailability('available')
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setDemoAvailability('unavailable')
          setIsAutoSpinning(false)
        }
      })

    return () => controller.abort()
  }, [demoMode, gameId])

  useEffect(() => {
    if (
      !isAutoSpinning ||
      isDemoSpinDisabled ||
      isSpinning ||
      isAutoSpinCoolingDown ||
      mascotPhase !== 'idle' ||
      isHelpOpen ||
      isSettingsOpen ||
      isReloadPromptOpen
    ) {
      return undefined
    }

    if (!useFreeGameForNextSpin && balance < selectedWagerRand) {
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
    isDemoSpinDisabled,
    isReloadPromptOpen,
    isSettingsOpen,
    isSpinning,
    selectedWagerRand,
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
    setFreeSpinFeatureMode(null)
    setIsFreeSpinBadgePopping(false)
    setEnergyBalance(0)
    setSealCollections(defaultSealCollections)
    setLastFreeSpinsAwarded(0)
    setLastEnergyAwarded(0)
    setLastEnergyMultiplierApplied(false)
    setEnergyFlyover(null)
    setWinAwardFlyover(null)
    setMoneyGrabPresentation(null)

    if (demoMode) {
      setBalance(demoStartingBalance)
      return () => {
        isCurrent = false
      }
    }

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
        setFreeSpinFeatureMode(state.freeSpinFeatureMode)
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
  }, [defaultSealCollections, demoMode, gameId, wagerOptions])

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

    if (!featureSet.energy) {
      setEnergyFlyover(null)
      setEnergyBalance(result.energyBalance)
      return
    }
    const energyFeature = featureSet.energy

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
        symbol === energyFeature.symbol ? [{ reel: reelIndex, row: rowIndex }] : [],
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
        `.slot-symbol[data-symbol="${energyFeature.symbol}"][data-reel-index="${position.reel}"][data-row-index="${position.row}"]`,
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

  async function animateSealCollection(
    result: SpinResult,
    isFastAutoSpin: boolean,
    signal: AbortSignal,
  ) {
    const settleSeals = () => {
      if (!isCurrentPresentation(signal)) {
        return
      }
      setSealFlyover(null)
      setSealImpactId(null)
      setSealCollections(
        result.sealCollections.length > 0
          ? result.sealCollections
          : defaultSealCollections,
      )
    }

    const collectionFeature = featureSet.collections
    if (!collectionFeature) {
      settleSeals()
      return
    }

    const collectionsBySymbol = new Map(
      collectionFeature.entries.map((collection) => [collection.symbol, collection]),
    )
    const sealPositions = result.reels.flatMap((reel, reelIndex) =>
      reel.flatMap((symbol, rowIndex) => {
        const collection = collectionsBySymbol.get(symbol)
        return collection
          ? [{ collection, reel: reelIndex, row: rowIndex, symbol }]
          : []
      }),
    )

    if (
      sealPositions.length === 0 ||
      prefersReducedMotionRef.current ||
      signal.aborted
    ) {
      settleSeals()
      return
    }

    const delayCompleted = await waitForPresentation(
      isFastAutoSpin ? 55 : 150,
      signal,
    )
    const frameCompleted = delayCompleted
      ? await waitForPresentationFrame(signal)
      : false
    if (!frameCompleted) {
      settleSeals()
      return
    }

    const animatedCounts = new Map(
      visibleSealCollections.map((collection) => [collection.sealId, collection.count]),
    )
    const finalCollections = new Map(
      result.sealCollections.map((collection) => [collection.sealId, collection]),
    )

    for (const [index, position] of sealPositions.entries()) {
      const source = document.querySelector<HTMLElement>(
        `.slot-symbol[data-symbol="${position.symbol}"][data-reel-index="${position.reel}"][data-row-index="${position.row}"]`,
      )
      const destination = document.querySelector<HTMLElement>(
        `.slots-page__seal-collection[data-seal-id="${position.collection.id}"]`,
      )
      const sourceRect = source?.getBoundingClientRect()
      const destinationRect = destination?.getBoundingClientRect()

      if (sourceRect && destinationRect) {
        const durationMs = isFastAutoSpin ? 330 : 560
        setSealFlyover({
          id: Date.now() + index,
          collectionId: position.collection.id,
          symbol: position.symbol,
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
          settleSeals()
          return
        }
      }

      const currentCount = animatedCounts.get(position.collection.id) ?? 0
      const finalCollection = finalCollections.get(position.collection.id)
      const nextCount = finalCollection && finalCollection.count >= currentCount
        ? Math.min(finalCollection.count, currentCount + 1)
        : Math.min(position.collection.requiredCount, currentCount + 1)
      animatedCounts.set(position.collection.id, nextCount)
      setSealCollections((currentCollections) =>
        currentCollections.map((collection) =>
          collection.sealId === position.collection.id
            ? { ...collection, count: nextCount }
            : collection,
        ),
      )
      setSealFlyover(null)
      setSealImpactId(position.collection.id)
      const impactCompleted = await waitForPresentation(
        isFastAutoSpin ? 45 : 105,
        signal,
      )
      setSealImpactId(null)
      if (!impactCompleted) {
        settleSeals()
        return
      }
    }

    settleSeals()
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
    const awardedCredits = Math.max(
      0,
      slotPointsToRand(result.payout.totalPoints, result.pointValueInCents),
    )
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
      bigWinMinimumRand,
      slotPointsToRand(result.wagerPoints, result.pointValueInCents) * bigWinMultiplier,
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

  async function animateMoneyGrab(
    result: SpinResult,
    isFastAutoSpin: boolean,
    signal: AbortSignal,
  ) {
    const settleGrab = () => {
      if (isCurrentPresentation(signal)) {
        setMoneyGrabPresentation(null)
      }
    }

    const moneyGrabFeature = featureSet.moneyGrab
    if (
      !moneyGrabFeature ||
      result.moneyGrabPoints <= 0 ||
      result.monkeyPawCount <= 0 ||
      prefersReducedMotionRef.current ||
      signal.aborted
    ) {
      settleGrab()
      return
    }

    const pawPosition = result.reels.flatMap((reel, reelIndex) =>
      reel.flatMap((symbol, row) =>
        symbol === moneyGrabFeature.collectorSymbol ? [{ reel: reelIndex, row }] : [],
      ),
    )[0]
    const moneyPositions = result.reels.flatMap((reel, reelIndex) =>
      reel.flatMap((symbol, row) => symbol.startsWith(moneyGrabFeature.valueSymbolPrefix)
        ? [{ reel: reelIndex, row, symbol }]
        : []),
    )

    if (!pawPosition || moneyPositions.length === 0) {
      settleGrab()
      return
    }

    const frameCompleted = await waitForPresentationFrame(signal)
    if (!frameCompleted || !isCurrentPresentation(signal)) {
      settleGrab()
      return
    }

    const findSymbolRect = (reel: number, row: number) =>
      document.querySelector<HTMLElement>(
        `.slot-symbol[data-reel-index="${reel}"][data-row-index="${row}"]`,
      )?.getBoundingClientRect()
    const pawRect = findSymbolRect(pawPosition.reel, pawPosition.row)
    const moneyRects = moneyPositions.flatMap((position) => {
      const rect = findSymbolRect(position.reel, position.row)
      return rect && rect.width > 0 && rect.height > 0
        ? [{ ...position, rect }]
        : []
    })
    if (!pawRect || pawRect.width <= 0 || pawRect.height <= 0 || moneyRects.length === 0) {
      settleGrab()
      return
    }

    const targetX = moneyRects.reduce(
      (sum, { rect }) => sum + rect.left + rect.width / 2,
      0,
    ) / moneyRects.length
    const targetY = moneyRects.reduce(
      (sum, { rect }) => sum + rect.top + rect.height / 2,
      0,
    ) / moneyRects.length
    const frameRect = document.querySelector<HTMLElement>('.slot-game-frame')?.getBoundingClientRect()
    const pawCenterX = pawRect.left + pawRect.width / 2
    const pawCenterY = pawRect.top + pawRect.height / 2
    const pawSize = Math.min(148, Math.max(76, Math.max(pawRect.width, pawRect.height) * 1.42))
    const pawDurationMs = isFastAutoSpin ? 410 : 650
    const tokenDurationMs = isFastAutoSpin ? 320 : 500
    const tokenDelayStepMs = isFastAutoSpin ? 22 : 42
    const popupDelayMs = isFastAutoSpin ? 245 : 420
    const popupDurationMs = isFastAutoSpin ? 330 : 560
    const presentationId = Date.now()
    const tokens = moneyRects.map(({ rect, reel, row, symbol }, index) => ({
      id: presentationId + index + 1,
      symbol,
      reel,
      row,
      left: rect.left,
      top: rect.top,
      width: rect.width,
      height: rect.height,
      travelX: targetX - (rect.left + rect.width / 2),
      travelY: targetY - (rect.top + rect.height / 2),
      delayMs: index * tokenDelayStepMs,
      durationMs: tokenDurationMs,
    }))
    const lastTokenEndMs = tokenDurationMs + (tokens.length - 1) * tokenDelayStepMs
    const totalDurationMs = Math.max(
      pawDurationMs,
      lastTokenEndMs,
      popupDelayMs + popupDurationMs,
    )

    setMoneyGrabPresentation({
      id: presentationId,
      amount: slotPointsToRand(result.moneyGrabPoints, result.pointValueInCents),
      pawLeft: pawCenterX - pawSize / 2,
      pawTop: pawCenterY - pawSize / 2,
      pawSize,
      pawTravelX: targetX - pawCenterX,
      pawTravelY: targetY - pawCenterY,
      pawDurationMs,
      popupLeft: frameRect ? frameRect.left + frameRect.width / 2 : targetX,
      popupTop: frameRect ? frameRect.top + frameRect.height * 0.4 : targetY,
      popupDelayMs,
      popupDurationMs,
      tokens,
    })

    await waitForPresentation(totalDurationMs, signal)
    settleGrab()
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

    if (isDemoSpinDisabled) {
      setIsAutoSpinning(false)
      return
    }

    if (!useFreeGameForNextSpin && balance < selectedWagerRand) {
      setIsAutoSpinning(false)
      setSpinError(null)
      setIsReloadPromptOpen(true)
      return
    }

    const isFastAutoSpin = isAutoSpinning
    const expectedFreeSpin = useFreeGameForNextSpin
    const requestedSpecialBoost = false
    const wagerForSpin = expectedFreeSpin
      ? freeSpinWagerPoints ?? selectedWagerPoints
      : selectedWagerPoints
    const optimisticCharge = expectedFreeSpin
      ? 0
      : slotPointsToRand(wagerForSpin, pointValueInCents)
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
    setMoneyGrabPresentation(null)
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
      symbolIds: animationSymbolIds,
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
      const result = demoMode
        ? await requestDemoSpin({
            gameId,
            wagerPoints: wagerForSpin,
            useFreeSpin: expectedFreeSpin,
            freeSpinsRemaining,
            freeSpinWagerPoints,
            energyBalance,
            sealCollections,
            freeSpinFeatureMode,
          })
        : await requestSpin({
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
      if (result.pointValueInCents !== pointValueInCents) {
        throw new Error(
          `The spin used a ${result.pointValueInCents}-cent point instead of ${pointValueInCents} cents.`,
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
      setLastWin(slotPointsToRand(result.payout.totalPoints, result.pointValueInCents))
      setLastFreeSpinsAwarded(result.freeSpinsAwarded)
      setLastEnergyMultiplierApplied(result.energyMultiplierApplied)
      setFreeSpinsRemaining(result.freeSpinsRemaining)
      setFreeSpinWagerPoints(
        result.freeSpinsRemaining > 0
          ? result.freeSpinWagerPoints ?? freeSpinWagerPoints ?? result.wagerPoints
          : null,
      )
      setFreeSpinFeatureMode(result.freeSpinFeatureMode)
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
      await animateMoneyGrab(result, isFastAutoSpin, presentationSignal)
      await animateCreditWinAward(
        result,
        visibleBalanceBeforeAward,
        isFastAutoSpin,
        presentationSignal,
      )
      await animateSealCollection(result, isFastAutoSpin, presentationSignal)
      await animateEnergyCollection(result, isFastAutoSpin, presentationSignal)
      shouldStartAutoSpinCooldown = isFastAutoSpin
    } catch (error) {
      if (!isMountedRef.current) {
        return
      }
      setIsAutoSpinning(false)
      if (demoMode) {
        setDemoAvailability('unavailable')
      }
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
        setFreeSpinFeatureMode(null)
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
      setSealFlyover(null)
      setSealImpactId(null)
      setWinAwardFlyover(null)
      setMoneyGrabPresentation(null)
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
    : balance >= selectedWagerRand
  const activeWagerDisplay = useFreeGameForNextSpin
    ? slotPointsToRand(freeSpinWagerPoints ?? selectedWagerPoints, pointValueInCents)
    : selectedWagerRand
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

    if (isDemoSpinDisabled) {
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

  return {
    activeWagerDisplay,
    audioPreferences,
    balance,
    cabinetTheme,
    canAffordSelectedWager,
    changeWager,
    closeSettings,
    creditTileRef,
    displayedReels,
    demoAvailability,
    demoAvailabilityMessage,
    demoMode,
    demoStartingBalance,
    energyBalance,
    energyFlyover,
    energyImpactKey,
    energyMeterCapacity,
    energyMeterRef,
    featureSet,
    freeSpinsRemaining,
    handleSpinButtonClick,
    helpCloseButtonRef,
    isAutoSpinning,
    isFreeSpinBadgePopping,
    isHelpOpen,
    isDemoSpinDisabled,
    isReloadPromptOpen,
    isSettingsOpen,
    isSpinning,
    isStopRequested,
    lastEnergyAwarded,
    lastEnergyMultiplierApplied,
    lastFreeSpinsAwarded,
    lastWin,
    mascotActionKey,
    mascotPhase,
    mascotSet,
    mascotSuccessFrame,
    help,
    moneyGrabPresentation,
    pageBackdropStyle,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    reloadPromptCloseButtonRef,
    selectedWager: selectedWagerRand,
    sealFlyover,
    sealImpactId,
    setIsAutoSpinning,
    setIsHelpOpen,
    setIsReloadPromptOpen,
    setIsSettingsOpen,
    setSpinError,
    setVolume,
    showFreeSpinBadge,
    slotsPageClassName,
    spinError,
    spinStage,
    symbolSet,
    toggleMuted,
    toggleResultsOnly,
    useFreeGameForNextSpin,
    visibleSealCollections,
    wagerIndex,
    wagerOptions,
    winAwardFlyover,
    winningPositions,
  }
}

export type SlotsPageController = ReturnType<typeof useSlotsPageController>
