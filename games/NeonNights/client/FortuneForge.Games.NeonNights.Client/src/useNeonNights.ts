import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  cancelSpinAnimation, finishSpinAnimation, getSlotKeyboardAction, reelLandingSettleDurationMs,
  requestQuickStop, SlotStateRevisionGuard, startSpinAnimation, stopSpinAnimationWithCadence,
  usePrefersReducedMotion, useSlotAudio, waitForPresentation, waitForPresentationFrame,
  type PaylinePayout, type ReelMotionState, type SpinAnimation,
} from '@fortuneforge/slot-client-core'
import { createNeonApi, NeonRequestError } from './api.js'
import { getNeonWinReaction, getNeonWinTier, NEON_NIGHTS_SOUNDS, neonBaseMusic, neonCarMusicCueId, neonDiscoMusic, type NeonWinTier } from './audio.js'
import { neonCabinet, neonDefaultWagerIndex, neonRules, neonSymbols } from './manifest.js'
import { discoInitialReels } from './disco.js'
import { presentNitroExpansion, stopNitroReels, type NitroPresentation } from './nitroPresentation.js'
import { emptyNeonState, getNeonCharge, getNeonRows, getNeonWager, isCarPlayMode, neonCarPlayMode, previewRound, stepDiscoMultiplier } from './roundState.js'
import type { NeonCarRespinResult, NeonCascadeStep, NeonDiscoMultiplier, NeonNightsGameProps, NeonNitroChoice, NeonSpinResult, NeonState, NeonSymbolId, NeonTriggerSymbol } from './types.js'

type CascadeFrame = Pick<NeonCascadeStep, 'cleared' | 'dropRows'> & { phase: 'award' | 'clear' | 'drop'; step: number }
type RoundSummary = { totalRand: number; mode: string | null }
type AwardPresentation = Pick<NeonSpinResult, 'payout' | 'wagerPoints' | 'slotsCreditsBalance' | 'neonDiscoCascade'>
type WinAward = {
  id: number
  displayAmount: number
  tier: 'win' | 'big' | 'jackpot'
  winTier: NeonWinTier
  flight: 'balance' | 'spin-total' | null
  left: number
  top: number
  travelX: number
  travelY: number
  endScale: number
  durationMs: number
}
const initialReels = neonRules.initialReels as NeonSymbolId[][]
const toRand = (points: number) => points / 100
const cents = (amount: number) => Math.round(amount * 100) / 100
const baseSpinsBeforeDrums = 4
const carAwardHoldMs = 400
const discoDealExitMs = 200
const discoColumnDropMs = 450
const discoColumnStaggerMs = 180
const discoExitRowStaggerMs = 32
const discoExitColumnStaggerMs = 24

const hasFallingSymbols = (column: readonly number[]) => column.some(rows => rows > 0)

