import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { getSlotAnimationSymbolIds } from './config/symbolSets'
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
import type { SlotEnergyAward } from './config/slotFeatures'
import type { SlotResultSoundEvent } from './config/soundSets'
import { useMascotPerformance, type MascotOutcome } from './hooks/useMascotPerformance'
import { usePrefersReducedMotion } from './hooks/usePrefersReducedMotion'
import { useSlotAudio } from './hooks/useSlotAudio'
import { getCollectionArrivalCue, getSuiteResultCue } from './audio/slotSoundEvents'
import {
  SlotStateRevisionGuard,
  waitForPresentation,
  waitForPresentationFrame,
} from './presentation/spinLifecycle'
import {
  describeSpinOutcome,
  findBestPayline,
  getSlotWinTier,
  getWinPresentationTier,
  selectOutcomeSoundEvent,
  type SlotSpinOutcome,
  type SlotWinTier,
  type WinPresentationTier,
} from './presentation/spinPresentation'
import { getPresentedFreeSpinsAwarded } from './presentation/freeSpinAwards'
import { slotPointsToRand } from './slotPagePresentation'
import { getSlotKeyboardAction } from './slotKeyboard'
import {
  requestDemoAvailability,
  requestDemoSpin,
  requestSlotState,
  requestSpin,
  SpinRequestError,
  type SlotState,
} from './services/slotsApi'
import type { GridPosition, LightningCollection, MidnightVaultBonusRound, MidnightVaultDemoState, PaylinePayout, SlotSealCollection, SlotSymbolId, SpinResult } from './types/slots'
import type { AccountSummary } from '../account/services/accountsApi'
import {
  GODS_TUMBLE_COLUMNS,
  GODS_TUMBLE_GAME_ID,
  GODS_TUMBLE_INITIAL_REELS,
  type GodsTumbleAward,
  type GodsTumbleStep,
} from '../../games/slots/godsOfOlympus/tumble'

type GodsTumbleFrame = Pick<GodsTumbleStep, 'cleared' | 'dropRows' | 'amountPoints'> & {
  totalPoints: number
  step: number
} & ({ phase: 'award'; award: GodsTumbleAward } | { phase: 'clear' | 'drop' })

const tumbleDemoFeatures: SlotExperienceSet['features'] = {}
const godsTumbleAwardMs = 650
const godsTumbleClearMs = 500
const godsTumbleDropMs = 520

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
  chestDropHeight: number
}

