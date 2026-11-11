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
  waitForPresentation,
  waitForPresentationFrame,
  type GridPosition,
  type ReelMotionState,
  type SpinAnimation,
} from '@fortuneforge/slot-client-core'
import { createGoldFoundryApi, GoldFoundryRequestError } from './api.js'
import { GOLD_FOUNDRY_SOUNDS } from './audio.js'
import { decodeCrucibleAward, type GoldCrucibleAward } from './crucibleAward.js'
import { GOLD_CRUCIBLE_POUR_MS } from './GoldCrucible.js'
import { GOLD_FOUNDRY_CABINET, GOLD_FOUNDRY_RULES } from './manifest.js'
import { FOUNDRY_INGOT_IDS, GOLD_FOUNDRY_SYMBOLS } from './symbols.js'
import type {
  GoldFoundryPaylinePayout,
  GoldFoundrySpinResult,
  GoldFoundryState,
  GoldFoundrySymbolId,
  GoldFoundryGameProps,
} from './types.js'

type SpinAvailability = 'checking' | 'available' | 'unavailable'
type FoundryOutcome = { kind: 'loss' | 'win' | 'great-win' | 'big-win'; title: string; tier: 'regular' | 'great' | 'big' | null }
type MoneyGrabTokenFlyover = {
  id: number
  symbol: GoldFoundrySymbolId
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
type WinAward = {
  id: number
  amount: number
  displayAmount: number
  winTier: 'regular' | 'great' | 'big'
  tier: 'win' | 'big' | 'jackpot'
  isFlying: boolean
  left: number
  top: number
  travelX: number
  travelY: number
  durationMs: number
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

const demoStartingBalance = 10_000
const regularWinHoldDurationMs = 380
const regularWinBalanceCountDurationMs = 420
const bigWinCountDurationMs = 1_250
const bigWinBalanceCountDurationMs = 720
const winFlyoverDurationMs = 540
const autoSpinWinPresentationMaxMultiplier = 1.35
const pointValueInCents = GOLD_FOUNDRY_RULES.pointValueInCents
const money = (points: number) => points * pointValueInCents / 100
const emptyState = (): GoldFoundryState => ({
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: 0,
  energyBalance: 0, sealCollections: [], freeSpinFeatureMode: null,
  crucibleWageredCents: 0, cruciblePourValueCents: 0,
})

const normalizedRows = (reel: readonly GoldFoundrySymbolId[], fallback: readonly GoldFoundrySymbolId[]) =>
  Array.from({ length: 4 }, (_, index) => reel[index] ?? fallback[index % fallback.length] ?? '2')

const bestPayline = (paylines: readonly GoldFoundryPaylinePayout[]) =>
  [...paylines].sort((left, right) => right.amountPoints - left.amountPoints || left.paylineId - right.paylineId)[0] ?? null

function outcome(award: number, wager: number): FoundryOutcome {
  if (award <= 0) return { kind: 'loss', title: 'No win this spin', tier: null }
  if (award >= Math.max(500, wager * 50)) return { kind: 'big-win', title: 'Big foundry win', tier: 'big' }
  if (award >= wager * 10) return { kind: 'great-win', title: 'Golden haul', tier: 'great' }
  return { kind: 'win', title: 'Foundry win', tier: 'regular' }
}

function resultSound(result: GoldFoundrySpinResult, best: GoldFoundryPaylinePayout | null, spinOutcome: FoundryOutcome) {
  if (spinOutcome.tier === 'big') return 'five' as const
  if (spinOutcome.tier === 'great') return 'premium' as const
  if (best?.matches.some(match => match.match.matchLength === result.reels.length)) return 'five' as const
  if (best && best.matches.filter(match => match.match.matchLength >= 3 && match.match.matchLength < result.reels.length).length >= 2)
    return 'premium' as const
  if (result.bananaBonusPoints > 0) return 'triple-sevens' as const
  if (spinOutcome.kind === 'loss') return 'no-win' as const
  return best ? 'single-three' as const : null
}

/** Owns all Gold Foundry gameplay and presentation state. The app supplies transport and shared chrome only. */
export function useGoldFoundry({ account, demoMode = false, authenticatedFetch, onSpinStateChange }: GoldFoundryGameProps) {
  const api = useMemo(() => createGoldFoundryApi(authenticatedFetch), [authenticatedFetch])
  const reducedMotion = usePrefersReducedMotion()
  const reducedMotionRef = useRef(reducedMotion)
  reducedMotionRef.current = reducedMotion
  const audio = useSlotAudio(GOLD_FOUNDRY_SOUNDS)
  const [state, setState] = useState<GoldFoundryState>(emptyState)
  const [displayedReels, setDisplayedReels] = useState<GoldFoundrySymbolId[][]>(() =>
    GOLD_FOUNDRY_RULES.initialReels.map(reel => [...reel]))
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(() => Array(5).fill('idle'))
  const [balance, setBalance] = useState(demoMode ? demoStartingBalance : account?.balances.slotsCredits ?? 0)
  const [wagerIndex, setWagerIndex] = useState(0)
  const [spinAvailability, setSpinAvailability] = useState<SpinAvailability>('checking')
  const [isSpinning, setIsSpinning] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isAutoSpinCoolingDown, setIsAutoSpinCoolingDown] = useState(false)
  const [isFastSpinActive, setIsFastSpinActive] = useState(false)
  const [spinError, setSpinError] = useState<string | null>(null)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isSymbolGuideOpen, setIsSymbolGuideOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [lastSpinOutcome, setLastSpinOutcome] = useState<FoundryOutcome | null>(null)
  const [lastSpinOutcomeKey, setLastSpinOutcomeKey] = useState(0)
  const [lastWin, setLastWin] = useState(0)
  const [lastCollectorAward, setLastCollectorAward] = useState(0)
  const [winningPaylines, setWinningPaylines] = useState<GoldFoundryPaylinePayout[]>([])
  const [crucibleAward, setCrucibleAward] = useState<GoldCrucibleAward | null>(() => decodeCrucibleAward({}))
  const [isCruciblePouring, setIsCruciblePouring] = useState(false)
  const [moneyGrabPresentation, setMoneyGrabPresentation] = useState<MoneyGrabPresentation | null>(null)
  const [energyFlyover, setEnergyFlyover] = useState<EnergyFlyover | null>(null)
  const [winAward, setWinAward] = useState<WinAward | null>(null)
  const [resultAtmosphereId, setResultAtmosphereId] = useState(0)
  const helpCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const reloadPromptCloseButtonRef = useRef<HTMLButtonElement | null>(null)
  const energyMeterRef = useRef<HTMLDivElement | null>(null)
  const animation = useRef<SpinAnimation<GoldFoundrySymbolId> | null>(null)
  const presentation = useRef<AbortController | null>(null)
  const mounted = useRef(false)
  const busy = useRef(false)
  const stopRequested = useRef(false)
  const handleSpinRef = useRef<() => Promise<void>>(async () => undefined)
  const selectedWagerPoints = GOLD_FOUNDRY_RULES.wagerOptions[wagerIndex] ?? 2
  const activeWagerDisplay = money(selectedWagerPoints)
  const blocked = isHelpOpen || isSettingsOpen || isSymbolGuideOpen || isReloadPromptOpen
  const isCurrentPresentation = (signal: AbortSignal) =>
    mounted.current && presentation.current?.signal === signal

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      presentation.current?.abort()
      if (animation.current) cancelSpinAnimation(animation.current)
    }
  }, [])
  useEffect(() => { onSpinStateChange?.(isSpinning); return () => onSpinStateChange?.(false) }, [isSpinning, onSpinStateChange])

  useEffect(() => {
    const abort = new AbortController()
    setSpinAvailability('checking')
    const load = demoMode ? api.availability(abort.signal) : api.state().then(loaded => {
      if (abort.signal.aborted) return
      setState(loaded)
      setCrucibleAward({ crucibleWageredCents: loaded.crucibleWageredCents,
        valueCents: loaded.cruciblePourValueCents, payoutCents: 0, completed: false })
    })
    void load.then(() => { if (!abort.signal.aborted) setSpinAvailability('available') }, () => {
      if (!abort.signal.aborted) { setSpinAvailability('unavailable'); setIsAutoSpinning(false) }
    })
    return () => abort.abort()
  }, [api, demoMode])

  const { playCue, playSequence, startLoop, stopLoop, stopResultCues, toggleMuted } = audio
  useEffect(() => {
    const start = () => { if (!document.hidden) startLoop(GOLD_FOUNDRY_SOUNDS.events.ambience) }
    start()
    const gesture = (event: Event) => { if (event.isTrusted) start() }
    document.addEventListener('pointerdown', gesture)
    document.addEventListener('keydown', gesture)
    document.addEventListener('visibilitychange', start)
    window.addEventListener('focus', start)
    return () => {
      document.removeEventListener('pointerdown', gesture); document.removeEventListener('keydown', gesture)
      document.removeEventListener('visibilitychange', start); window.removeEventListener('focus', start)
      stopLoop(GOLD_FOUNDRY_SOUNDS.events.ambience)
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
    const timer = window.setTimeout(() => setIsAutoSpinCoolingDown(false), GOLD_FOUNDRY_RULES.autoSpinDelayMs)
    return () => window.clearTimeout(timer)
  }, [isAutoSpinCoolingDown])

  useEffect(() => {
    if (!isAutoSpinning || spinAvailability !== 'available' || busy.current || isSpinning || isAutoSpinCoolingDown || blocked) return
    if (balance < activeWagerDisplay) { setIsAutoSpinning(false); setIsReloadPromptOpen(true); return }
    const timer = window.setTimeout(() => { void handleSpinRef.current() }, 0)
    return () => window.clearTimeout(timer)
  }, [activeWagerDisplay, balance, blocked, isAutoSpinCoolingDown, isAutoSpinning, isSpinning, spinAvailability])

  const changeWager = useCallback((direction: -1 | 1) => {
    setIsAutoSpinning(false)
    setWagerIndex(current => {
      const next = Math.min(GOLD_FOUNDRY_RULES.wagerOptions.length - 1, Math.max(0, current + direction))
      if (next !== current) playCue(GOLD_FOUNDRY_SOUNDS.events.betStep ?? 'button-click')
      return next
    })
    setSpinError(null)
  }, [playCue])
  const selectWager = useCallback((index: number) => {
    setIsAutoSpinning(false)
    const next = Math.min(GOLD_FOUNDRY_RULES.wagerOptions.length - 1, Math.max(0, Math.round(index)))
    if (next !== wagerIndex) playCue(GOLD_FOUNDRY_SOUNDS.events.betStep ?? 'button-click')
    setWagerIndex(next)
    setSpinError(null)
  }, [playCue, wagerIndex])

  function animateNumber(from: number, to: number, durationMs: number, signal: AbortSignal,
    onValue: (value: number) => void, precision = 100): Promise<boolean> {
    if (signal.aborted || durationMs <= 0 || from === to) { onValue(to); return Promise.resolve(!signal.aborted) }
    return new Promise(resolve => {
      const started = performance.now()
      let frame = 0
      let settled = false
      const finish = (completed: boolean) => {
        if (settled) return
        settled = true
        window.cancelAnimationFrame(frame)
        signal.removeEventListener('abort', abort)
        onValue(to)
        resolve(completed)
      }
      const abort = () => finish(false)
      const step = (time: number) => {
        if (signal.aborted) { finish(false); return }
        const progress = Math.min(1, (time - started) / durationMs)
        const eased = 1 - Math.pow(1 - progress, 3)
        onValue(Math.round((from + (to - from) * eased) * precision) / precision)
        if (progress < 1) frame = window.requestAnimationFrame(step)
        else finish(true)
      }
      signal.addEventListener('abort', abort, { once: true })
      frame = window.requestAnimationFrame(step)
    })
  }

  async function animateCreditWinAward(result: GoldFoundrySpinResult, visibleBalanceBeforeAward: number,
    fast: boolean, signal: AbortSignal, pendingCrucibleAward: number) {
    const awardedCredits = Math.max(0, money(result.payout.totalPoints))
    const finalBalance = result.slotsCreditsBalance === null
      ? visibleBalanceBeforeAward + awardedCredits
      : result.slotsCreditsBalance - pendingCrucibleAward
    const settle = () => {
      if (!isCurrentPresentation(signal)) return
      setBalance(finalBalance)
      setWinAward(null)
    }
    if (awardedCredits <= 0 || reducedMotionRef.current || signal.aborted) { settle(); return }
    const speed = fast
      ? Math.min(Math.max(1, GOLD_FOUNDRY_RULES.autoSpinSpeedMultiplier), autoSpinWinPresentationMaxMultiplier)
      : 1
    const spinTier = outcome(awardedCredits, money(result.wagerPoints)).tier ?? 'regular'
    const tier: WinAward['tier'] = awardedCredits >= Math.max(500, money(result.wagerPoints) * 50)
      ? 'jackpot' : awardedCredits >= Math.max(25, money(result.wagerPoints) * 10) ? 'big' : 'win'
    const initialHold = tier === 'jackpot' ? bigWinCountDurationMs / speed
      : tier === 'big' ? bigWinCountDurationMs * .68 / speed : regularWinHoldDurationMs / speed
    const flyoverDuration = winFlyoverDurationMs / speed
    const balanceDuration = tier === 'win' ? regularWinBalanceCountDurationMs / speed : bigWinBalanceCountDurationMs / speed
    if (!await waitForPresentation(fast ? 40 : 110, signal) || !await waitForPresentationFrame(signal)) {
      settle(); return
    }
    const sourceRect = (document.querySelector<HTMLElement>('.slot-game-frame') ??
      document.querySelector<HTMLElement>('.slots-page__stage'))?.getBoundingClientRect()
    const targetRect = document.querySelector<HTMLElement>(
      '.in-game-shell > [data-game-navbar] .site-header-balance')?.getBoundingClientRect()
    const left = sourceRect ? sourceRect.left + sourceRect.width / 2 : window.innerWidth / 2
    const top = sourceRect ? sourceRect.top + sourceRect.height * .38 : window.innerHeight * .38
    const destinationLeft = targetRect ? targetRect.left + targetRect.width / 2 : left
    const destinationTop = targetRect ? targetRect.top + targetRect.height / 2 : top
    const id = Date.now()
    setWinAward({ id, amount: awardedCredits, displayAmount: tier === 'win' ? awardedCredits : 0,
      winTier: spinTier, tier, isFlying: false, left, top, travelX: 0, travelY: 0, durationMs: initialHold })
    if (tier !== 'win') {
      if (!await animateNumber(0, awardedCredits, initialHold, signal, displayAmount =>
        setWinAward(current => current?.id === id ? { ...current, displayAmount } : current))) { settle(); return }
    } else if (!await waitForPresentation(initialHold, signal)) { settle(); return }
    if (!isCurrentPresentation(signal)) return
    const flyingId = Date.now() + 1
    setWinAward(current => current ? { ...current, id: flyingId, displayAmount: awardedCredits,
      isFlying: true, travelX: destinationLeft - left, travelY: destinationTop - top,
      durationMs: flyoverDuration } : current)
    const countBalance = (async () => {
      if (!await waitForPresentation(flyoverDuration * .28, signal)) return false
      return animateNumber(visibleBalanceBeforeAward, finalBalance, balanceDuration, signal,
        value => { if (isCurrentPresentation(signal)) setBalance(value) })
    })()
    const [flew, counted] = await Promise.all([waitForPresentation(flyoverDuration, signal), countBalance])
    if (!flew || !counted) { settle(); return }
    settle()
  }

  async function animateMoneyGrab(result: GoldFoundrySpinResult, fast: boolean, signal: AbortSignal) {
    const settle = () => { if (isCurrentPresentation(signal)) setMoneyGrabPresentation(null) }
    if (result.moneyGrabPoints <= 0 || result.monkeyPawCount <= 0 || reducedMotionRef.current || signal.aborted) {
      settle()
      return
    }
    const pawPosition = result.reels.flatMap((reel, reelIndex) => reel.flatMap((symbol, row) =>
      symbol === 'PAW' ? [{ reel: reelIndex, row }] : []))[0]
    const moneyPositions = result.reels.flatMap((reel, reelIndex) => reel.flatMap((symbol, row) =>
      FOUNDRY_INGOT_IDS.includes(symbol as typeof FOUNDRY_INGOT_IDS[number]) ? [{ reel: reelIndex, row, symbol }] : []))
    if (!pawPosition || moneyPositions.length === 0 || !await waitForPresentationFrame(signal) || signal.aborted) {
      settle()
      return
    }
    const rectFor = (reel: number, row: number) => document.querySelector<HTMLElement>(
      `.slot-symbol[data-reel-index="${reel}"][data-row-index="${row}"]`)?.getBoundingClientRect()
    const pawRect = rectFor(pawPosition.reel, pawPosition.row)
    const moneyRects = moneyPositions.flatMap(position => {
      const rect = rectFor(position.reel, position.row)
      return rect && rect.width > 0 && rect.height > 0 ? [{ ...position, rect }] : []
    })
    if (!pawRect || pawRect.width <= 0 || pawRect.height <= 0 || moneyRects.length === 0) {
      settle()
      return
    }
    const targetX = moneyRects.reduce((sum, { rect }) => sum + rect.left + rect.width / 2, 0) / moneyRects.length
    const targetY = moneyRects.reduce((sum, { rect }) => sum + rect.top + rect.height / 2, 0) / moneyRects.length
    const frameRect = document.querySelector<HTMLElement>('.slot-game-frame')?.getBoundingClientRect()
    const pawCenterX = pawRect.left + pawRect.width / 2
    const pawCenterY = pawRect.top + pawRect.height / 2
    const pawSize = Math.min(148, Math.max(76, Math.max(pawRect.width, pawRect.height) * 1.42))
    const pawDurationMs = fast ? 410 : 650
    const tokenDurationMs = fast ? 320 : 500
    const tokenDelayStepMs = fast ? 22 : 42
    const popupDelayMs = fast ? 245 : 420
    const popupDurationMs = fast ? 330 : 560
    const id = Date.now()
    const tokens = moneyRects.map(({ rect, reel, row, symbol }, index) => ({
      id: id + index + 1, symbol, reel, row, left: rect.left, top: rect.top,
      width: rect.width, height: rect.height,
      travelX: targetX - (rect.left + rect.width / 2), travelY: targetY - (rect.top + rect.height / 2),
      delayMs: index * tokenDelayStepMs, durationMs: tokenDurationMs,
    }))
    const totalDurationMs = Math.max(pawDurationMs,
      tokenDurationMs + (tokens.length - 1) * tokenDelayStepMs, popupDelayMs + popupDurationMs)
    setMoneyGrabPresentation({ id, amount: money(result.moneyGrabPoints),
      pawLeft: pawCenterX - pawSize / 2, pawTop: pawCenterY - pawSize / 2, pawSize,
      pawTravelX: targetX - pawCenterX, pawTravelY: targetY - pawCenterY, pawDurationMs,
      popupLeft: frameRect ? frameRect.left + frameRect.width / 2 : targetX,
      popupTop: frameRect ? frameRect.top + frameRect.height * .4 : targetY,
      popupDelayMs, popupDurationMs, tokens })
    await waitForPresentation(totalDurationMs, signal)
    settle()
  }

  async function animateEnergyCollection(result: GoldFoundrySpinResult, award: GoldCrucibleAward,
    startingEnergy: number, fast: boolean, signal: AbortSignal) {
    const setEnergy = (energyBalance: number) => setState(current => ({ ...current, energyBalance }))
    const settle = () => {
      if (!isCurrentPresentation(signal)) return
      setEnergyFlyover(null)
      setIsCruciblePouring(false)
      setEnergy(result.energyBalance)
      setCrucibleAward(award)
    }
    const showCompletion = async () => {
      setEnergy(GOLD_FOUNDRY_RULES.energyMeterCapacity)
      setCrucibleAward(award)
      setIsCruciblePouring(true)
      await waitForPresentation(GOLD_CRUCIBLE_POUR_MS, signal)
    }
    if (reducedMotionRef.current) { settle(); return }
    const completed = award.completed
    const targetBeforeReset = completed ? GOLD_FOUNDRY_RULES.energyMeterCapacity : result.energyBalance
    const boltPositions = result.reels.flatMap((reel, reelIndex) => reel.flatMap((symbol, row) =>
      symbol === 'BOLT' ? [{ reel: reelIndex, row }] : [])).sort((left, right) => left.reel - right.reel || left.row - right.row)
    if (result.energyAwarded <= 0 || boltPositions.length === 0 || signal.aborted) {
      if (completed && !signal.aborted) await showCompletion()
      settle()
      return
    }
    if (!await waitForPresentation(fast ? 90 : 280, signal) || !await waitForPresentationFrame(signal)) {
      settle()
      return
    }
    let animatedBalance = completed
      ? Math.min(GOLD_FOUNDRY_RULES.energyMeterCapacity, Math.max(0, startingEnergy))
      : Math.max(0, result.energyBalance - result.energyAwarded)
    setEnergy(animatedBalance)
    const perBolt = Math.floor(result.energyAwarded / boltPositions.length)
    let remainder = result.energyAwarded % boltPositions.length
    for (const [index, position] of boltPositions.entries()) {
      const source = document.querySelector<HTMLElement>(
        `.slot-symbol[data-symbol="BOLT"][data-reel-index="${position.reel}"][data-row-index="${position.row}"]`)
      const sourceRect = source?.getBoundingClientRect()
      const destinationRect = energyMeterRef.current?.getBoundingClientRect()
      const increment = perBolt + (remainder > 0 ? 1 : 0)
      remainder = Math.max(0, remainder - 1)
      if (sourceRect && destinationRect) {
        const durationMs = fast ? 390 : 680
        setEnergyFlyover({ id: Date.now() + index, left: sourceRect.left, top: sourceRect.top,
          width: sourceRect.width, height: sourceRect.height,
          travelX: destinationRect.left + destinationRect.width / 2 - (sourceRect.left + sourceRect.width / 2),
          travelY: destinationRect.top + destinationRect.height / 2 - (sourceRect.top + sourceRect.height / 2),
          durationMs })
        if (!await waitForPresentation(durationMs, signal)) { settle(); return }
      }
      animatedBalance = Math.min(targetBeforeReset, animatedBalance + increment)
      setEnergy(animatedBalance)
      setEnergyFlyover(null)
      if (!await waitForPresentation(fast ? 45 : 100, signal)) { settle(); return }
    }
    if (completed) await showCompletion()
    settle()
  }

  async function handleSpin() {
    if (blocked || busy.current || isSpinning || spinAvailability !== 'available') return
    const wagerRand = activeWagerDisplay
    if (balance < wagerRand) { setIsAutoSpinning(false); setSpinError(null); setIsReloadPromptOpen(true); return }
    const fast = isAutoSpinning
    const before = GOLD_FOUNDRY_RULES.initialReels.map((fallback, index) =>
      normalizedRows(displayedReels[index] ?? fallback, fallback))
    const visibleBalanceBeforeAward = balance - wagerRand
    busy.current = true
    presentation.current?.abort()
    const presentationController = new AbortController()
    presentation.current = presentationController
    const presentationSignal = presentationController.signal
    stopRequested.current = false
    setIsStopRequested(false); setIsFastSpinActive(fast); setBalance(visibleBalanceBeforeAward)
    setLastSpinOutcome(null); setLastWin(0); setLastCollectorAward(0); setWinningPaylines([])
    setHasCompletedSpin(false); setSpinError(null); setMoneyGrabPresentation(null); setWinAward(null)
    setCrucibleAward(current => current && { ...current, payoutCents: 0 })
    if (!fast) playCue(GOLD_FOUNDRY_SOUNDS.events.spinButtonClick ?? 'spin-button-click')
    const spinCue = GOLD_FOUNDRY_SOUNDS.events.reelSpin
    startLoop(spinCue)
    setIsSpinning(true); setDisplayedReels(before); setReelMotion(Array(5).fill('idle'))
    const currentAnimation = startSpinAnimation({
      reelCount: 5, rowsPerReel: 4, symbolIds: GOLD_FOUNDRY_SYMBOLS.animationSymbolIds ?? [],
      displayFrame: (reelIndex, symbols) => setDisplayedReels(current => current.map((reel, index) =>
        index === reelIndex ? symbols as GoldFoundrySymbolId[] : reel)),
      setReelMotion: (reelIndex, motion) => setReelMotion(current => current.map((value, index) => index === reelIndex ? motion : value)),
      reducedMotion: reducedMotionRef.current,
      speedMultiplier: fast ? GOLD_FOUNDRY_RULES.autoSpinSpeedMultiplier : GOLD_FOUNDRY_RULES.baseSpinSpeedMultiplier,
      travelRows: GOLD_FOUNDRY_RULES.reelTravelRowCount,
    })
    animation.current = currentAnimation
    const settle = async (target: readonly (readonly GoldFoundrySymbolId[])[]) => stopSpinAnimationWithCadence({
      animation: currentAnimation, isQuickStopRequested: () => stopRequested.current,
      onReelStopped: (_, stopped) => { playCue(GOLD_FOUNDRY_SOUNDS.events.reelStop); if (stopped === target.length) stopLoop(spinCue) },
      quickBrakeDurationMs: 90, quickSettleAfterStopMs: 35,
      settleAfterLastReelMs: reelLandingSettleDurationMs / (fast ? GOLD_FOUNDRY_RULES.autoSpinSpeedMultiplier : 1),
      shouldSnapToTarget: () => reducedMotionRef.current,
      snapToTarget: () => { setDisplayedReels(target.map(reel => [...reel])); setReelMotion(Array(5).fill('stopped')); stopLoop(spinCue) },
      targetReels: target,
    })
    let resultStarted = false
    try {
      const result = await api.spin({ wagerPoints: selectedWagerPoints, state }, demoMode)
      resultStarted = true
      await settle(result.reels)
      if (!mounted.current) return
      const award = decodeCrucibleAward(result)
      if (!award) throw new Error('The Gold Foundry server returned an invalid crucible award.')
      const lineAndFeatureWin = money(result.payout.totalPoints)
      const crucibleWin = award.completed ? award.payoutCents / 100 : 0
      const totalWin = lineAndFeatureWin + crucibleWin
      const spinOutcome = outcome(totalWin, wagerRand)
      const best = bestPayline(result.payout.paylines)
      const nextState: GoldFoundryState = {
        freeSpinsRemaining: 0, freeSpinWagerPoints: null, specialPointsBalance: result.specialPointsBalance,
        energyBalance: result.energyBalance, sealCollections: [], freeSpinFeatureMode: null,
        crucibleWageredCents: award.crucibleWageredCents, cruciblePourValueCents: award.valueCents,
      }
      setState({ ...nextState, energyBalance: state.energyBalance }); setDisplayedReels(result.reels.map(reel => [...reel]))
      setWinningPaylines(result.payout.paylines); setLastWin(totalWin); setLastSpinOutcome(spinOutcome)
      setLastCollectorAward(money(result.moneyGrabPoints)); setHasCompletedSpin(true)
      setLastSpinOutcomeKey(current => current + 1); setResultAtmosphereId(current => current + 1)
      const authoritativeBalance = result.slotsCreditsBalance ?? visibleBalanceBeforeAward + totalWin
      const sound = resultSound(result, best, spinOutcome)
      if (sound && !stopRequested.current && (!fast || sound !== 'no-win')) {
        if (sound === 'triple-sevens') playCue(GOLD_FOUNDRY_SOUNDS.events.tripleSevens ?? 'triple-sevens')
        else playSequence(GOLD_FOUNDRY_SOUNDS.events.results[sound])
      }
      await animateMoneyGrab(result, fast, presentationSignal)
      await animateCreditWinAward(result, visibleBalanceBeforeAward, fast, presentationSignal, crucibleWin)
      await animateEnergyCollection(result, award, state.energyBalance, fast, presentationSignal)
      setBalance(authoritativeBalance)
      setIsAutoSpinCoolingDown(fast)
    } catch (error) {
      setIsAutoSpinning(false)
      if (demoMode) setSpinAvailability('unavailable')
      const insufficient = error instanceof GoldFoundryRequestError && error.code === 'insufficient-slot-credits'
      if (insufficient) setIsReloadPromptOpen(true)
      if (!resultStarted) { try { await settle(before) } catch { setDisplayedReels(before) } } else setDisplayedReels(before)
      if (insufficient && error.available !== undefined) setBalance(error.available)
      else setBalance(current => current + wagerRand)
      setSpinError(error instanceof Error ? error.message : 'The spin could not be completed.')
      setWinningPaylines([]); setLastSpinOutcome(null); setMoneyGrabPresentation(null); setWinAward(null)
    } finally {
      stopLoop(spinCue)
      if (mounted.current) { finishSpinAnimation(currentAnimation); setIsSpinning(false); setIsFastSpinActive(false); setIsStopRequested(false) }
      else cancelSpinAnimation(currentAnimation)
      if (animation.current === currentAnimation) animation.current = null
      stopRequested.current = false; busy.current = false
      if (presentation.current === presentationController) presentation.current = null
    }
  }
  handleSpinRef.current = handleSpin

  const handleSpinButtonClick = useCallback(() => {
    if (blocked) return
    if (busy.current || isSpinning) {
      if (!stopRequested.current) playCue(GOLD_FOUNDRY_SOUNDS.events.stopButtonClick ?? 'button-click')
      stopRequested.current = true; if (animation.current) requestQuickStop(animation.current)
      presentation.current?.abort()
      stopResultCues(); setIsStopRequested(true); setIsAutoSpinning(false); setIsFastSpinActive(true); return
    }
    if (spinAvailability === 'available') void handleSpinRef.current()
  }, [blocked, isSpinning, playCue, spinAvailability, stopResultCues])

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

  const reelStripStyle = (reelIndex: number) => {
    const rows = Math.max(1, displayedReels[reelIndex]?.length ?? 4)
    const moving = ['accelerating', 'spinning', 'braking'].includes(reelMotion[reelIndex])
    const visible = moving && rows > GOLD_FOUNDRY_RULES.reelTravelRowCount ? rows - GOLD_FOUNDRY_RULES.reelTravelRowCount : rows
    return { '--slot-symbol-rows': rows, '--slot-strip-height': `${rows / visible * 100}%`,
      '--slot-spin-travel': `${-Math.max(0, rows - visible) / rows * 100}%` } as CSSProperties
  }
  const pageStyle = {
    '--slot-page-backdrop': `url("${GOLD_FOUNDRY_CABINET.pageBackdropImage}")`,
    '--slot-result-primary': GOLD_FOUNDRY_CABINET.palette.trimBright,
    '--slot-result-secondary': GOLD_FOUNDRY_CABINET.palette.accent,
    '--slot-result-glow': GOLD_FOUNDRY_CABINET.palette.glow,
  } as CSSProperties
  const winningPositions: GridPosition[] = winningPaylines.flatMap(line => line.matches.flatMap(match => match.match.positions))
  return {
    account, activeWagerDisplay, audio, balance, blocked, changeWager, crucibleAward, demoMode,
    displayedReels, energyFlyover, energyMeterRef, handleSpinButtonClick, hasCompletedSpin, helpCloseButtonRef,
    isAutoSpinning, isCruciblePouring, isFastSpinActive, isHelpOpen, isReloadPromptOpen,
    isSettingsOpen, isSpinning, isStopRequested, isSymbolGuideOpen, lastCollectorAward,
    lastSpinOutcome, lastSpinOutcomeKey, lastWin, moneyGrabPresentation,
    pageStyle, reducedMotion, reelMotion, reelStripStyle, reloadPromptCloseButtonRef, resultAtmosphereId,
    selectWager, selectedWagerPoints, setIsAutoSpinning, setIsHelpOpen, setIsReloadPromptOpen,
    setIsSettingsOpen, setIsSymbolGuideOpen, setSpinError, spinAvailability, spinError, state,
    wagerIndex, winAward, winningPositions,
  }
}

export type GoldFoundryController = ReturnType<typeof useGoldFoundry>
export { shouldUseAnimatedSymbol }