/** Owns Neon gameplay sequencing; the host provides identity, wallet display and transport only. */
export function useNeonNights({ account, demoMode = false, authenticatedFetch, onSpinStateChange }: NeonNightsGameProps) {
  const api = useMemo(() => createNeonApi(authenticatedFetch), [authenticatedFetch])
  const reducedMotion = usePrefersReducedMotion()
  const audio = useSlotAudio(NEON_NIGHTS_SOUNDS)
  const [balance, setBalance] = useState(demoMode ? 10_000 : account?.balances.slotsCredits ?? 0)
  const [state, setState] = useState<NeonState>(emptyNeonState)
  const [displayedReels, setDisplayedReels] = useState<NeonSymbolId[][]>(() => initialReels.map(reel => [...reel]))
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(Array(5).fill('idle'))
  const [isSpinning, setIsSpinning] = useState(false)
  const [isAutoSpinning, setIsAutoSpinning] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [wagerIndex, setWagerIndex] = useState(neonDefaultWagerIndex)
  const [spinAvailability, setSpinAvailability] = useState<'checking' | 'available' | 'unavailable'>('checking')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [completedBaseSpins, setCompletedBaseSpins] = useState(0)
  const [winningPaylines, setWinningPaylines] = useState<PaylinePayout[]>([])
  const [resultKey, setResultKey] = useState(0)
  const [lastWin, setLastWin] = useState(0)
  const [winAward, setWinAward] = useState<WinAward | null>(null)
  const [cascadeFrame, setCascadeFrame] = useState<CascadeFrame | null>(null)
  const [nitroPresentation, setNitroPresentation] = useState<NitroPresentation | null>(null)
  const [isChoosingNitro, setIsChoosingNitro] = useState(false)
  const [nitroAttempt, setNitroAttempt] = useState<{ spinId: string; reelIndex: number } | null>(null)
  const [discoDealPhase, setDiscoDealPhase] = useState<'exit' | 'enter' | null>(null)
  const [discoMultiplier, setDiscoMultiplier] = useState<NeonDiscoMultiplier>(1)
  const [specialIntro, setSpecialIntro] = useState<{ symbol: NeonTriggerSymbol } | null>(null)
  const [arcadeExpanding, setArcadeExpanding] = useState(false)
  const [carCabinetExpanded, setCarCabinetExpanded] = useState(false)
  const [specialStarted, setSpecialStarted] = useState(false)
  const [specialSpinInProgress, setSpecialSpinInProgress] = useState<'car' | 'disco' | null>(null)
  const [roundSummary, setRoundSummary] = useState<RoundSummary | null>(null)
  const [skippedRespinSpinId, setSkippedRespinSpinId] = useState<string | null>(null)
  const [isRespinning, setIsRespinning] = useState(false)
  const [respinDismissPending, setRespinDismissPending] = useState(false)
  const [respinAttempt, setRespinAttempt] = useState<{ spinId: string; reelIndex: number } | null>(null)
  const [roundWinnings, setRoundWinnings] = useState(0)
  const [isAdminTriggering, setIsAdminTriggering] = useState(false)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isSymbolGuideOpen, setIsSymbolGuideOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const helpCloseButtonRef = useRef<HTMLButtonElement>(null)
  const reloadPromptCloseButtonRef = useRef<HTMLButtonElement>(null)
  const mounted = useRef(true)
  const busy = useRef(false)
  const stopRequested = useRef(false)
  const animation = useRef<SpinAnimation<NeonSymbolId> | null>(null)
  const presentation = useRef<AbortController | null>(null)
  const expansionTimer = useRef<number | null>(null)
  const firstSpecialSpin = useRef(false)
  const handleSpinRef = useRef<() => Promise<void>>(async () => {})
  const stateRevision = useRef(new SlotStateRevisionGuard())
  const selectedWagerPoints = neonRules.wagerOptions[wagerIndex]
  const nitroChoice = state.pendingNeonNitroChoice ?? null
  const nitroChoiceReels = nitroChoice?.eligibleReels.filter(reel => nitroAttempt?.spinId !== nitroChoice.spinId || reel === nitroAttempt.reelIndex) ?? []
  const activeWagerPoints = nitroChoice?.wagerPoints ?? getNeonWager(state, selectedWagerPoints)
  const activeWager = toRand(activeWagerPoints)
  // The seven-row cabinet and its music belong to the round through Nitro,
  // respins, and the final tally, even when the remaining-spin count hits zero.
  const specialActive = carCabinetExpanded || specialStarted &&
    (state.freeSpinsRemaining > 0 || specialSpinInProgress === 'car' || roundSummary !== null)
  const discoActive = roundSummary ? roundSummary.mode === 'neon-disco'
    : !specialActive && nitroChoice === null && state.freeSpinsRemaining === 0 && (state.neonDiscoSpinsRemaining > 0 || specialSpinInProgress === 'disco')
  const blocked = isHelpOpen || isSettingsOpen || isSymbolGuideOpen || isReloadPromptOpen
  const respinOffer = demoMode ? null : state.neonCarRespinOffer ?? null
  const retryRespin = respinAttempt !== null && respinOffer?.spinId === respinAttempt.spinId
  const respinReels = respinOffer?.eligibleReels.filter(reel => !retryRespin || reel === respinAttempt.reelIndex) ?? []
  const canChooseRespin = respinReels.length > 0 && !isSpinning && !respinDismissPending && !blocked && nitroChoice === null
  const respinSelecting = canChooseRespin && respinOffer?.spinId !== skippedRespinSpinId
  const carSummaryReady = isCarPlayMode(roundSummary?.mode ?? null) && !isSpinning && !nitroChoice &&
    (respinReels.length === 0 || respinOffer?.spinId === skippedRespinSpinId)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      presentation.current?.abort()
      if (animation.current) cancelSpinAnimation(animation.current)
      if (expansionTimer.current !== null) window.clearTimeout(expansionTimer.current)
    }
  }, [])
  useEffect(() => { onSpinStateChange?.(isSpinning); return () => onSpinStateChange?.(false) }, [isSpinning, onSpinStateChange])

  const restoreState = useCallback((loaded: NeonState) => {
    setState(loaded)
    setDiscoMultiplier(loaded.neonDiscoMultiplier)
    const pendingChoice = loaded.pendingNeonNitroChoice
    const expanded = Boolean(pendingChoice || loaded.neonCarRespinOffer) || getNeonRows(loaded) === 7
    if (expanded) setCarCabinetExpanded(true)
    const roundWinPoints = loaded.neonCarRoundWinPoints ?? loaded.neonCarRespinOffer?.roundWinPoints ??
      (pendingChoice?.sourceKind === 'car-respin' ? pendingChoice.roundWinPoints : undefined)
    const restoredWinnings = toRand((expanded ? roundWinPoints : loaded.neonDiscoRoundWinPoints) ?? 0)
    setRoundWinnings(restoredWinnings)
    if (pendingChoice) {
      setDisplayedReels(pendingChoice.initialReels)
      setSpecialStarted(true)
      setRoundSummary(null)
    } else if (loaded.neonCarRespinOffer) {
      const offer = loaded.neonCarRespinOffer
      setDisplayedReels(offer.reels)
      setSpecialStarted(true)
      if (loaded.freeSpinsRemaining === 0) setRoundSummary({ totalRand: restoredWinnings, mode: neonCarPlayMode })
    } else if (loaded.freeSpinsRemaining > 0) {
      setSpecialStarted(true)
      if (expanded) setDisplayedReels(current => expandArcade(current))
    } else if (loaded.neonDiscoSpinsRemaining > 0) setDisplayedReels(discoInitialReels.map(reel => [...reel]))
  }, [])

  useEffect(() => {
    const abort = new AbortController()
    const revision = stateRevision.current.capture()
    setSpinAvailability('checking')
    const load = demoMode ? api.availability(abort.signal) : api.state().then(loaded => {
      if (!abort.signal.aborted && stateRevision.current.isCurrent(revision)) restoreState(loaded)
    })
    void load.then(() => { if (!abort.signal.aborted) setSpinAvailability('available') }, error => {
      if (!abort.signal.aborted) { setSpinAvailability('unavailable'); setSpinError(error instanceof Error ? error.message : 'Neon Nights could not connect.') }
    })
    return () => abort.abort()
  }, [api, demoMode, restoreState])

  const { preloadLoop, startLoop, startSyncedLoop, stopLoop, getLoopPlaybackSeconds, playCue, stopResultCues } = audio
  useEffect(() => {
    if (carSummaryReady) playCue('bonus-complete')
  }, [carSummaryReady, playCue])
  const baseDrumsUnlocked = completedBaseSpins >= baseSpinsBeforeDrums
  useEffect(() => {
    if (completedBaseSpins === baseSpinsBeforeDrums - 1) preloadLoop(neonBaseMusic.drumCueId)
  }, [completedBaseSpins, preloadLoop])
  const discoIntroMusicStarted = useRef(false)
  useEffect(() => {
    if (specialIntro?.symbol === 'POWER') preloadLoop(neonDiscoMusic.cueId)
    else {
      discoIntroMusicStarted.current = false
      if (specialIntro?.symbol === '6') preloadLoop(neonCarMusicCueId)
    }
  }, [preloadLoop, specialIntro])

  const startDiscoIntroMusic = useCallback((fadeInMs: number) => {
    if (document.hidden) return
    discoIntroMusicStarted.current = true
    startLoop(neonDiscoMusic.cueId, fadeInMs)
  }, [startLoop])

  const waitingForCarIntro = state.freeSpinsRemaining > 0 && !specialStarted && !arcadeExpanding
  const introSymbol = specialIntro?.symbol
  useEffect(() => {
    const startMusic = () => {
      const introIsPlaying = introSymbol !== undefined && (introSymbol !== 'POWER' || !discoIntroMusicStarted.current)
      const activeMusic = introIsPlaying || waitingForCarIntro ? null
        : discoActive ? neonDiscoMusic.cueId : specialActive ? neonCarMusicCueId : neonBaseMusic.cueId
      const playDrums = activeMusic === neonBaseMusic.cueId && baseDrumsUnlocked
      if (!playDrums) stopLoop(neonBaseMusic.drumCueId)
      for (const cueId of [neonBaseMusic.cueId, neonCarMusicCueId, neonDiscoMusic.cueId]) {
        if (cueId !== activeMusic) stopLoop(cueId)
      }
      if (!activeMusic || document.hidden) return
      startLoop(activeMusic)
      if (playDrums) startSyncedLoop(neonBaseMusic.drumCueId, neonBaseMusic.cueId, neonBaseMusic.barSeconds)
    }
    startMusic()
    const onGesture = (event: Event) => { if (event.isTrusted) startMusic() }
    document.addEventListener('pointerdown', onGesture)
    document.addEventListener('keydown', onGesture)
    document.addEventListener('visibilitychange', startMusic)
    window.addEventListener('focus', startMusic)
    window.addEventListener('pageshow', startMusic)
    return () => {
      document.removeEventListener('pointerdown', onGesture)
      document.removeEventListener('keydown', onGesture)
      document.removeEventListener('visibilitychange', startMusic)
      window.removeEventListener('focus', startMusic)
      window.removeEventListener('pageshow', startMusic)
    }
  }, [waitingForCarIntro, introSymbol, discoActive, specialActive,
    baseDrumsUnlocked, startLoop, startSyncedLoop, stopLoop, audio.preferences.mode])
  const getDiscoMusicTime = useCallback(() => getLoopPlaybackSeconds(neonDiscoMusic.cueId), [getLoopPlaybackSeconds])
  useEffect(() => {
    const click = (event: MouseEvent) => {
      if (!event.isTrusted || !(event.target instanceof Element)) return
      const button = event.target.closest('button')
      if (!button || button.disabled || button.closest('[inert]') || button.matches('.spin-button, .pirates-spin-controls__bet-step')) return
      if (button.closest('.slots-page, [data-slot-dialog-theme]') || button.closest('.neon-round-summary')) playCue('button-click')
    }
    document.addEventListener('click', click, true)
    return () => document.removeEventListener('click', click, true)
  }, [playCue])

  function clearResult() {
    setHasCompletedSpin(false); setWinningPaylines([]); setCascadeFrame(null); setLastWin(0); setWinAward(null)
  }
  const finishSpecialIntro = useCallback((symbol: NeonTriggerSymbol) => {
    setSpecialIntro(null)
    if (symbol === 'POWER') {
      setDisplayedReels(discoInitialReels.map(reel => [...reel]))
      return
    }
    if (symbol === '6') {
      setArcadeExpanding(true)
      setCarCabinetExpanded(true)
      setDisplayedReels(current => expandArcade(current))
      if (!document.hidden) startLoop(neonCarMusicCueId)
      playCue('arcade-expansion')
      expansionTimer.current = window.setTimeout(() => {
        expansionTimer.current = null; firstSpecialSpin.current = true; setArcadeExpanding(false); setSpecialStarted(true)
      }, 3500)
    } else { firstSpecialSpin.current = true; setSpecialStarted(true) }
  }, [playCue, startLoop])
  async function dismissRoundSummary() {
    if (busy.current || nitroChoice) return
    if (respinOffer) {
      busy.current = true
      setRespinDismissPending(true)
      setSpinError(null)
      try { await api.dismissRespin(respinOffer.spinId) }
      catch (error) {
        if (mounted.current) setSpinError(error instanceof Error ? error.message : 'Could not close Car Plays.')
        return
      } finally {
        busy.current = false
        if (mounted.current) setRespinDismissPending(false)
      }
      if (!mounted.current) return
    }
    const queuedDisco = state.neonDiscoSpinsRemaining > 0
    setRoundSummary(null); setSpecialStarted(false)
    setRoundWinnings(queuedDisco ? toRand(state.neonDiscoRoundWinPoints ?? 0) : 0)
    setCarCabinetExpanded(false)
    setState(current => ({
      ...current, neonCarRespinOffer: null, neonCarRoundWinPoints: null,
      neonDiscoRoundWinPoints: queuedDisco ? current.neonDiscoRoundWinPoints : null,
    }))
    setSkippedRespinSpinId(null); setRespinAttempt(null)
    setDiscoMultiplier(state.neonDiscoMultiplier)
    if (queuedDisco) setDisplayedReels(discoInitialReels.map(reel => [...reel]))
    else setDisplayedReels(current => current.map((reel, column) => reel.slice(0, 4).map((symbol, row) =>
      symbol !== 'NITRO' && neonSymbols.definitions[symbol] ? symbol : initialReels[column][row])))
    clearResult()
  }
  function selectWager(nextIndex: number) {
    if (busy.current || respinSelecting || nitroChoice) return
    if (state.freeSpinsRemaining > 0) {
      setSpinError(isCarPlayMode(state.freeSpinFeatureMode) ? 'Car Plays are locked to the triggering wager.' : 'Free spins are locked to the wager that won them.'); return
    }
    const index = Math.min(neonRules.wagerOptions.length - 1, Math.max(0, Math.round(nextIndex)))
    setIsAutoSpinning(false); setSpinError(null)
    if (index !== wagerIndex) playCue('bet-step')
    setWagerIndex(index)
  }
  function handleSpinButtonClick() {
    if (specialIntro || roundSummary || arcadeExpanding || blocked || isRespinning || nitroChoice || isChoosingNitro) return
    if (busy.current) {
      if (discoActive) return
      stopRequested.current = true; setIsStopRequested(true); setIsAutoSpinning(false)
      if (animation.current) requestQuickStop(animation.current)
      presentation.current?.abort(); setNitroPresentation(null); stopResultCues(); return
    }
    void handleSpinRef.current()
  }

  async function animateAward(result: AwardPresentation, beforeAward: number, fast: boolean, signal: AbortSignal, carPlay = false) {
    const amount = toRand(result.payout.totalPoints)
    const finalBalance = result.slotsCreditsBalance ?? cents(beforeAward + amount)
    const settle = () => { if (mounted.current) { setBalance(finalBalance); setWinAward(null) } }
    if (amount <= 0 || reducedMotion || signal.aborted) { settle(); return }
    const wager = toRand(result.wagerPoints)
    const winTier = getNeonWinTier(amount, wager)
    const tier = winTier === 'big' ? 'jackpot' : winTier === 'great' && amount >= 25 ? 'big' : 'win'
    const speed = fast ? 1.35 : 1
    const hold = (tier === 'jackpot' ? 2400 : tier === 'big' ? 1500 : 820) / speed
    if (!await waitForPresentation(fast ? 40 : 110, signal) || !await waitForPresentationFrame(signal)) { settle(); return }
    const frame = document.querySelector('.slot-game-frame')?.getBoundingClientRect()
    const left = frame ? frame.left + frame.width / 2 : innerWidth / 2
    const top = frame ? frame.top + frame.height * .38 : innerHeight * .38
    const id = Date.now()
    setWinAward({ id, displayAmount: 0, tier, winTier, flight: null, left, top, travelX: 0, travelY: 0, endScale: 1, durationMs: hold })
    const countDuration = tier === 'win' ? hold * .72 : hold
    if (!await animateNumber(0, amount, countDuration, signal, displayAmount => {
      if (mounted.current) setWinAward(current => current?.id === id ? { ...current, displayAmount } : current)
    }) || !await waitForPresentation(hold - countDuration + (carPlay ? carAwardHoldMs : 0), signal)) { settle(); return }
    const flyDuration = 540 / speed
    const totalSlot = carPlay || result.neonDiscoCascade
      ? document.querySelector('.neon-round-subtotal')
      : result.payout.paylines.length > 1 ? document.querySelector('.neon-spin-total') : null
    const target = (totalSlot ?? document.querySelector('.in-game-shell > [data-game-navbar] .site-header-balance'))?.getBoundingClientRect()
    const amountElement = totalSlot ? document.querySelector('.slots-page__win-award--neon .neon-win-amount') : null
    const amountRect = amountElement?.getBoundingClientRect()
    const flightLeft = amountRect ? amountRect.left + amountRect.width / 2 : left
    const flightTop = amountRect ? amountRect.top + amountRect.height / 2 : top
    const endScale = totalSlot && amountElement
      ? parseFloat(getComputedStyle(totalSlot).fontSize) / parseFloat(getComputedStyle(amountElement).fontSize)
      : 0.28
    setWinAward(current => current ? {
      ...current, id: id + 1, displayAmount: amount, flight: totalSlot ? 'spin-total' : 'balance',
      left: flightLeft, top: flightTop, durationMs: flyDuration, endScale,
      travelX: target ? target.left + target.width / 2 - flightLeft : 0,
      travelY: target ? target.top + target.height / 2 - flightTop : 0,
    } : current)
    await Promise.all([waitForPresentation(flyDuration, signal), (async () => {
      if (await waitForPresentation(flyDuration * .28, signal)) await animateNumber(beforeAward, finalBalance, (tier === 'win' ? 420 : 720) / speed, signal, next => { if (mounted.current) setBalance(next) })
    })()])
    settle()
  }

  async function playDiscoDropLandings(columnCount: number, signal: AbortSignal) {
    if (!await waitForPresentationFrame(signal)) return false
    const landingTimes = reducedMotion
      ? [0]
      : Array.from({ length: columnCount }, (_, column) => discoColumnDropMs + column * discoColumnStaggerMs)
    const landings = await Promise.all(landingTimes.map(async delay => {
      const landed = await waitForPresentation(delay, signal)
      if (landed && mounted.current && columnCount > 0) playCue('disco-column-land')
      return landed
    }))
    return landings.every(Boolean)
  }

  function cancelRespinSelection() {
    if (busy.current) return
    setSkippedRespinSpinId(respinOffer?.spinId ?? null)
  }

  async function revealNitroExpansion(nitro: NonNullable<NeonSpinResult['neonNitroWild']>, reels: NeonSymbolId[][], signal: AbortSignal) {
    if (reducedMotion) return
    await presentNitroExpansion(nitro, signal,
      next => { if (mounted.current) setNitroPresentation(next) },
      index => {
        if (mounted.current) setDisplayedReels(current => current.map((reel, column) => column === index ? reels[index] : reel))
      },
      (index, rowIndex) => {
        if (mounted.current) setDisplayedReels(current => current.map((reel, column) => column === index
          ? reel.map((symbol, row) => row === rowIndex ? reels[index][row] : symbol) : reel))
      })
  }

  async function showCarRespinResult(result: NeonCarRespinResult, roundWinPoints: number, wagerPoints: number, signal: AbortSignal) {
    const improvement = toRand(result.improvementPoints)
    const nextRoundWinPoints = result.roundWinPoints ?? roundWinPoints + result.improvementPoints
    setState(current => ({ ...current, pendingNeonNitroChoice: null, neonCarRespinOffer: null, neonCarRoundWinPoints: nextRoundWinPoints }))
    setRespinAttempt(null)
    setDisplayedReels(result.reels); setWinningPaylines(result.payout.paylines)
    setResultKey(current => current + 1); setHasCompletedSpin(true); setLastWin(improvement)
    playCue(getNeonWinReaction(improvement, toRand(wagerPoints)))
    await animateAward({ wagerPoints, payout: { totalPoints: result.improvementPoints, paylines: [] },
      slotsCreditsBalance: result.slotsCreditsBalance }, balance, false, signal, true)
    if (!mounted.current) return
    const totalRand = toRand(nextRoundWinPoints)
    setRoundWinnings(totalRand)
    setRoundSummary(current => current ? { ...current, totalRand } : current)
  }

  function enterNitroChoice(choice: NeonNitroChoice) {
    setIsAutoSpinning(false)
    setSkippedRespinSpinId(null)
    setSpecialStarted(true)
    setCarCabinetExpanded(true)
    setRoundSummary(null)
    setNitroAttempt(null)
    setDisplayedReels(choice.initialReels)
    setState(current => ({ ...current, freeSpinsRemaining: choice.freeSpinsRemaining,
      freeSpinFeatureMode: neonCarPlayMode, freeSpinWagerPoints: choice.wagerPoints,
      neonCarRespinOffer: null, pendingNeonNitroChoice: choice }))
  }

  async function chooseNitroReel(reelIndex: number) {
    if (busy.current || blocked || !nitroChoice || !nitroChoiceReels.includes(reelIndex)) return
    const abort = new AbortController()
    busy.current = true; stateRevision.current.advance(); presentation.current = abort
    setNitroAttempt({ spinId: nitroChoice.spinId, reelIndex }); setIsChoosingNitro(true); setIsSpinning(true); setSpinError(null)
    try {
      const result = await api.chooseNitro(nitroChoice, reelIndex)
      if (!mounted.current) return
      if (result.neonNitroWild) await revealNitroExpansion(result.neonNitroWild, result.reels, abort.signal)
      if (!mounted.current) return
      if ('sourceSpinId' in result && nitroChoice.sourceKind === 'car-respin') {
        await showCarRespinResult(result, nitroChoice.roundWinPoints, nitroChoice.wagerPoints, abort.signal)
        if (mounted.current && nitroChoice.freeSpinsRemaining === 0)
          setRoundSummary({ totalRand: toRand(result.roundWinPoints ?? nitroChoice.roundWinPoints + result.improvementPoints), mode: neonCarPlayMode })
      } else if ('spinId' in result) {
        const award = toRand(result.payout.totalPoints)
        const roundWinPoints = result.neonCarRoundWinPoints ?? result.neonCarRespinOffer?.roundWinPoints
        const totalRand = roundWinPoints == null ? cents(roundWinnings + award) : toRand(roundWinPoints)
        setState({ freeSpinsRemaining: result.freeSpinsRemaining, freeSpinWagerPoints: result.freeSpinWagerPoints,
          freeSpinFeatureMode: result.freeSpinFeatureMode, neonDiscoSpinsRemaining: result.neonDiscoSpinsRemaining,
          neonDiscoMultiplier: result.neonDiscoMultiplier, neonDiscoRoundWinPoints: result.neonDiscoRoundWinPoints,
          neonCarRoundWinPoints: result.neonCarRoundWinPoints,
          neonCarRespinOffer: result.neonCarRespinOffer, pendingNeonNitroChoice: null })
        setDisplayedReels(result.reels); setWinningPaylines(result.payout.paylines)
        setResultKey(current => current + 1); setHasCompletedSpin(true); setLastWin(award)
        playCue(getNeonWinReaction(award, toRand(result.wagerPoints)))
        const afterWager = result.slotsCreditsBalance == null ? cents(balance - toRand(result.chargedWagerCents ?? result.wagerPoints))
          : cents(result.slotsCreditsBalance - award)
        setBalance(afterWager)
        await animateAward(result, afterWager, false, abort.signal, true)
        if (mounted.current) {
          setRoundWinnings(totalRand)
          if (result.freeSpinsRemaining === 0) setRoundSummary({ totalRand, mode: neonCarPlayMode })
        }
      }
      if (mounted.current) setNitroAttempt(null)
    } catch (error) {
      if (!mounted.current) return
      setDisplayedReels(nitroChoice.initialReels)
      setSpinError(error instanceof Error ? error.message : 'Could not finish Nitro Wild. Choose the same reel to retry.')
    } finally {
      presentation.current = null; busy.current = false
      if (mounted.current) { setIsChoosingNitro(false); setIsSpinning(false); setNitroPresentation(null) }
    }
  }

  async function chooseRespinReel(reelIndex: number) {
    if (busy.current || !respinOffer || !respinSelecting || !canChooseRespin || !respinReels.includes(reelIndex)) return
    const { spinId, reels: originalReels, roundWinPoints } = respinOffer
    const abort = new AbortController()
    const setColumn = (symbols: readonly NeonSymbolId[]) => {
      if (mounted.current) setDisplayedReels(current => current.map((reel, index) => index === reelIndex ? [...symbols] : reel))
    }
    busy.current = true; stateRevision.current.advance(); presentation.current = abort
    setRespinAttempt({ spinId, reelIndex }); setIsRespinning(true); setIsSpinning(true)
    setSpinError(null); clearResult(); stopResultCues()
    startLoop('special-reel-spin')
    const singleReel = startSpinAnimation<NeonSymbolId>({
      reelCount: 1, rowsPerReel: originalReels[reelIndex].length, reducedMotion, travelRows: 8,
      symbolIds: neonSymbols.animationSymbolIds as NeonSymbolId[],
      displayFrame: (_, symbols) => setColumn(symbols),
      setReelMotion: (_, motion) => { if (mounted.current) setReelMotion(current => current.map((value, index) => index === reelIndex ? motion : value)) },
    })
    animation.current = singleReel
    try {
      const result = await api.respin(respinOffer, reelIndex)
      if (!mounted.current) return
      const pending = 'pendingNeonNitroChoice' in result ? result.pendingNeonNitroChoice : null
      const revealed = pending ? pending.initialReels[reelIndex]
        : 'reels' in result ? (result.neonNitroWild && !reducedMotion ? result.initialReels : result.reels)[reelIndex] : originalReels[reelIndex]
      await stopSpinAnimationWithCadence({ animation: singleReel, targetReels: [revealed],
        isQuickStopRequested: () => false, shouldSnapToTarget: () => !mounted.current,
        snapToTarget: () => setColumn(revealed), settleAfterLastReelMs: reelLandingSettleDurationMs,
        onReelStopped: () => { stopLoop('special-reel-spin'); playCue('special-reel-stop') },
      })
      stopLoop('special-reel-spin')
      if (!mounted.current) return
      if (pending) { enterNitroChoice(pending); return }
      if ('reels' in result) {
        if (result.neonNitroWild) await revealNitroExpansion(result.neonNitroWild, result.reels, abort.signal)
        if (mounted.current) await showCarRespinResult(result, roundWinPoints, activeWagerPoints, abort.signal)
      }
    } catch (error) {
      if (!mounted.current) return
      setDisplayedReels(originalReels)
      setSkippedRespinSpinId(null)
      setSpinError(error instanceof Error ? error.message : 'The free respin could not be completed. Choose the same reel to retry.')
      if (!(error instanceof NeonRequestError) || error.status >= 500 || error.code === 'nitro-choice-required') {
        const recovered = await api.state().catch(() => null)
        if (mounted.current && recovered?.pendingNeonNitroChoice) { restoreState(recovered); setSpinError(null) }
      }
    } finally {
      stopLoop('special-reel-spin'); finishSpinAnimation(singleReel)
      animation.current = null; presentation.current = null; busy.current = false
      if (mounted.current) { setIsRespinning(false); setIsSpinning(false); setNitroPresentation(null) }
    }
  }

  async function handleSpin() {
    if (busy.current || specialIntro || roundSummary || arcadeExpanding || blocked || nitroChoice || spinAvailability !== 'available') return
    const before = state
    const free = before.freeSpinsRemaining > 0
    const disco = !free && before.neonDiscoSpinsRemaining > 0
    const carPlay = free && isCarPlayMode(before.freeSpinFeatureMode)
    const wager = getNeonWager(before, selectedWagerPoints)
    const charge = toRand(getNeonCharge(before, selectedWagerPoints))
    if (balance < charge) { setIsAutoSpinning(false); setIsReloadPromptOpen(true); return }
    const balanceBefore = balance
    const previousReels = displayedReels.map(reel => [...reel])
    const fast = isAutoSpinning
    busy.current = true; stopRequested.current = false; stateRevision.current.advance()
    const abort = new AbortController(); presentation.current = abort
    setIsSpinning(true); setIsStopRequested(false); setSpinError(null); setSkippedRespinSpinId(null); clearResult(); stopResultCues()
    if (!carPlay) setBalance(cents(balanceBefore - charge))
    if (free) {
      setSpecialStarted(true)
      setSpecialSpinInProgress('car')
      setState(current => ({ ...current, neonCarRespinOffer: null, freeSpinsRemaining: Math.max(0, current.freeSpinsRemaining - 1) }))
    } else if (disco) setSpecialSpinInProgress('disco')
    const rows = getNeonRows(before)
    const rollCue = free ? 'special-reel-spin' : 'reel-spin'
    const landCue = free ? 'special-reel-stop' : 'reel-stop'
    let reelsLanded = 0
    const onReelStopped = () => {
      reelsLanded += 1
      if (reelsLanded === previousReels.length) stopLoop(rollCue)
      playCue(landCue)
    }
    const dealExitStaggerMs = (rows - 1) * discoExitRowStaggerMs + (previousReels.length - 1) * discoExitColumnStaggerMs
    if (disco) {
      setReelMotion(previousReels.map(() => 'idle'))
      if (!reducedMotion) setDiscoDealPhase('exit')
    } else {
      if (!fast && !free) playCue('lever-pull')
      startLoop(rollCue)
      animation.current = startSpinAnimation<NeonSymbolId>({
        reelCount: 5, rowsPerReel: rows, reducedMotion, travelRows: 8, speedMultiplier: fast ? 3 : 1,
        symbolIds: neonSymbols.animationSymbolIds as NeonSymbolId[],
        displayFrame: (reel, symbols) => { if (mounted.current) setDisplayedReels(current => current.map((column, index) => index === reel ? [...symbols] : column)) },
        setReelMotion: (reel, motion) => { if (mounted.current) setReelMotion(current => current.map((value, index) => index === reel ? motion : value)) },
      })
    }
    let pendingSummary: RoundSummary | null = null
    let settledState: NeonState | null = null
    try {
      // Clear the board while awaiting the authoritative result, rather than
      // cycling a synthetic reel strip during Disco plays.
      const [result] = await Promise.all([
        api.spin({ wagerPoints: wager, useFreeSpin: free, state: before }, demoMode),
        waitForPresentation(disco && !reducedMotion ? discoDealExitMs + dealExitStaggerMs : 0, abort.signal),
      ])
      if (!mounted.current) return
      if (!('reels' in result)) {
        const choice = result.pendingNeonNitroChoice
        if (!carPlay || choice.wagerPoints !== wager) throw new Error('The Nitro choice did not match the active Car Play.')
        if (!reducedMotion && !stopRequested.current) {
          await stopNitroReels({ animation: animation.current!, nitro: choice.neonNitroWild, settledReels: choice.initialReels,
            signal: abort.signal, isQuickStopRequested: () => stopRequested.current,
            settleAfterLastReelMs: reelLandingSettleDurationMs / (fast ? 3 : 1),
            onReelStopped, onAnticipation: () => playCue('nitro-spool'),
            onPresentationChange: next => { if (mounted.current) setNitroPresentation(next) },
          })
        }
        if (mounted.current) enterNitroChoice(choice)
        return
      }
      if (result.reels[0].length !== rows || result.isFreeSpin !== free || (disco && !result.neonDiscoCascade)) throw new Error('The Neon Nights result did not match the active play state.')
      if (carPlay) setBalance(cents(balanceBefore - charge))
      settledState = {
        freeSpinsRemaining: result.freeSpinsRemaining,
        freeSpinWagerPoints: result.freeSpinWagerPoints,
        freeSpinFeatureMode: result.freeSpinFeatureMode,
        neonDiscoSpinsRemaining: result.neonDiscoSpinsRemaining,
        neonDiscoMultiplier: result.neonDiscoMultiplier,
        neonDiscoRoundWinPoints: demoMode ? null : result.neonDiscoRoundWinPoints,
        neonCarRespinOffer: demoMode ? null : result.neonCarRespinOffer,
        neonCarRoundWinPoints: demoMode ? null : result.neonCarRoundWinPoints,
      }
      setRespinAttempt(null)
      // Keep the base soundtrack through the triggering spin's result.
      // Publish a newly awarded round together with its intro in the final update.
      if (free || disco) setState(settledState)
      if (disco) {
        setDisplayedReels(result.reels)
        if (!reducedMotion && !abort.signal.aborted) setDiscoDealPhase('enter')
        await playDiscoDropLandings(result.reels.length, abort.signal)
        setDiscoDealPhase(null)
      } else if (result.neonNitroWild && !reducedMotion && !stopRequested.current) {
        await stopNitroReels({ animation: animation.current!, nitro: result.neonNitroWild, settledReels: result.reels,
          signal: abort.signal, isQuickStopRequested: () => stopRequested.current,
          settleAfterLastReelMs: reelLandingSettleDurationMs / (fast ? 3 : 1),
          onReelStopped,
          onPresentationChange: next => { if (mounted.current) setNitroPresentation(next) },
          onAnticipation: () => playCue('nitro-spool'),
        })
      } else {
        await stopSpinAnimationWithCadence({ animation: animation.current!, isQuickStopRequested: () => stopRequested.current,
          quickBrakeDurationMs: 90, quickSettleAfterStopMs: 35, settleAfterLastReelMs: reelLandingSettleDurationMs / (fast ? 3 : 1),
          shouldSnapToTarget: () => !mounted.current, snapToTarget: () => setDisplayedReels(result.reels), targetReels: result.reels,
          onReelStopped })
      }
      if (!mounted.current) return
      if (!disco) stopLoop(rollCue)
      setDisplayedReels(result.reels)
      if (result.neonDiscoCascade) {
        let cumulative = 0
        for (let index = 0; index < result.neonDiscoCascade.steps.length; index++) {
          const step = result.neonDiscoCascade.steps[index]
          if (abort.signal.aborted) break
          cumulative += step.payout.totalPoints
          setDiscoMultiplier(step.multiplier)
          setWinningPaylines(step.payout.paylines); setResultKey(current => current + 1); setHasCompletedSpin(true); setLastWin(toRand(cumulative))
          setCascadeFrame({ phase: 'award', cleared: step.cleared, dropRows: step.dropRows, step: index })
          if (!await waitForPresentation(reducedMotion ? 150 : 950, abort.signal)) break
          setCascadeFrame({ phase: 'clear', cleared: step.cleared, dropRows: step.dropRows, step: index })
          if (!await waitForPresentation(reducedMotion ? 100 : 420, abort.signal)) break
          setWinningPaylines([])
          setDiscoMultiplier(stepDiscoMultiplier(step.multiplier, 1))
          setDisplayedReels(step.reels)
          setCascadeFrame({ phase: 'drop', cleared: step.cleared, dropRows: step.dropRows, step: index })
          if (!await playDiscoDropLandings(step.dropRows.filter(hasFallingSymbols).length, abort.signal)) break
          if (reducedMotion && !await waitForPresentation(100, abort.signal)) break
        }
        const finalStep = result.neonDiscoCascade.steps.at(-1)
        if (finalStep) setDisplayedReels(finalStep.reels)
        const endTier = finalStep ? stepDiscoMultiplier(finalStep.multiplier, 1) : stepDiscoMultiplier(before.neonDiscoMultiplier, -1)
        setDiscoMultiplier(result.neonDiscoSpinsRemaining > 0 ? result.neonDiscoMultiplier : endTier)
        setCascadeFrame(null); setWinningPaylines([])
      } else setWinningPaylines(result.payout.paylines)
      setResultKey(current => current + 1); setHasCompletedSpin(true); setLastWin(toRand(result.payout.totalPoints))
      const award = toRand(result.payout.totalPoints)
      let nextRoundWinnings = roundWinnings
      if (free || disco) {
        const roundWinPoints = carPlay
          ? settledState.neonCarRoundWinPoints ?? settledState.neonCarRespinOffer?.roundWinPoints
          : settledState.neonDiscoRoundWinPoints
        nextRoundWinnings = roundWinPoints == null ? cents(roundWinnings + award) : toRand(roundWinPoints)
        if ((free && result.freeSpinsRemaining === 0) || (disco && result.neonDiscoSpinsRemaining === 0)) pendingSummary = { totalRand: nextRoundWinnings, mode: disco ? 'neon-disco' : before.freeSpinFeatureMode }
      }
      const newRound = !free && !disco && (result.freeSpinsAwarded > 0 || result.neonDiscoSpinsRemaining > 0)
      if (newRound) setIsAutoSpinning(false)
      if (pendingSummary && !carPlay) playCue('bonus-complete')
      else if (newRound) playCue('bonus-entry')
      else playCue(getNeonWinReaction(award, toRand(result.wagerPoints), !free && !disco))
      await animateAward(result, cents(balanceBefore - charge), fast, abort.signal, carPlay)
      if (mounted.current && (free || disco)) setRoundWinnings(nextRoundWinnings)
      if (newRound) {
        if (reducedMotion && result.payout.paylines.length > 0) await waitForPresentation(700, abort.signal)
        if (!mounted.current) return
        clearResult()
        setRoundWinnings(0)
        setSpecialStarted(false)
        setDiscoMultiplier(settledState.neonDiscoMultiplier)
        const activator = result.neonArcade.activators.find(candidate => candidate.symbolId === 'POWER') ?? result.neonArcade.activators.find(candidate => candidate.symbolId === '6')
        if (activator?.positions.some(cell => result.reels[cell.reel]?.[cell.row] === activator.symbolId))
          setSpecialIntro({ symbol: activator.symbolId })
        else if (result.freeSpinsRemaining > 0) {
          setSpecialStarted(true); firstSpecialSpin.current = true
          if (getNeonRows(settledState) === 7) {
            setCarCabinetExpanded(true)
            setDisplayedReels(current => expandArcade(current))
          }
        }
        else setDisplayedReels(discoInitialReels.map(reel => [...reel]))
      }
      if (!free && !disco && !baseDrumsUnlocked && mounted.current) setCompletedBaseSpins(count => count + 1)
    } catch (error) {
      if (!mounted.current) return
      setIsAutoSpinning(false); setDiscoDealPhase(null); setDisplayedReels(previousReels); setState(before); clearResult()
      setDiscoMultiplier(before.neonDiscoMultiplier)
      setBalance(error instanceof NeonRequestError && error.available !== undefined ? error.available : balanceBefore)
      if (error instanceof NeonRequestError && error.code === 'insufficient_balance') setIsReloadPromptOpen(true)
      if (error instanceof NeonRequestError && (error.status === 503 || error.status === 404)) setSpinAvailability('unavailable')
      setSpinError(error instanceof Error ? error.message : 'Neon Nights could not complete the spin.')
      const recoverNitro = carPlay && (!(error instanceof NeonRequestError) || error.status >= 500) ||
        error instanceof NeonRequestError && error.code === 'nitro-choice-required'
      if (!demoMode && (recoverNitro || error instanceof NeonRequestError && error.code === 'free-spins-required')) {
        const recovered = await api.state().catch(() => null)
        if (mounted.current && recovered) {
          settledState = recovered
          restoreState(recovered)
          if (recovered.pendingNeonNitroChoice || (!recoverNitro && (recovered.freeSpinsRemaining > 0 || recovered.neonCarRespinOffer))) setSpinError(null)
        }
      }
    } finally {
      if (!disco) stopLoop(rollCue)
      if (animation.current) finishSpinAnimation(animation.current)
      animation.current = null; presentation.current = null; busy.current = false; stopRequested.current = false
      if (mounted.current) {
        if (settledState) setState(settledState)
        setIsSpinning(false); setIsStopRequested(false); setSpecialSpinInProgress(null); setCascadeFrame(null); setDiscoDealPhase(null); setNitroPresentation(null)
        if (pendingSummary) setRoundSummary(pendingSummary)
      }
    }
  }
  handleSpinRef.current = handleSpin
  useEffect(() => {
    if (isSpinning || busy.current || blocked || specialIntro || roundSummary || arcadeExpanding || respinSelecting || canChooseRespin || nitroChoice || spinAvailability !== 'available') return
    if (state.freeSpinsRemaining > 0 && specialStarted && !isCarPlayMode(state.freeSpinFeatureMode)) {
      const delay = firstSpecialSpin.current ? 0 : 2400
      firstSpecialSpin.current = false
      const timer = window.setTimeout(() => { void handleSpinRef.current() }, delay)
      return () => window.clearTimeout(timer)
    }
    if (!isAutoSpinning) return
    const timer = window.setTimeout(() => { void handleSpinRef.current() }, 1000)
    return () => window.clearTimeout(timer)
  }, [isSpinning, blocked, specialIntro, roundSummary, arcadeExpanding, respinSelecting, canChooseRespin, nitroChoice, spinAvailability, state.freeSpinsRemaining, state.freeSpinFeatureMode, specialStarted, isAutoSpinning, discoActive])

  async function triggerAdmin(symbol: NeonTriggerSymbol) {
    if ((!demoMode && account?.role.toLowerCase() !== 'admin') || (demoMode && !import.meta.env.DEV) || busy.current || isAdminTriggering || specialIntro || roundSummary || arcadeExpanding || nitroChoice || state.freeSpinsRemaining > 0 || state.neonDiscoSpinsRemaining > 0) return
    setIsAdminTriggering(true); stateRevision.current.advance()
    try {
      const loaded = demoMode ? previewRound(symbol, selectedWagerPoints) : await api.trigger(symbol === '6' ? 'car' : 'disco-ball', selectedWagerPoints)
      if (!mounted.current) return
      setState(loaded); setIsAutoSpinning(false); setSpinError(null); clearResult(); setRoundWinnings(0); setSpecialStarted(false)
      setDiscoMultiplier(loaded.neonDiscoMultiplier)
      setDisplayedReels(current => current.map((reel, index) => Array.from({ length: 4 }, (_, row) => index === 2 && row === 1 ? symbol : reel[row] ?? initialReels[index][row])))
      setSpecialIntro({ symbol })
    } catch (error) { if (mounted.current) setSpinError(error instanceof Error ? error.message : 'Could not start Special Plays.') }
    finally { if (mounted.current) setIsAdminTriggering(false) }
  }
  const keyboardHandler = useRef<(event: KeyboardEvent) => void>(() => {})
  keyboardHandler.current = event => {
    if (event.defaultPrevented) return
    const target = event.target
    const isEditing = target instanceof HTMLElement && (target.isContentEditable ||
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
    const hasControlFocus = target instanceof HTMLElement && ['BUTTON', 'A'].includes(target.tagName)
    const isMuteKey = event.key.toLowerCase() === 'm'
    const action = getSlotKeyboardAction(event.key, { hasOpenDialog: blocked && !isMuteKey,
      isEditableTarget: isEditing || (hasControlFocus && !isMuteKey),
      modified: event.altKey || event.ctrlKey || event.metaKey, repeated: event.repeat || event.isComposing })
    if (action === 'toggle-mute') {
      event.preventDefault()
      audio.toggleMuted()
      return
    }
    if (blocked || specialIntro || arcadeExpanding || isRespinning || nitroChoice || isChoosingNitro) return
    if (respinSelecting) {
      if (event.key === 'Escape') { event.preventDefault(); cancelRespinSelection() }
      if (event.key !== ' ') return
    }
    if (roundSummary) return
    if (!action) return
    event.preventDefault()
    if (action === 'spin-or-stop') handleSpinButtonClick()
    else if (action === 'increase-wager') selectWager(wagerIndex + 1)
    else if (action === 'decrease-wager') selectWager(wagerIndex - 1)
    else if (action === 'open-help') { setIsAutoSpinning(false); setIsHelpOpen(true) }
  }
  useEffect(() => {
    const key = (event: KeyboardEvent) => keyboardHandler.current(event)
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [])

  function reelStripStyle(index: number): CSSProperties {
    const count = Math.max(1, displayedReels[index]?.length ?? 4)
    const moving = ['accelerating', 'spinning', 'braking'].includes(reelMotion[index])
    const visible = moving && count > 8 ? count - 8 : count
    const style = {
      '--slot-symbol-rows': count,
      '--slot-strip-height': `${count / visible * 100}%`,
      '--slot-spin-travel': `${-(count - visible) / count * 100}%`,
    }
    if (cascadeFrame?.phase !== 'drop') return style as CSSProperties
    const earlierDrops = cascadeFrame.dropRows.slice(0, index).filter(hasFallingSymbols).length
    return {
      ...style,
      '--neon-disco-drop-duration': `${discoColumnDropMs}ms`,
      '--neon-disco-column-delay': `${earlierDrops * discoColumnStaggerMs}ms`,
    } as CSSProperties
  }
  function discoDealStyle(reel: number, row: number): CSSProperties {
    const rows = displayedReels[reel]?.length ?? 4
    const exiting = discoDealPhase === 'exit'
    const delay = exiting
      ? (rows - 1 - row) * discoExitRowStaggerMs + reel * discoExitColumnStaggerMs
      : reel * discoColumnStaggerMs
    return {
      '--neon-deal-rows': rows,
      '--neon-deal-row': row,
      '--neon-deal-duration': `${exiting ? discoDealExitMs : discoColumnDropMs}ms`,
      '--neon-deal-delay': `${delay}ms`,
    } as CSSProperties
  }
  const pageStyle = {
    '--slot-page-backdrop': `url("${neonCabinet.pageBackdropImage}")`,
    '--slot-result-primary': neonCabinet.palette.trimBright, '--slot-result-secondary': neonCabinet.palette.accent,
    '--slot-result-glow': neonCabinet.palette.glow,
  } as CSSProperties
  return { account, demoMode, audio, balance, state, activeWager, selectedWagerPoints, wagerIndex, selectWager, displayedReels, reelMotion, reelStripStyle,
    isSpinning, isAutoSpinning, setIsAutoSpinning, isStopRequested, handleSpinButtonClick, spinAvailability, spinError, setSpinError,
    hasCompletedSpin, winningPaylines, resultKey, lastWin, winAward, cascadeFrame, nitroPresentation, discoDealPhase, discoDealStyle, discoMultiplier, specialIntro, finishSpecialIntro, arcadeExpanding, carCabinetExpanded,
    specialActive, discoActive, roundWinnings, roundSummary, dismissRoundSummary, triggerAdmin, isAdminTriggering,
    canChooseRespin, respinReels, respinSelecting, isRespinning, respinDismissPending, retryRespin,
    cancelRespinSelection, chooseRespinReel,
    nitroChoice, nitroChoiceReels, isChoosingNitro, chooseNitroReel,
    isHelpOpen, setIsHelpOpen, isSettingsOpen, setIsSettingsOpen, isSymbolGuideOpen, setIsSymbolGuideOpen, isReloadPromptOpen, setIsReloadPromptOpen,
    helpCloseButtonRef, reloadPromptCloseButtonRef, reducedMotion, getDiscoMusicTime, startDiscoIntroMusic, pageStyle,
  }
}
export type NeonNightsController = ReturnType<typeof useNeonNights>
function expandArcade(reels: NeonSymbolId[][]): NeonSymbolId[][] {
  return reels.map((reel, index) => Array.from({ length: 7 }, (_, row) => reel[row] ?? initialReels[index][(row + 1) % 4]))
}
async function animateNumber(from: number, to: number, duration: number, signal: AbortSignal, onValue: (value: number) => void) {
  if (signal.aborted || duration <= 0) { onValue(to); return !signal.aborted }
  return new Promise<boolean>(resolve => {
    const start = performance.now()
    let frame = 0
    let settled = false
    const finish = (completed: boolean) => {
      if (settled) return
      settled = true; cancelAnimationFrame(frame); signal.removeEventListener('abort', abort); onValue(to); resolve(completed)
    }
    const abort = () => finish(false)
    const step = (now: number) => {
      const progress = Math.min(1, (now - start) / duration)
      onValue(cents(from + (to - from) * (1 - (1 - progress) ** 3)))
      if (progress === 1) finish(true)
      else frame = requestAnimationFrame(step)
    }
    signal.addEventListener('abort', abort, { once: true }); frame = requestAnimationFrame(step)
  })
}