type WinAwardFlyover = {
  id: number
  amount: number
  displayAmount: number
  winTier: SlotWinTier
  isFlying: boolean
  tier: WinPresentationTier
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

type CollectionAwardPresentation = {
  collectionId: string
  freeSpins: number
  featureMode: string | null
}

const manualStopBrakeDurationMs = 90
const manualStopSettleDurationMs = 35
const regularWinHoldDurationMs = 380
const regularWinBalanceCountDurationMs = 420
const bigWinCountDurationMs = 1250
const bigWinBalanceCountDurationMs = 720
const winFlyoverDurationMs = 540
const lightningCollectionDurationMs = 500
const lightningCollectionPayoutId = 902
const autoSpinWinPresentationMaxMultiplier = 1.35
const extraRowsFeatureMode = 'rows'
const specialSpinDelayMs = 2_400

export type SlotsPageProps = {
  account?: AccountSummary
  demoMode?: boolean
  experienceSet: SlotExperienceSet
  onSpinStateChange?: (isSpinning: boolean) => void
}

const demoStartingBalance = 10_000
type SpinAvailability = 'checking' | 'available' | 'unavailable'

export function useSlotsPageController({
  account,
  demoMode = false,
  experienceSet,
  onSpinStateChange,
}: SlotsPageProps) {
  const balanceActionHref = demoMode ? null : '/balance'
  const balanceRecoveryHref = demoMode ? window.location.pathname : '/balance'
  const balanceRecoveryLabel = demoMode ? 'Reset balance' : 'Add Rand'
  // This hook coordinates gameplay. Presentation assets and tunable rules enter
  // through this single composed set rather than direct file imports.
  const {
    cabinet: cabinetTheme,
    help,
    mascot: mascotSet,
    outcomeNarrative,
    rules,
    sounds: soundSet,
    symbols: symbolSet,
  } = experienceSet
  const {
    autoSpinDelayMs,
    autoSpinSpeedMultiplier,
    baseSpinSpeedMultiplier,
    energyMeterCapacity,
    gameId,
    pointValueInCents,
    reelTravelRowCount,
    wagerOptions,
  } = rules
  const usesGodsTumble = gameId === GODS_TUMBLE_GAME_ID
  const isPaidGodsTumble = usesGodsTumble && !demoMode
  const featureSet = usesGodsTumble && demoMode ? tumbleDemoFeatures : experienceSet.features
  const initialReels = usesGodsTumble ? GODS_TUMBLE_INITIAL_REELS : rules.initialReels
  const energyCompletionAward = featureSet.energy?.completionAward
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
    () => getSlotAnimationSymbolIds(symbolSet),
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
  const [godsTumbleFrame, setGodsTumbleFrame] = useState<GodsTumbleFrame | null>(null)
  const [spinAvailability, setSpinAvailability] = useState<SpinAvailability>(
    demoMode || isPaidGodsTumble ? 'checking' : 'available',
  )
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isAutoSpinCoolingDown, setIsAutoSpinCoolingDown] = useState(false)
  const [isFastSpinActive, setIsFastSpinActive] = useState(false)
  const [spinStage, setSpinStage] = useState<'requesting' | 'stopping'>('requesting')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [bestWin, setBestWin] = useState<PaylinePayout | null>(null)
  const [lastWin, setLastWin] = useState(0)
  const [lastCollectorAward, setLastCollectorAward] = useState(0)
  const [lightningCollection, setLightningCollection] = useState<LightningCollection | null>(null)
  const [lastFreeSpinsAwarded, setLastFreeSpinsAwarded] = useState(0)
  const [winningPaylineCount, setWinningPaylineCount] = useState(0)
  const [winningPaylines, setWinningPaylines] = useState<PaylinePayout[]>([])
  const [bonusPositions, setBonusPositions] = useState<GridPosition[]>([])
  const [balance, setBalance] = useState(
    demoMode ? demoStartingBalance : account?.balances.slotsCredits ?? 0,
  )
  const [lastSpinOutcome, setLastSpinOutcome] = useState<SlotSpinOutcome | null>(null)
  const [resultAtmosphereId, setResultAtmosphereId] = useState(0)
  const [lastEnergyAwarded, setLastEnergyAwarded] = useState(0)
  const [lastEnergyMultiplierApplied, setLastEnergyMultiplierApplied] = useState(false)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [lastSpinOutcomeKey, setLastSpinOutcomeKey] = useState(0)
  const [freeSpinsRemaining, setFreeSpinsRemaining] = useState(0)
  const [freeSpinWagerPoints, setFreeSpinWagerPoints] = useState<number | null>(null)
  const [freeSpinFeatureMode, setFreeSpinFeatureMode] = useState<string | null>(null)
  const [midnightBonusState, setMidnightBonusState] = useState<MidnightVaultDemoState | null>(null)
  const [midnightBonusRound, setMidnightBonusRound] = useState<MidnightVaultBonusRound | null>(null)
  const [midnightQueuedRounds, setMidnightQueuedRounds] = useState(0)
  const [hasSpecialRoundStarted, setHasSpecialRoundStarted] = useState(false)
  const [isSpecialSpinInProgress, setIsSpecialSpinInProgress] = useState(false)
  const [isSpecialSpinDelayActive, setIsSpecialSpinDelayActive] = useState(false)
  const [heldCompletedCollectionId, setHeldCompletedCollectionId] = useState<string | null>(null)
  const [specialRoundWinnings, setSpecialRoundWinnings] = useState(0)
  const [rainbowBonusSummary, setRainbowBonusSummary] = useState<{ totalRand: number | null } | null>(null)
  const [specialRoundGemCount, setSpecialRoundGemCount] = useState(0)
  const [isFreeSpinBadgePopping, setIsFreeSpinBadgePopping] = useState(false)
  const [energyBalance, setEnergyBalance] = useState(0)
  const [isEnergyAwardCompleting, setIsEnergyAwardCompleting] = useState(false)
  const [energyAward, setEnergyAward] = useState<SlotEnergyAward | null>(null)
  const [sealCollections, setSealCollections] =
    useState<SlotSealCollection[]>(defaultSealCollections)
  const [energyFlyover, setEnergyFlyover] = useState<EnergyFlyover | null>(null)
  const [energyImpactKey, setEnergyImpactKey] = useState(0)
  const [sealFlyover, setSealFlyover] = useState<SealFlyover | null>(null)
  const [sealImpactId, setSealImpactId] = useState<string | null>(null)
  const [winAwardFlyover, setWinAwardFlyover] = useState<WinAwardFlyover | null>(null)
  const [moneyGrabPresentation, setMoneyGrabPresentation] =
    useState<MoneyGrabPresentation | null>(null)
  const [collectionAwardPresentation, setCollectionAwardPresentation] =
    useState<CollectionAwardPresentation | null>(null)
  const [wagerIndex, setWagerIndex] = useState(0)
  const [isSymbolGuideOpen, setIsSymbolGuideOpen] = useState(false)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const playedSuiteResultRef = useRef<string | null>(null)
  const playedSuiteCollectionsRef = useRef<{ spinId: string; tracks: Set<string> } | null>(null)
  const spinInProgressRef = useRef(false)
  const hasShownGodsTumbleShowcaseRef = useRef(false)
  const stopSpinRequestedRef = useRef(false)
  const prefersReducedMotionRef = useRef(prefersReducedMotion)
  const presentationAbortControllerRef = useRef<AbortController | null>(null)
  const activeSpinAnimationRef = useRef<SpinAnimation | null>(null)
  const slotStateRevisionGuardRef = useRef(new SlotStateRevisionGuard())
  const isMountedRef = useRef(true)
  const handleSpinRef = useRef<() => Promise<void>>(async () => undefined)
  const rainbowBonusWinningsCentsRef = useRef(0)
  const rainbowBonusStartedHereRef = useRef(false)
  const rainbowBonusSummaryPendingRef = useRef(false)
  const helpCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const reloadPromptCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const energyMeterRef = useRef<HTMLDivElement | null>(null)
  const creditTileRef = useRef<HTMLDivElement | null>(null)
  const freeSpinBadgeTimerRef = useRef<number | null>(null)
  const specialSpinDelayTimerRef = useRef<number | null>(null)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const isGodsTumbleBoard = usesGodsTumble && displayedReels.length === GODS_TUMBLE_COLUMNS
  const isSpinServiceDisabled = spinAvailability !== 'available'
  const spinAvailabilityMessage = !isSpinServiceDisabled
    ? null
    : spinAvailability === 'checking'
      ? 'Checking game service availability…'
      : 'Game service unavailable — Spin is disabled. Reload after the service is restored.'
  const selectedWagerPoints = wagerOptions[wagerIndex] ?? wagerOptions[0] ?? 0
  const selectedWagerRand = slotPointsToRand(selectedWagerPoints, pointValueInCents)
  const expectedServerSymbolSetId = symbolSet.serverSymbolSetId ?? symbolSet.id
  const useFreeGameForNextSpin = freeSpinsRemaining > 0
  const isSpecialGameActive = featureSet.specialRound !== undefined &&
    hasSpecialRoundStarted &&
    (useFreeGameForNextSpin || isSpecialSpinInProgress)
  const closeSettings = useCallback(() => setIsSettingsOpen(false), [])
  const dismissCollectionAwardPresentation = useCallback(
    () => setCollectionAwardPresentation(null),
    [],
  )
  const dismissRainbowBonusSummary = useCallback(() => {
    rainbowBonusSummaryPendingRef.current = false
    setRainbowBonusSummary(null)
    setLastSpinOutcome(null)
    setHasCompletedSpin(false)
    setWinningPaylines([])
    setFreeSpinFeatureMode(null)
  }, [])

  useEffect(() => {
    isMountedRef.current = true
    return () => {
      isMountedRef.current = false
      presentationAbortControllerRef.current?.abort()
      if (activeSpinAnimationRef.current !== null) {
        cancelSpinAnimation(activeSpinAnimationRef.current)
        activeSpinAnimationRef.current = null
      }
      if (specialSpinDelayTimerRef.current !== null) {
        window.clearTimeout(specialSpinDelayTimerRef.current)
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
    if (soundSet.events.specialReelSpin !== undefined) {
      stopLoop(soundSet.events.specialReelSpin)
    }
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
    soundSet.events.specialReelSpin,
    stopLoop,
    stopResultCues,
  ])

  useEffect(() => {
    const autoSpinAmbienceCue = soundSet.events.autoSpinAmbience
    if (!isAutoSpinning && autoSpinAmbienceCue !== undefined) {
      stopLoop(autoSpinAmbienceCue)
    }
  }, [isAutoSpinning, soundSet.events.autoSpinAmbience, stopLoop])

  useEffect(() => {
    if (!demoMode) {
      if (!isPaidGodsTumble) setSpinAvailability('available')
      return undefined
    }

    const controller = new AbortController()
    setSpinAvailability('checking')
    void requestDemoAvailability(gameId, controller.signal)
      .then(() => {
        if (!controller.signal.aborted) {
          setSpinAvailability('available')
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setSpinAvailability('unavailable')
          setIsAutoSpinning(false)
        }
      })

    return () => controller.abort()
  }, [demoMode, gameId, isPaidGodsTumble])

  useEffect(() => {
    if (
      !isAutoSpinning ||
      isSpinServiceDisabled ||
      isSpinning ||
      isAutoSpinCoolingDown ||
      mascotPhase !== 'idle' ||
      isHelpOpen ||
      isSymbolGuideOpen ||
      isSettingsOpen ||
      isReloadPromptOpen ||
      collectionAwardPresentation !== null ||
      rainbowBonusSummary !== null ||
      isSpecialGameActive
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
    collectionAwardPresentation,
    rainbowBonusSummary,
    isHelpOpen,
    isSymbolGuideOpen,
    isSpinServiceDisabled,
    isReloadPromptOpen,
    isSettingsOpen,
    isSpinning,
    selectedWagerRand,
    mascotPhase,
    useFreeGameForNextSpin,
    isSpecialGameActive,
  ])

  useEffect(() => {
    const canQueueSpecialSpin =
      isSpecialGameActive &&
      freeSpinsRemaining > 0 &&
      !isSpinning &&
      mascotPhase === 'idle' &&
      !isHelpOpen &&
      !isSymbolGuideOpen &&
      !isSettingsOpen &&
      !isReloadPromptOpen &&
      collectionAwardPresentation === null

    if (!canQueueSpecialSpin) {
      if (specialSpinDelayTimerRef.current !== null) {
        window.clearTimeout(specialSpinDelayTimerRef.current)
        specialSpinDelayTimerRef.current = null
      }
      setIsSpecialSpinDelayActive((current) => current ? false : current)
      return undefined
    }

    if (specialSpinDelayTimerRef.current !== null) {
      return undefined
    }

    setIsSpecialSpinDelayActive(true)
    const timer = window.setTimeout(() => {
      specialSpinDelayTimerRef.current = null
      setIsSpecialSpinDelayActive(false)
      void handleSpinRef.current()
    }, specialSpinDelayMs)
    specialSpinDelayTimerRef.current = timer

    return () => {
      if (specialSpinDelayTimerRef.current === timer) {
        window.clearTimeout(timer)
        specialSpinDelayTimerRef.current = null
      }
    }
  }, [
    collectionAwardPresentation,
    freeSpinsRemaining,
    isHelpOpen,
    isSymbolGuideOpen,
    isReloadPromptOpen,
    isSettingsOpen,
    isSpecialGameActive,
    isSpinning,
    mascotPhase,
  ])

  useEffect(() => {
    const buttonClickCue = soundSet.events.buttonClick
    if ((!soundSet.events.themeSuite && gameId !== 'dragon-hoard-v1') || buttonClickCue === undefined) return

    const onButtonClick = (event: MouseEvent) => {
      if (!event.isTrusted || !(event.target instanceof Element)) return
      const control = event.target.closest('button, summary, [role="button"]')
      if (control === null || control.matches('.spin-button, :disabled, [aria-disabled="true"]') ||
        control.closest('[inert]') !== null) return
      if (soundSet.events.themeSuite && control.closest('.slots-page__wager-control, .pirates-spin-controls__bet-stepper') !== null) return
      // The main spin control handles its own cues. Capture also reaches
      // dialog portals and the payment bell while this cabinet is mounted.
      playCue(buttonClickCue)
    }

    document.addEventListener('click', onButtonClick, true)
    return () => document.removeEventListener('click', onButtonClick, true)
  }, [gameId, playCue, soundSet.events.buttonClick, soundSet.events.themeSuite])

  useEffect(() => {
    // Ask for ambience as soon as the cabinet opens. Browsers that permit
    // autoplay start immediately; restrictive browsers retry on any trusted
    // interaction. Listeners remain available because a click made while the
    // player has muted effects must not consume the only unlock attempt.
    const specialAmbienceCue = soundSet.events.specialAmbience
    const playSpecialAmbience = specialAmbienceCue !== undefined &&
      (useFreeGameForNextSpin || isSpecialSpinInProgress ||
        rainbowBonusSummary !== null)
    const activeAmbienceCue = playSpecialAmbience && specialAmbienceCue !== undefined
      ? specialAmbienceCue
      : soundSet.events.ambience
    if (playSpecialAmbience) {
      stopLoop(soundSet.events.ambience)
      if (soundSet.events.autoSpinAmbience !== undefined) {
        stopLoop(soundSet.events.autoSpinAmbience)
      }
    } else if (specialAmbienceCue !== undefined) {
      stopLoop(specialAmbienceCue)
    }
    startLoop(activeAmbienceCue)
    const unlockAudio = (event: Event) => {
      if (!event.isTrusted) return
      startLoop(activeAmbienceCue)
    }
    const retryVisibleAudio = () => {
      if (document.visibilityState === 'visible') startLoop(activeAmbienceCue)
    }
    window.addEventListener('pointerdown', unlockAudio)
    window.addEventListener('keydown', unlockAudio)
    window.addEventListener('focus', retryVisibleAudio)
    window.addEventListener('pageshow', retryVisibleAudio)
    document.addEventListener('visibilitychange', retryVisibleAudio)
    return () => {
      window.removeEventListener('pointerdown', unlockAudio)
      window.removeEventListener('keydown', unlockAudio)
      window.removeEventListener('focus', retryVisibleAudio)
      window.removeEventListener('pageshow', retryVisibleAudio)
      document.removeEventListener('visibilitychange', retryVisibleAudio)
    }
  }, [
    audioPreferences.mode,
    gameId,
    isSpecialSpinInProgress,
    rainbowBonusSummary,
    soundSet.events.ambience,
    soundSet.events.autoSpinAmbience,
    soundSet.events.specialAmbience,
    startLoop,
    stopLoop,
    useFreeGameForNextSpin,
  ])

  useEffect(() => {
    if (isSpecialGameActive) {
      return
    }

    if (!soundSet.events.themeSuite && hasSpecialRoundStarted) {
      startLoop(soundSet.events.ambience)
    }
    setHeldCompletedCollectionId(null)
    setSpecialRoundGemCount(0)
    setSpecialRoundWinnings(0)
    if (!useFreeGameForNextSpin) {
      setHasSpecialRoundStarted(false)
    }
  }, [
    gameId,
    hasSpecialRoundStarted,
    isSpecialGameActive,
    soundSet.events.ambience,
    soundSet.events.themeSuite,
    startLoop,
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

  const applySlotState = useCallback((state: SlotState) => {
    const award = energyCompletionAward?.decode(state)
    if (award === null) return

    setFreeSpinsRemaining(state.freeSpinsRemaining)
    setFreeSpinWagerPoints(state.freeSpinWagerPoints)
    setFreeSpinFeatureMode(state.freeSpinFeatureMode)
    setMidnightBonusRound(state.bonusRound ?? null)
    setMidnightQueuedRounds(state.queuedBonusRounds?.length ?? 0)
    setEnergyBalance(state.energyBalance)
    if (award) setEnergyAward(award)
    setSealCollections(state.sealCollections.length > 0 ? state.sealCollections : defaultSealCollections)
    if (state.freeSpinWagerPoints !== null) {
      const matchingWagerIndex = wagerOptions.findIndex(
        (wager) => wager === state.freeSpinWagerPoints,
      )
      if (matchingWagerIndex >= 0) {
        setWagerIndex(matchingWagerIndex)
      }
    }
  }, [defaultSealCollections, energyCompletionAward, gameId, wagerOptions])

  useEffect(() => {
    let isCurrent = true
    slotStateRevisionGuardRef.current.advance()
    const requestedRevision = slotStateRevisionGuardRef.current.capture()
    setFreeSpinsRemaining(0)
    setMidnightBonusState(null)
    setMidnightBonusRound(null)
    setMidnightQueuedRounds(0)
    setFreeSpinWagerPoints(null)
    setFreeSpinFeatureMode(null)
    setHasSpecialRoundStarted(false)
    setIsFreeSpinBadgePopping(false)
    setEnergyBalance(0)
    setIsEnergyAwardCompleting(false)
    setEnergyAward(energyCompletionAward?.decode({}) ?? null)
    setSealCollections(defaultSealCollections)
    setLastEnergyAwarded(0)
    setLastEnergyMultiplierApplied(false)
    setLastWin(0)
    setLastCollectorAward(0)
    setLightningCollection(null)
    setLastFreeSpinsAwarded(0)
    setLastSpinOutcome(null)
    setWinningPaylineCount(0)
    setWinningPaylines([])
    setHasCompletedSpin(false)
    setEnergyFlyover(null)
    setWinAwardFlyover(null)
    setMoneyGrabPresentation(null)
    setCollectionAwardPresentation(null)

    rainbowBonusWinningsCentsRef.current = 0
    rainbowBonusStartedHereRef.current = false
    rainbowBonusSummaryPendingRef.current = false
    setRainbowBonusSummary(null)

    if (demoMode) {
      setBalance(demoStartingBalance)
      return () => {
        isCurrent = false
      }
    }

    if (isPaidGodsTumble) setSpinAvailability('checking')
    void requestSlotState(gameId)
      .then((state) => {
        if (
          !isCurrent ||
          !slotStateRevisionGuardRef.current.isCurrent(requestedRevision)
        ) {
          return
        }

        applySlotState(state)
        if (isPaidGodsTumble) setSpinAvailability('available')
      })
      .catch(() => {
        if (isCurrent && isPaidGodsTumble) setSpinAvailability('unavailable')
      })

    return () => {
      isCurrent = false
    }
  }, [applySlotState, defaultSealCollections, demoMode, energyCompletionAward, gameId, isPaidGodsTumble])

  function isCurrentPresentation(signal: AbortSignal): boolean {
    return (
      isMountedRef.current &&
      presentationAbortControllerRef.current?.signal === signal
    )
  }

  async function animateEnergyCollection(
    result: SpinResult,
    award: SlotEnergyAward | undefined,
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
    const completed = award?.completed ?? result.energyMultiplierApplied

    const settleEnergy = () => {
      if (!isCurrentPresentation(signal)) {
        return
      }
      setEnergyFlyover(null)
      setIsEnergyAwardCompleting(false)
      setEnergyBalance(result.energyBalance)
      if (award) setEnergyAward(award)
    }

    const showEnergyCompletion = async () => {
      setEnergyBalance(energyMeterCapacity)
      setEnergyImpactKey((current) => current + 1)
      if (award) {
        setIsEnergyAwardCompleting(true)
        setEnergyAward(award)
      }
      await waitForPresentation(
        energyCompletionAward?.durationMs ?? (isFastAutoSpin ? 120 : 320),
        signal,
      )
    }

    setLastEnergyAwarded(result.energyAwarded)
    if (award && prefersReducedMotionRef.current) {
      settleEnergy()
      return
    }

    const meterTargetBeforeReset = completed
      ? energyMeterCapacity
      : result.energyBalance
    if (result.energyAwarded <= 0) {
      if (completed) {
        await showEnergyCompletion()
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
      if (award && completed && isCurrentPresentation(signal)) {
        await showEnergyCompletion()
      }
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

    let animatedBalance = completed
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

    if (completed) {
      await showEnergyCompletion()
    }
    settleEnergy()
  }

  async function animateSealCollection(
    result: SpinResult,
    isFastAutoSpin: boolean,
    signal: AbortSignal,
    isFeatureCast: boolean,
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

    const midnightAward = gameId === 'dragon-hoard-v1'
      ? result.awardedBonusRounds?.find(round => round.id === result.bonusRound?.id && round.id.includes(':collection:'))
      : undefined
    const collectionFreeSpinsAwarded = gameId === 'dragon-hoard-v1'
      ? midnightAward?.totalSpins ?? 0
      : collectionFeature.presentation === 'gem-hoard'
      ? result.collectionFreeSpinsAwarded ?? 0
      : getPresentedFreeSpinsAwarded(result, freeSpinsRemaining, result.isFreeSpin)
    const awardedModes = new Set(result.freeSpinFeatureMode?.split('-') ?? [])
    const awardedCollectionId = gameId === 'dragon-hoard-v1'
      ? midnightAward?.id.split(':')[2] ?? null
      : collectionFeature.presentation === 'gem-hoard' &&
      collectionFreeSpinsAwarded > 0
      ? collectionFeature.entries.find((entry) =>
        awardedModes.has(entry.id) && (result.sealsAwarded[entry.symbol] ?? 0) > 0,
      )?.id ?? null
      : null
    const presentCollectionAward = (collectionId: string | null) => {
      if (!collectionId || !collectionFeature.completionDialog || !isCurrentPresentation(signal) ||
        gameId === 'dragon-hoard-v1' && result.isFreeSpin) {
        return
      }
      setIsAutoSpinning(false)
      stopLoop(soundSet.events.ambience)
      if (soundSet.events.autoSpinAmbience !== undefined) {
        stopLoop(soundSet.events.autoSpinAmbience)
      }
      setHasSpecialRoundStarted(true)
      setHeldCompletedCollectionId(collectionId)
      setSpecialRoundGemCount(0)
      setSpecialRoundWinnings(0)
      setCollectionAwardPresentation({
        collectionId,
        freeSpins: collectionFreeSpinsAwarded,
        featureMode: result.freeSpinFeatureMode,
      })
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
    const heardTracks = soundSet.events.themeSuite && playedSuiteCollectionsRef.current?.spinId === result.spinId
      ? playedSuiteCollectionsRef.current.tracks : new Set<string>()
    if (soundSet.events.themeSuite) playedSuiteCollectionsRef.current = { spinId: result.spinId, tracks: heardTracks }
    const playCollectionArrival = (track: string) => {
      if (signal.aborted || heardTracks.has(track)) return false
      const cue = getCollectionArrivalCue(soundSet, track)
      if (cue === undefined) return false
      heardTracks.add(track)
      playCue(cue)
      return true
    }

    if (
      sealPositions.length === 0 ||
      prefersReducedMotionRef.current ||
      signal.aborted
    ) {
      if (!signal.aborted && prefersReducedMotionRef.current)
        sealPositions.forEach(position => playCollectionArrival(position.collection.id))
      settleSeals()
      if (!signal.aborted) presentCollectionAward(awardedCollectionId)
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
      if (!signal.aborted) presentCollectionAward(awardedCollectionId)
      return
    }

    const animatedCounts = new Map(
      visibleSealCollections.map((collection) => [collection.sealId, collection.count]),
    )
    const finalCollections = new Map(
      result.sealCollections.map((collection) => [collection.sealId, collection]),
    )
    // A gem feature can trigger before the last gem on a reel is collected, so
    // the final count may still exceed the starting count after its reset.
    let completedCollectionId: string | null = awardedCollectionId

    for (const [index, position] of sealPositions.entries()) {
      const source = document.querySelector<HTMLElement>(
        `.slot-symbol[data-symbol="${position.symbol}"][data-reel-index="${position.reel}"][data-row-index="${position.row}"]`,
      )
      const destinationSelector = collectionFeature.presentation === 'gem-hoard'
        ? `.slots-page__seal-collection[data-seal-id="${position.collection.id}"] .slots-page__pirate-gem-pile`
        : `.slots-page__seal-collection[data-seal-id="${position.collection.id}"]`
      const destination = document.querySelector<HTMLElement>(destinationSelector)
      const sourceRect = source?.getBoundingClientRect()
      const destinationRect = destination?.getBoundingClientRect()

      if (sourceRect && destinationRect) {
        const isGemHoard = collectionFeature.presentation === 'gem-hoard'
        const durationMs = isGemHoard
          ? (isFastAutoSpin ? 420 : 720)
          : (isFastAutoSpin ? 330 : 560)
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
          chestDropHeight: isGemHoard
            ? Math.max(18, destinationRect.height * 0.5)
            : 0,
        })
        const completed = await waitForPresentation(durationMs, signal)
        if (!completed) {
          settleSeals()
          return
        }
      }

      const currentCount = animatedCounts.get(position.collection.id) ?? 0
      const finalCollection = finalCollections.get(position.collection.id)
      if (collectionFreeSpinsAwarded > 0 && finalCollection && finalCollection.count < currentCount) {
        completedCollectionId ??= position.collection.id
      }
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
      const suiteArrival = playCollectionArrival(position.collection.id)
      if (!soundSet.events.themeSuite && !suiteArrival && gameId === 'reel-riches-v1' && isFeatureCast && index === 0 && soundSet.events.specialCollect) {
        playCue(soundSet.events.specialCollect)
      }
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
    presentCollectionAward(completedCollectionId)
  }

  async function animateNumberValue(
    fromValue: number,
    toValue: number,
    durationMs: number,
    signal: AbortSignal,
    onValue: (value: number) => void,
    precision = 1,
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
        onValue(Math.round((fromValue + valueDelta * easedProgress) * precision) / precision)

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
    pendingEnergyAward: number,
  ) {
    const awardedCredits = Math.max(
      0,
      slotPointsToRand(result.payout.totalPoints, result.pointValueInCents),
    )
    const finalBalance = result.slotsCreditsBalance === null
      ? visibleBalanceBeforeAward + awardedCredits
      : result.slotsCreditsBalance - pendingEnergyAward
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
    const winTier = getSlotWinTier(
      awardedCredits,
      slotPointsToRand(result.wagerPoints, result.pointValueInCents),
    )
    const wager = slotPointsToRand(result.wagerPoints, result.pointValueInCents)
    const tier = getWinPresentationTier(awardedCredits, wager)
    const isBigWin = tier !== 'win'
    const initialHoldDuration = tier === 'jackpot'
      ? bigWinCountDurationMs / speedMultiplier
      : tier === 'big'
        ? bigWinCountDurationMs * 0.68 / speedMultiplier
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
    const balanceTarget = creditTileRef.current ?? document.querySelector<HTMLElement>('.in-game-shell > [data-game-navbar] .site-header-balance')
    const destinationRect = balanceTarget?.getBoundingClientRect()
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
      winTier,
      isFlying: false,
      tier,
      left: startLeft,
      top: startTop,
      travelX: 0,
      travelY: 0,
      durationMs: initialHoldDuration,
    })

    if (isBigWin) {
      const countDuration = initialHoldDuration
      const completed = await animateNumberValue(
        0,
        awardedCredits,
        countDuration,
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
        1,
      )
      if (!completed) {
        settleAward()
        return
      }
      if (countDuration < initialHoldDuration &&
          !await waitForPresentation(initialHoldDuration - countDuration, signal)) {
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
      reel.flatMap((symbol, row) => {
        const isCollected = moneyGrabFeature.valueSymbols
          ? moneyGrabFeature.valueSymbols.includes(symbol)
          : symbol.startsWith(moneyGrabFeature.valueSymbolPrefix)
        return isCollected ? [{ reel: reelIndex, row, symbol }] : []
      }),
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
    const handleKeyboardControl = (event: KeyboardEvent) => {
      const target = event.target instanceof Element ? event.target : null
      const isEditableTarget = target?.closest(
        'a, button, input, select, textarea, summary, [contenteditable="true"], [role="button"], [role="slider"]',
      ) !== null
      const action = getSlotKeyboardAction(event.key, {
        hasOpenDialog: isHelpOpen || isSymbolGuideOpen || isSettingsOpen || isReloadPromptOpen ||
          rainbowBonusSummary !== null,
        isEditableTarget,
        modified: event.altKey || event.ctrlKey || event.metaKey,
        repeated: event.repeat,
      })
      if (action === null) return

      event.preventDefault()
      if (action === 'spin-or-stop') {
        handleSpinButtonClick()
      } else if (action === 'decrease-wager') {
        changeWager(-1)
      } else if (action === 'increase-wager') {
        changeWager(1)
      } else if (action === 'toggle-mute') {
        toggleMuted()
      } else {
        setIsAutoSpinning(false)
        setIsHelpOpen(true)
      }
    }

    window.addEventListener('keydown', handleKeyboardControl)
    return () => window.removeEventListener('keydown', handleKeyboardControl)
  })

  async function handleSpin() {
    if (isHelpOpen || isSymbolGuideOpen || isSettingsOpen || isReloadPromptOpen ||
        spinInProgressRef.current || isSpinning || collectionAwardPresentation !== null || rainbowBonusSummaryPendingRef.current) {
      return
    }

    if (isSpinServiceDisabled || collectionAwardPresentation !== null) {
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
    const expectsGodsTumble = usesGodsTumble && !expectedFreeSpin
    const autoSpinAmbienceCue = soundSet.events.autoSpinAmbience
    const ambienceCue = isFastAutoSpin && autoSpinAmbienceCue !== undefined
      ? autoSpinAmbienceCue
      : soundSet.events.ambience
    const spinLoopCue = expectedFreeSpin
      ? soundSet.events.specialReelSpin ?? soundSet.events.reelSpin
      : soundSet.events.reelSpin
    const reelStopCue = expectedFreeSpin
      ? soundSet.events.specialReelStop ?? soundSet.events.reelStop
      : soundSet.events.reelStop
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
      setHasSpecialRoundStarted(true)
      setIsSpecialSpinInProgress(true)
    }
    setLastSpinOutcome(null)
    setLastWin(0)
    setWinningPaylines([])
    setLastCollectorAward(0)
    setLightningCollection(null)
    setEnergyAward((current) => current && { ...current, payoutCents: 0 })
    setLastFreeSpinsAwarded(0)
    setLastEnergyAwarded(0)
    setLastEnergyMultiplierApplied(false)
    setHasCompletedSpin(false)
    setEnergyFlyover(null)
    setWinAwardFlyover(null)
    setMoneyGrabPresentation(null)
    if (!expectedFreeSpin) {
      setIsFreeSpinBadgePopping(false)
    }
    beginPerformance(!isFastAutoSpin)
    if (!isFastAutoSpin && !expectedFreeSpin) {
      playCue(soundSet.events.leverPull)
    }
    if (expectedFreeSpin) {
      stopLoop(soundSet.events.ambience)
      if (autoSpinAmbienceCue !== undefined) {
        stopLoop(autoSpinAmbienceCue)
      }
    } else {
      if (isFastAutoSpin && autoSpinAmbienceCue !== undefined) {
        stopLoop(soundSet.events.ambience)
      }
      startLoop(ambienceCue)
    }
    startLoop(spinLoopCue)
    setIsSpinning(true)
    setSpinStage('requesting')
    setSpinError(null)
    setBestWin(null)
    setWinningPaylineCount(0)
    setBonusPositions([])
    const spinInitialReels = expectsGodsTumble ? GODS_TUMBLE_INITIAL_REELS : rules.initialReels
    const regularRowCount = spinInitialReels[0]?.length ?? 4
    const expectedRowCount = getSpinRowCount(
      expectedFreeSpin,
      freeSpinFeatureMode,
      regularRowCount,
    )
    const reelsBeforeSpin = spinInitialReels.map((reel, reelIndex) =>
      normalizeReelRows(displayedReels[reelIndex] ?? reel, reel, expectedRowCount),
    )
    if (displayedReels.length !== reelsBeforeSpin.length || displayedReels.some((reel) => reel.length !== expectedRowCount)) {
      setDisplayedReels(reelsBeforeSpin)
    }
    if (reelMotion.length !== reelsBeforeSpin.length) {
      setReelMotion(reelsBeforeSpin.map(() => 'idle'))
    }
    const displayFrame = (reelIndex: number, symbols: readonly SlotSymbolId[]) => {
      setDisplayedReels((currentReels) =>
        currentReels.map((reel, index) =>
          index === reelIndex ? [...symbols] : reel,
        ),
      )
    }
    const animation = startSpinAnimation({
      reelCount: reelsBeforeSpin.length,
      rowsPerReel: expectedRowCount,
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
      speedMultiplier: isFastAutoSpin ? autoSpinSpeedMultiplier : baseSpinSpeedMultiplier,
      travelRows: reelTravelRowCount,
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
        stopLoop(spinLoopCue)
      }
      await stopSpinAnimationWithCadence({
        animation,
        isQuickStopRequested: () => stopSpinRequestedRef.current,
        onReelStopped: (_, stoppedReelCount) => {
          playCue(reelStopCue)
          if (stoppedReelCount === targetReels.length) {
            stopLoop(spinLoopCue)
          }
        },
        quickBrakeDurationMs: manualStopBrakeDurationMs,
        quickSettleAfterStopMs: manualStopSettleDurationMs,
        settleAfterLastReelMs:
          reelLandingSettleDurationMs /
          (isFastAutoSpin ? autoSpinSpeedMultiplier : baseSpinSpeedMultiplier),
        shouldSnapToTarget: () => prefersReducedMotionRef.current,
        snapToTarget: settleWithoutMotion,
        targetReels,
      })
    }
    let hasStartedStoppingResult = false
    let mascotOutcome: MascotOutcome = 'neutral'
    let shouldStartAutoSpinCooldown = false
    let completedRainbowBonus: { totalRand: number | null } | null = null
    let confirmedPaidTumbleBalance: number | null = null
    const showGodsTumbleShowcase = import.meta.env.DEV && demoMode && expectsGodsTumble && !hasShownGodsTumbleShowcaseRef.current &&
      new URLSearchParams(window.location.search).get('showcase') === '1'

    try {
      const result = demoMode
        ? await requestDemoSpin({
            ...energyAward?.demoState,
            gameId,
            wagerPoints: wagerForSpin,
            useFreeSpin: expectedFreeSpin,
            freeSpinsRemaining,
            freeSpinWagerPoints,
            energyBalance,
            sealCollections,
            freeSpinFeatureMode,
            ...(gameId === 'dragon-hoard-v1' ? { midnightBonusState } : {}),
            ...(expectsGodsTumble ? { olympusTumbleShowcase: showGodsTumbleShowcase } : {}),
          })
        : await requestSpin({
            gameId,
            wagerPoints: wagerForSpin,
            useFreeSpin: expectedFreeSpin,
            useSpecialBoost: requestedSpecialBoost,
          })

      const energyAwardResult = energyCompletionAward?.decode(result)
      if (energyAwardResult === null) {
        throw new Error('The spin server returned an invalid reel result.')
      }
      if (result.reels.length !== reelsBeforeSpin.length) {
        throw new Error(`Expected ${reelsBeforeSpin.length} reels but received ${result.reels.length}.`)
      }
      if (result.reels.some((reel) => reel.length !== expectedRowCount)) {
        throw new Error(`Expected ${expectedRowCount} visible rows per reel but received an invalid slot grid.`)
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
      const tumble = expectsGodsTumble ? result.olympusTumble : undefined
      if (expectsGodsTumble && !tumble) {
        throw new Error('The spin server did not return an Olympus tumble result.')
      }
      if (tumble && !demoMode) confirmedPaidTumbleBalance = result.slotsCreditsBalance

      slotStateRevisionGuardRef.current.advance()
      setSpinStage('stopping')
      hasStartedStoppingResult = true
      await stopReelsWithCadence(result.reels)
      if (!isMountedRef.current) {
        return
      }

      if (tumble) {
        if (!isCurrentPresentation(presentationSignal)) return

        let totalPoints = 0
        for (const [index, { awards, cleared, dropRows, reels, amountPoints }] of tumble.steps.entries()) {
          if (presentationSignal.aborted) break
          const frame = { cleared, dropRows, amountPoints, step: index + 1 }
          for (const award of awards) {
            totalPoints += award.amountPoints
            setGodsTumbleFrame({ ...frame, totalPoints, phase: 'award', award })
            setLastWin(slotPointsToRand(totalPoints, pointValueInCents))
            if (!await waitForPresentation(godsTumbleAwardMs, presentationSignal)) break
          }
          if (presentationSignal.aborted) break
          if (!isCurrentPresentation(presentationSignal)) return
          setGodsTumbleFrame({ ...frame, totalPoints, phase: 'clear' })
          if (!await waitForPresentation(godsTumbleClearMs, presentationSignal)) break
          if (!isCurrentPresentation(presentationSignal)) return
          setDisplayedReels(reels)
          setGodsTumbleFrame({ ...frame, totalPoints, phase: 'drop' })
          if (!await waitForPresentation(prefersReducedMotionRef.current ? 120 : godsTumbleDropMs, presentationSignal)) break
        }

        // Stop skips the remaining animation and settles the completed spin.
        if (!isCurrentPresentation(presentationSignal)) return
        const awardRand = slotPointsToRand(tumble.totalPoints, pointValueInCents)
        setDisplayedReels(tumble.steps.at(-1)?.reels ?? tumble.initialReels)
        setLastWin(awardRand)
        setBalance(result.slotsCreditsBalance ?? (visibleBalanceBeforeAward + awardRand))
        setLastSpinOutcome(describeSpinOutcome({
          awardRand,
          wagerRand: selectedWagerRand,
          freeSpinsAwarded: 0,
          narrative: outcomeNarrative,
        }))
        setHasCompletedSpin(true)
        setLastSpinOutcomeKey((current) => current + 1)
        if (showGodsTumbleShowcase) hasShownGodsTumbleShowcaseRef.current = true
        mascotOutcome = awardRand > 0 ? 'win' : 'loss'
        shouldStartAutoSpinCooldown = isFastAutoSpin
        return
      }

      const hasEnergyPayout = energyAwardResult?.completed === true
      const energyCredits = hasEnergyPayout ? energyAwardResult.payoutCents / 100 : 0
      const awardedRand = slotPointsToRand(result.payout.totalPoints, result.pointValueInCents) + energyCredits
      const wagerRand = slotPointsToRand(result.wagerPoints, result.pointValueInCents)
      // Read the authoritative balance for presentation only. Some demo
      // collection awards are omitted from the separate award field.
      const presentedFreeSpinsAwarded = getPresentedFreeSpinsAwarded(result, freeSpinsRemaining, expectedFreeSpin)
      const spinOutcome: SlotSpinOutcome = describeSpinOutcome({
        awardRand: awardedRand,
        wagerRand,
        freeSpinsAwarded: presentedFreeSpinsAwarded,
        narrative: outcomeNarrative,
      })
      const lineWins = result.lightningCollection
        ? result.payout.paylines.filter(({ paylineId }) => paylineId !== lightningCollectionPayoutId)
        : result.payout.paylines
      const bestPayline = findBestPayline(lineWins)
      const winSoundCue = selectOutcomeSoundEvent(
        bestPayline,
        reelsBeforeSpin.length,
        spinOutcome.winTier,
      )
      const triggeredBonusPositions = result.freeSpinsAwarded > 0
        ? result.reels.flatMap((reel, reelIndex) =>
            reel.flatMap((symbol, row) => symbol === 'FREE' ? [{ reel: reelIndex, row }] : []),
          )
        : []
      if (
        (result.payout.totalPoints > 0 ||
          presentedFreeSpinsAwarded > 0)
      ) {
        mascotOutcome = 'win'
      }
      setBestWin(bestPayline)
      setWinningPaylineCount(lineWins.length)
      setWinningPaylines(lineWins)
      setBonusPositions(triggeredBonusPositions)
      setLastSpinOutcome(spinOutcome)
      setResultAtmosphereId((current) => current + 1)
      const spinWinnings = awardedRand
      setLastWin(spinWinnings)
      setLightningCollection(result.lightningCollection ?? null)
      if (featureSet.moneyGrab?.presentation === 'separate-award') {
        setLastCollectorAward(slotPointsToRand(result.moneyGrabPoints, result.pointValueInCents))
      }
      if (expectedFreeSpin) {
        setSpecialRoundWinnings((current) => current + spinWinnings)
        if (gameId === 'rainbow-realm-fruits-v1') {
          rainbowBonusWinningsCentsRef.current += result.payout.totalPoints * result.pointValueInCents
          if (result.freeSpinsRemaining === 0) {
            completedRainbowBonus = {
              totalRand: rainbowBonusStartedHereRef.current
                ? rainbowBonusWinningsCentsRef.current / 100
                : null,
            }
          }
        }
        const heldCollection = heldCompletedCollectionId
          ? result.sealCollections.find((collection) => collection.sealId === heldCompletedCollectionId)
          : undefined
        setSpecialRoundGemCount(heldCollection?.count ?? 0)
      } else if (presentedFreeSpinsAwarded > 0 || (
        gameId === 'rainbow-realm-fruits-v1' && freeSpinsRemaining === 0 && result.freeSpinsRemaining > 0
      ) || (
        gameId === 'reel-riches-v1' && result.freeSpinsRemaining > freeSpinsRemaining
      )) {
        setSpecialRoundWinnings(0)
        if (gameId === 'rainbow-realm-fruits-v1' && result.freeSpinsRemaining > 0) {
          rainbowBonusWinningsCentsRef.current = 0
          rainbowBonusStartedHereRef.current = true
        }
      }
      setLastFreeSpinsAwarded(presentedFreeSpinsAwarded)
      setLastEnergyMultiplierApplied(result.energyMultiplierApplied)
      setHasCompletedSpin(true)
      setLastSpinOutcomeKey((current) => current + 1)
      setFreeSpinsRemaining(result.freeSpinsRemaining)
      setFreeSpinWagerPoints(
        result.freeSpinsRemaining > 0
          ? result.freeSpinWagerPoints ?? freeSpinWagerPoints ?? result.wagerPoints
          : null,
      )
      setFreeSpinFeatureMode(result.freeSpinFeatureMode)
      if (gameId === 'dragon-hoard-v1') {
        setMidnightBonusState(result.midnightBonusState ?? null)
        setMidnightBonusRound(result.bonusRound ?? null)
        setMidnightQueuedRounds(result.queuedBonusRounds?.length ?? 0)
      }
      let resultSoundEvent: SlotResultSoundEvent | null = winSoundCue
      if (presentedFreeSpinsAwarded > 0 || (
        gameId === 'reel-riches-v1' && !expectedFreeSpin && result.freeSpinsRemaining > freeSpinsRemaining
      )) {
        resultSoundEvent = 'bonus'
      } else if (awardedRand === 0) {
        mascotOutcome = 'loss'
        resultSoundEvent = 'no-win'
      }
      const suiteResultCue = soundSet.events.themeSuite
        ? getSuiteResultCue(soundSet, result, resultSoundEvent, expectedFreeSpin, presentedFreeSpinsAwarded) : undefined
      const revealPainted = (resultSoundEvent !== null || suiteResultCue !== undefined) && !presentationSignal.aborted
        ? await waitForPresentationFrame(presentationSignal)
        : false
      const shouldPlayResultCue = (resultSoundEvent !== null || suiteResultCue !== undefined) &&
        revealPainted &&
        !stopSpinRequestedRef.current &&
        // A fast auto-spin keeps the voyage music uninterrupted. In
        // particular, its no-win results must not trigger the wave cue.
        !(isFastAutoSpin && resultSoundEvent === 'no-win' && !(soundSet.events.themeSuite &&
          suiteResultCue !== undefined && suiteResultCue === soundSet.events.bonusComplete))
      if (shouldPlayResultCue) {
        if (soundSet.events.themeSuite) {
          if (suiteResultCue !== undefined && playedSuiteResultRef.current !== result.spinId) {
            playedSuiteResultRef.current = result.spinId
            playCue(suiteResultCue)
          }
        } else if (resultSoundEvent !== null) {
          playSequence(
            expectedFreeSpin
              ? soundSet.events.specialResults?.[resultSoundEvent] ?? soundSet.events.results[resultSoundEvent]
              : soundSet.events.results[resultSoundEvent],
          )
        }
      }
      if (result.lightningCollection && !prefersReducedMotionRef.current) {
        await waitForPresentation(lightningCollectionDurationMs, presentationSignal)
      }
      await animateMoneyGrab(result, isFastAutoSpin, presentationSignal)
      await animateCreditWinAward(
        result,
        visibleBalanceBeforeAward,
        isFastAutoSpin,
        presentationSignal,
        energyCredits,
      )
      if (hasEnergyPayout) {
        await animateEnergyCollection(result, energyAwardResult, isFastAutoSpin, presentationSignal)
        if (isCurrentPresentation(presentationSignal)) {
          setBalance((current) => result.slotsCreditsBalance ?? current + energyCredits)
        }
      }
      await animateSealCollection(result, isFastAutoSpin, presentationSignal, expectedFreeSpin)
      if (!hasEnergyPayout) {
        await animateEnergyCollection(result, energyAwardResult, isFastAutoSpin, presentationSignal)
      }
      shouldStartAutoSpinCooldown = isFastAutoSpin
    } catch (error) {
      completedRainbowBonus = null
      if (!isMountedRef.current) {
        return
      }
      setIsAutoSpinning(false)
      if (demoMode && !expectsGodsTumble) {
        setSpinAvailability('unavailable')
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
      setWinningPaylines([])
      setWinningPaylineCount(0)
      setBonusPositions([])
      if (expectsGodsTumble) setLastWin(0)
      setLastFreeSpinsAwarded(0)
      setLastEnergyAwarded(0)
      setLastEnergyMultiplierApplied(false)
      setLastSpinOutcome(null)
      setEnergyFlyover(null)
      setSealFlyover(null)
      setSealImpactId(null)
      setWinAwardFlyover(null)
      setMoneyGrabPresentation(null)
      setLightningCollection(null)
      if (expectedFreeSpin && !isFreeSpinUnavailable) {
        setFreeSpinsRemaining((current) => current + 1)
      }
      if (confirmedPaidTumbleBalance !== null) {
        setBalance(confirmedPaidTumbleBalance)
      } else if (isInsufficientBalance && error.available !== undefined) {
        setBalance(error.available)
      } else {
        setBalance((currentBalance) => currentBalance + optimisticCharge)
      }
      setSpinError(error instanceof Error ? error.message : 'The spin could not be completed.')
      if (!demoMode && error instanceof SpinRequestError && error.code === 'free-spins-required') {
        const state = await requestSlotState(gameId).catch(() => null)
        if (state && isCurrentPresentation(presentationSignal)) {
          applySlotState(state)
          if (state.freeSpinsRemaining > 0) setSpinError(null)
        }
      }
    } finally {
      stopLoop(spinLoopCue)
      if (isMountedRef.current) {
        finishSpinAnimation(animation)
        if (expectsGodsTumble) setGodsTumbleFrame(null)
        setIsEnergyAwardCompleting(false)
        completePerformance(mascotOutcome, !isFastAutoSpin)
        setIsAutoSpinCoolingDown(shouldStartAutoSpinCooldown)
        setIsSpinning(false)
        setIsFastSpinActive(false)
        setIsStopRequested(false)
        if (expectedFreeSpin) {
          setIsSpecialSpinInProgress(false)
        }
        if (completedRainbowBonus !== null) {
          rainbowBonusSummaryPendingRef.current = true
          setRainbowBonusSummary(completedRainbowBonus)
          setIsAutoSpinning(false)
        }
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
  const canAffordSelectedWager = useFreeGameForNextSpin || balance >= selectedWagerRand
  const activeWagerDisplay = useFreeGameForNextSpin
    ? slotPointsToRand(freeSpinWagerPoints ?? selectedWagerPoints, pointValueInCents)
    : selectedWagerRand
  const showFreeSpinBadge = isFreeSpinBadgePopping || (!isSpinning && freeSpinsRemaining > 0)
  const visibleSealCollections = defaultSealCollections.map((fallback) => {
    const current = sealCollections.find((collection) => collection.sealId === fallback.sealId)
    return current ?? fallback
  })
  const pageBackdropImage = cabinetTheme.pageBackdropImage ?? cabinetTheme.visualsBackdropImage
  const pageBackdropStyle = {
    ...(pageBackdropImage ? { '--slot-page-backdrop': cabinetTheme.backdropImageSet ?? `url("${pageBackdropImage}")` } : {}),
    '--slot-result-primary': cabinetTheme.palette.trimBright,
    '--slot-result-secondary': cabinetTheme.palette.accent,
    '--slot-result-glow': cabinetTheme.palette.glow,
  } as CSSProperties
  const slotsPageClassName = [
    'slots-page',
    isFastSpinActive ? 'slots-page--fast-spin' : '',
    isSpecialGameActive ? 'slots-page--special-game' : '',
    pageBackdropImage ? 'slots-page--theme-backdrop' : '',
  ].filter(Boolean).join(' ')

  function changeWager(direction: -1 | 1) {
    if (freeSpinsRemaining > 0) {
      setSpinError('Free spins are locked to the wager that won them.')
      return
    }

    setIsAutoSpinning(false)
    if (soundSet.events.themeSuite && soundSet.events.betStep &&
      Math.min(wagerOptions.length - 1, Math.max(0, wagerIndex + direction)) !== wagerIndex) playCue(soundSet.events.betStep)
    setWagerIndex((currentIndex) =>
      Math.min(wagerOptions.length - 1, Math.max(0, currentIndex + direction)),
    )
    setSpinError(null)
  }

  function selectWager(nextIndex: number) {
    if (freeSpinsRemaining > 0) {
      setSpinError('Free spins are locked to the wager that won them.')
      return
    }

    setIsAutoSpinning(false)
    const selectedIndex = Math.min(wagerOptions.length - 1, Math.max(0, Math.round(nextIndex)))
    if (soundSet.events.themeSuite && soundSet.events.betStep && selectedIndex !== wagerIndex) playCue(soundSet.events.betStep)
    setWagerIndex(selectedIndex)
    setSpinError(null)
  }

  function handleSpinButtonClick() {
    if (rainbowBonusSummaryPendingRef.current) return
    if (isHelpOpen || isSymbolGuideOpen || isSettingsOpen || isReloadPromptOpen) return
    if (spinInProgressRef.current || isSpinning) {
      if (!stopSpinRequestedRef.current && soundSet.events.stopButtonClick) {
        playCue(soundSet.events.stopButtonClick)
      }
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

    if (collectionAwardPresentation !== null) {
      dismissCollectionAwardPresentation()
      return
    }

    if (isSpinServiceDisabled) {
      return
    }

    if (soundSet.events.spinButtonClick) {
      playCue(soundSet.events.spinButtonClick)
    }

    if (isSpecialSpinDelayActive) {
      if (specialSpinDelayTimerRef.current !== null) {
        window.clearTimeout(specialSpinDelayTimerRef.current)
        specialSpinDelayTimerRef.current = null
      }
      setIsSpecialSpinDelayActive(false)
      void handleSpin()
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
    const currentMotion = reelMotion[reelIndex]
    const isMoving =
      currentMotion === 'accelerating' ||
      currentMotion === 'spinning' ||
      currentMotion === 'braking'
    const visibleRowCount = isMoving && symbolRowCount > reelTravelRowCount
      ? symbolRowCount - reelTravelRowCount
      : symbolRowCount
    const stripHeight = symbolRowCount / Math.max(1, visibleRowCount) * 100
    const travel = -Math.max(0, symbolRowCount - visibleRowCount) / symbolRowCount * 100
    return {
      '--slot-symbol-rows': symbolRowCount,
      '--slot-strip-height': `${stripHeight}%`,
      '--slot-spin-travel': `${travel}%`,
    } as CSSProperties
  }

  return {
    account,
    balanceActionHref,
    balanceRecoveryHref,
    balanceRecoveryLabel,
    activeWagerDisplay,
    audioPreferences,
    balance,
    bestWin,
    cabinetTheme,
    canAffordSelectedWager,
    changeWager,
    closeSettings,
    collectionAwardPresentation,
    creditTileRef,
    displayedReels,
    dismissCollectionAwardPresentation,
    dismissRainbowBonusSummary,
    spinAvailability,
    spinAvailabilityMessage,
    demoMode,
    demoStartingBalance,
    energyBalance,
    energyFlyover,
    energyImpactKey,
    energyMeterCapacity,
    energyMeterRef,
    energyAward,
    isEnergyAwardCompleting,
    featureSet,
    freeSpinFeatureMode,
    midnightBonusRound,
    midnightQueuedRounds,
    freeSpinsRemaining,
    hasCompletedSpin,
    godsTumbleFrame,
    handleSpinButtonClick,
    helpCloseButtonRef,
    isAutoSpinning,
    isFreeSpinBadgePopping,
    isHelpOpen,
    isSymbolGuideOpen,
    isSpinServiceDisabled,
    isGodsTumbleBoard,
    isReloadPromptOpen,
    isSpecialGameActive,
    isSpecialSpinDelayActive,
    isSettingsOpen,
    isSpinning,
    isStopRequested,
    lastEnergyAwarded,
    lastEnergyMultiplierApplied,
    lastCollectorAward,
    lastFreeSpinsAwarded,
    lastSpinOutcomeKey,
    lastWin,
    lastSpinOutcome,
    lightningCollection,
    mascotActionKey,
    mascotPhase,
    mascotSet,
    mascotSuccessFrame,
    help,
    moneyGrabPresentation,
    pageBackdropStyle,
    pointValueInCents,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    rainbowBonusSummary,
    resultAtmosphereId,
    reloadPromptCloseButtonRef,
    selectedWager: selectedWagerRand,
    sealFlyover,
    sealImpactId,
    heldCompletedCollectionId,
    specialRoundGemCount,
    specialRoundWinnings,
    setIsAutoSpinning,
    setIsHelpOpen,
    setIsSymbolGuideOpen,
    setIsReloadPromptOpen,
    setIsSettingsOpen,
    setSpinError,
    selectWager,
    setVolume,
    showFreeSpinBadge,
    slotsPageClassName,
    spinError,
    spinStage,
    symbolSet,
    toggleMuted,
    toggleResultsOnly,
    useFreeGameForNextSpin,
    usesGodsTumble,
    visibleSealCollections,
    wagerIndex,
    wagerOptions,
    winAwardFlyover,
    winningPaylineCount,
    winningPaylines,
    winningPositions,
  }
}

function getSpinRowCount(
  isFreeSpin: boolean,
  featureMode: string | null,
  regularRowCount: number,
): number {
  const hasExtraRows = isFreeSpin && featureMode?.split('-').includes(extraRowsFeatureMode)
  return regularRowCount + (hasExtraRows ? 2 : 0)
}

function normalizeReelRows(
  reel: readonly SlotSymbolId[],
  fallbackReel: readonly SlotSymbolId[] | undefined,
  rowCount: number,
): SlotSymbolId[] {
  return Array.from(
    { length: rowCount },
    (_, rowIndex) => reel[rowIndex] ?? fallbackReel?.[rowIndex] ?? '2',
  )
}

export type SlotsPageController = ReturnType<typeof useSlotsPageController>
