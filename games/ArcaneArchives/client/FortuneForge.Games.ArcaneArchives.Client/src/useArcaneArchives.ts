import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  cancelSpinAnimation, finishSpinAnimation, getSlotKeyboardAction, requestQuickStop,
  SlotStateRevisionGuard, slotPointsToRand, startSpinAnimation, stopSpinAnimationWithCadence,
  usePrefersReducedMotion, useSlotAudio, waitForPresentation,
  type PaylinePayout, type ReelMotionState, type SlotPotionSoundEvent, type SpinAnimation,
} from '@fortuneforge/slot-client-core'
import { getPaylineWinTimings } from '@fortuneforge/slot-client-core/slotWinPresentation'
import { ArcaneRequestError, createArcaneApi } from './api.js'
import { ARCANE_ARCHIVES_EXPERIENCE_SET } from './manifest.js'
import { ARCANE_ARCHIVES_SOUNDS } from './sounds.js'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols.js'
import { getArcaneWinPresentationTier } from './arcaneWinPresentation.js'
import type { SlotPotionState, SlotTomeState } from './arcaneFeatures.js'
import {
  ARCANE_LIGHTNING_PAYLINE_ID, ARCANE_WAND_PAYLINE_ID,
  type ArcaneArchivesGameProps, type ArcaneRoundSummary, type ArcaneSpinResult, type ArcaneState,
} from './types.js'

const rules = ARCANE_ARCHIVES_EXPERIENCE_SET.rules
const initialReels = () => rules.initialReels.map(reel => [...reel])
const initialState: ArcaneState = {
  freeSpinsRemaining: 0, freeSpinWagerPoints: null, freeSpinFeatureMode: null,
  specialPointsBalance: 0, energyBalance: 0, arcaneTome: null, arcanePotions: null,
}
const defaultWagerIndex = Math.max(0, rules.wagerOptions.findIndex(points => slotPointsToRand(points, rules.pointValueInCents) === (rules.defaultWagerRand ?? 3)))
const message = (error: unknown) => error instanceof Error ? error.message : 'Arcane Archives could not complete the request.'

/** Owns paid Arcane round sequencing, its persisted workbench, input and presentation lifecycle. */
export function useArcaneArchives({ account, authenticatedFetch, onSpinStateChange }: ArcaneArchivesGameProps) {
  const accountId = account?.userId
  const api = useMemo(() => createArcaneApi(authenticatedFetch, accountId), [authenticatedFetch, accountId])
  const reducedMotion = usePrefersReducedMotion()
  const audio = useSlotAudio(ARCANE_ARCHIVES_SOUNDS)
  const audioRef = useRef(audio); audioRef.current = audio
  const [balance, setBalance] = useState(account?.balances.slotsCredits ?? 0)
  const [state, setState] = useState<ArcaneState>(initialState)
  const stateRef = useRef(state); stateRef.current = state
  const [displayedReels, setDisplayedReels] = useState<string[][]>(initialReels)
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(Array(5).fill('idle'))
  const [lastStoppedReel, setLastStoppedReel] = useState<number | null>(null)
  const [isSpinning, setIsSpinning] = useState(false)
  const [isAutoSpinning, setAutoSpinning] = useState(false)
  const [isStopRequested, setIsStopRequested] = useState(false)
  const [wagerIndex, setWagerIndex] = useState(defaultWagerIndex)
  const [pointValueInCents, setPointValueInCents] = useState(rules.pointValueInCents)
  const [spinAvailability, setSpinAvailability] = useState<'checking' | 'available' | 'unavailable'>('checking')
  const [spinError, setSpinError] = useState<string | null>(null)
  const [hasCompletedSpin, setHasCompletedSpin] = useState(false)
  const [winningPaylines, setWinningPaylines] = useState<PaylinePayout[]>([])
  const [resultKey, setResultKey] = useState(0)
  const [lastWin, setLastWin] = useState(0)
  const [lastResult, setLastResult] = useState<ArcaneSpinResult | null>(null)
  const [tomeIngredientsAwarded, setTomeIngredientsAwarded] = useState<Readonly<Record<string, number>>>({})
  const [confirmedPotionUse, setConfirmedPotionUse] = useState<number | null>(null)
  const [isArcaneUpdating, setIsArcaneUpdating] = useState(false)
  const [arcaneError, setArcaneError] = useState<string | null>(null)
  const [roundSummary, setRoundSummary] = useState<ArcaneRoundSummary | null>(null)
  const [isHelpOpen, setIsHelpOpen] = useState(false)
  const [isSettingsOpen, setIsSettingsOpen] = useState(false)
  const [isSymbolValuesOpen, setIsSymbolValuesOpen] = useState(false)
  const [isReloadPromptOpen, setIsReloadPromptOpen] = useState(false)
  const [presentationBusy, setPresentationBusy] = useState(false)
  const mounted = useRef(false)
  const session = useRef(0)
  const revision = useRef(new SlotStateRevisionGuard())
  const busy = useRef(false)
  const commandPending = useRef(false)
  const stopRequested = useRef(false)
  const animation = useRef<SpinAnimation<string> | null>(null)
  const presentation = useRef<AbortController | null>(null)
  const spinRef = useRef<() => Promise<void>>(async () => {})
  const roundWin = useRef(0)
  const accountRef = useRef(accountId); accountRef.current = accountId
  const selectedWagerPoints = rules.wagerOptions[wagerIndex] ?? rules.wagerOptions[0]
  const tomeState = state.arcaneTome
  const potions = state.arcanePotions
  const isTomeActivationRequired = tomeState?.grimoireReady === true
  const lockedPotionWagerPoints = potions?.active ? potions.active.bottle.lockedWagerPoints ?? rules.wagerOptions[0] : null
  const pending = api.pendingSpin()
  const useFreeGameForNextSpin = pending?.useFreeSpin ?? state.freeSpinsRemaining > 0
  const activeWagerPoints = pending?.wagerPoints ?? (useFreeGameForNextSpin
    ? state.freeSpinWagerPoints ?? selectedWagerPoints : lockedPotionWagerPoints ?? selectedWagerPoints)
  const activeWager = slotPointsToRand(activeWagerPoints, pointValueInCents)
  const preferredWager = slotPointsToRand(selectedWagerPoints, pointValueInCents)
  const blocked = isHelpOpen || isSettingsOpen || isSymbolValuesOpen || isReloadPromptOpen || presentationBusy
  const canAffordSelectedWager = Boolean(pending) || useFreeGameForNextSpin || balance >= activeWager
  const isSpecialGameActive = state.freeSpinsRemaining > 0 || (isSpinning && lastResult?.isFreeSpin === true) || roundSummary !== null

  const applyState = useCallback((next: ArcaneState, updateBalance = true) => {
    stateRef.current = next
    setState(next)
    if (updateBalance && next.slotsCreditsBalance != null) setBalance(next.slotsCreditsBalance)
    if (next.arcaneTome?.grimoireReady) setAutoSpinning(false)
    if (next.freeSpinWagerPoints != null && !next.arcanePotions?.active) {
      const index = rules.wagerOptions.indexOf(next.freeSpinWagerPoints)
      if (index >= 0) setWagerIndex(index)
    }
  }, [])
  const applySettledResult = useCallback((result: ArcaneSpinResult, updateBalance = true) => {
    const current = stateRef.current
    const next = { ...result, arcaneTome: result.arcaneTome === undefined ? current.arcaneTome : result.arcaneTome,
      arcanePotions: result.arcanePotions === undefined ? current.arcanePotions : result.arcanePotions }
    applyState(next, updateBalance)
    setPointValueInCents(result.pointValueInCents)
    setDisplayedReels(result.reels.map(reel => [...reel]))
    setLastResult(result)
    setWinningPaylines(result.payout.paylines.filter(line =>
      !(result.arcaneWandCollection && line.paylineId === ARCANE_WAND_PAYLINE_ID) &&
      !(result.lightningCollection && line.paylineId === ARCANE_LIGHTNING_PAYLINE_ID)))
    setTomeIngredientsAwarded(result.arcaneIngredientsAwarded ?? {})
    setLastWin(slotPointsToRand(result.payout.totalPoints, result.pointValueInCents))
    setHasCompletedSpin(true)
    setResultKey(key => key + 1)
  }, [applyState])

  useEffect(() => {
    mounted.current = true
    const generation = ++session.current
    const accountAtStart = accountId
    const abort = new AbortController()
    const stateRevision = revision.current.advance()
    const current = () => mounted.current && session.current === generation && accountRef.current === accountAtStart && !abort.signal.aborted && revision.current.isCurrent(stateRevision)
    busy.current = false; commandPending.current = false; stopRequested.current = false
    setState(initialState); stateRef.current = initialState
    setBalance(account?.balances.slotsCredits ?? 0); setDisplayedReels(initialReels())
    setReelMotion(Array(5).fill('idle')); setIsSpinning(false); setAutoSpinning(false)
    setLastResult(null); setHasCompletedSpin(false); setWinningPaylines([]); setLastWin(0)
    setConfirmedPotionUse(null); setTomeIngredientsAwarded({}); setRoundSummary(null); roundWin.current = 0
    setSpinAvailability('checking'); setSpinError(null); setArcaneError(null)
    void (async () => {
      try {
        let loaded = await api.state(abort.signal)
        if (!current()) return
        applyState(loaded)
        if (api.pendingSpin()) {
          const result = await api.resumeSpin()
          if (!current()) return
          loaded = await api.state(abort.signal)
          if (!current()) return
          if (result) applySettledResult(result)
          applyState(loaded)
        }
        if (current()) setSpinAvailability('available')
      } catch (error) {
        if (current()) { setSpinAvailability('unavailable'); setSpinError(message(error)) }
      }
    })()
    return () => {
      mounted.current = false; session.current += 1; abort.abort(); presentation.current?.abort()
      if (animation.current) cancelSpinAnimation(animation.current)
      audioRef.current.stopLoop(ARCANE_ARCHIVES_SOUNDS.events.reelSpin)
      if (ARCANE_ARCHIVES_SOUNDS.events.specialReelSpin) audioRef.current.stopLoop(ARCANE_ARCHIVES_SOUNDS.events.specialReelSpin)
    }
  }, [api, accountId, applyState, applySettledResult])
  useEffect(() => { onSpinStateChange?.(isSpinning); return () => onSpinStateChange?.(false) }, [isSpinning, onSpinStateChange])
  useEffect(() => { if (blocked || isTomeActivationRequired || spinAvailability !== 'available') setAutoSpinning(false) }, [blocked, isTomeActivationRequired, spinAvailability])
  useEffect(() => {
    const hidden = () => { if (document.hidden) setAutoSpinning(false) }
    document.addEventListener('visibilitychange', hidden)
    return () => document.removeEventListener('visibilitychange', hidden)
  }, [])

  const playPotionCue = useCallback((event: SlotPotionSoundEvent) => {
    const cues = ARCANE_ARCHIVES_SOUNDS.events.potion?.[event]
    if (cues) audioRef.current.playSequence(cues)
  }, [])
  async function updateWorkbench<T>(request: () => Promise<T>, apply: (value: T) => void) {
    if (!tomeState || busy.current || commandPending.current || spinAvailability !== 'available' || api.pendingSpin()) return
    commandPending.current = true; setIsArcaneUpdating(true); setAutoSpinning(false); setArcaneError(null)
    const generation = session.current; const owner = accountId
    revision.current.advance()
    const current = () => mounted.current && session.current === generation && accountRef.current === owner
    try {
      const value = await request()
      if (current()) apply(value)
    } catch (error) {
      const loaded = await api.state().catch(() => null)
      if (current()) { if (loaded) applyState(loaded); setArcaneError(message(error)) }
    } finally {
      if (current()) { commandPending.current = false; setIsArcaneUpdating(false) }
    }
  }
  function changeTomePage(page: string, activate: boolean) {
    return updateWorkbench(() => api.changeTomePage(page, activate), (next: SlotTomeState) =>
      applyState({ ...stateRef.current, arcaneTome: next }))
  }
  function activatePotion(id: number) {
    return updateWorkbench(() => api.activatePotion(id), (next: SlotPotionState) => {
      applyState({ ...stateRef.current, arcanePotions: next })
      setConfirmedPotionUse(next.active?.bottle.id === id ? id : null)
    })
  }

  async function spin() {
    if (busy.current || commandPending.current || blocked || spinAvailability !== 'available' || isTomeActivationRequired) return
    if (!canAffordSelectedWager) { setAutoSpinning(false); setIsReloadPromptOpen(true); return }
    const generation = session.current; const owner = accountId
    const current = () => mounted.current && session.current === generation && accountRef.current === owner
    busy.current = true; stopRequested.current = false; revision.current.advance()
    const abort = new AbortController(); presentation.current = abort
    const fast = isAutoSpinning
    const wasFreeSpin = useFreeGameForNextSpin
    const terms = { wagerPoints: activeWagerPoints, useFreeSpin: wasFreeSpin, useSpecialBoost: false }
    if (!wasFreeSpin && !pending) setBalance(currentBalance => currentBalance - activeWager)
    const oldReels = displayedReels.map(reel => [...reel])
    const events = ARCANE_ARCHIVES_SOUNDS.events
    const spinCue = wasFreeSpin ? events.specialReelSpin ?? events.reelSpin : events.reelSpin
    const stopCue = wasFreeSpin ? events.specialReelStop ?? events.reelStop : events.reelStop
    const speed = fast ? rules.autoSpinSpeedMultiplier : rules.baseSpinSpeedMultiplier
    setIsSpinning(true); setIsStopRequested(false); setSpinError(null); setLastStoppedReel(null)
    setHasCompletedSpin(false); setWinningPaylines([]); setLastResult(null); setTomeIngredientsAwarded({})
    setRoundSummary(null)
    audio.playCue(events.spinButtonClick ?? events.leverPull)
    audio.startLoop(wasFreeSpin ? events.specialAmbience ?? events.ambience : events.ambience)
    audio.startLoop(spinCue)
    const moving = startSpinAnimation({
      reelCount: 5, rowsPerReel: wasFreeSpin && state.freeSpinFeatureMode?.split('-').includes('rows') ? 6 : 4,
      symbolIds: Object.keys(ARCANE_ARCHIVES_SYMBOLS.definitions).filter(id => !tomeState?.activePage || id !== 'ACE'),
      speedMultiplier: speed, travelRows: rules.reelTravelRowCount, reducedMotion,
      displayFrame: (reelIndex, symbols) => { if (current()) setDisplayedReels(reels => reels.map((reel, index) => index === reelIndex ? [...symbols] : reel)) },
      setReelMotion: (reelIndex, motion) => { if (current()) setReelMotion(motions => motions.map((old, index) => index === reelIndex ? motion : old)) },
    })
    animation.current = moving
    let settled: ArcaneSpinResult | undefined
    try {
      settled = await api.spin(terms)
      if (!current()) return
      const result = settled
      await stopSpinAnimationWithCadence({
        animation: moving, isQuickStopRequested: () => stopRequested.current,
        targetReels: result.reels,
        onReelStopped: index => { if (current()) { setLastStoppedReel(index); audio.playCue(stopCue) } },
        shouldSnapToTarget: () => !current() || reducedMotion || stopRequested.current,
        snapToTarget: () => { if (current()) { setDisplayedReels(result.reels.map(reel => [...reel])); setReelMotion(Array(5).fill('stopped')) } },
      })
      if (!current()) return
      audio.stopLoop(spinCue)
      // Workbench and awards are revealed only after the authoritative reels have stopped.
      applySettledResult(result, false)
      const total = slotPointsToRand(result.payout.totalPoints, result.pointValueInCents)
      if (wasFreeSpin) {
        roundWin.current += total
        if (result.freeSpinsRemaining === 0) { setRoundSummary({ totalRand: roundWin.current, mode: state.freeSpinFeatureMode }); setAutoSpinning(false) }
      } else if (result.freeSpinsAwarded > 0) roundWin.current = 0
      const collection = result.arcaneWandCollection ?? result.lightningCollection
      const cue = collection ? events.collectorWin : wasFreeSpin && result.freeSpinsRemaining === 0
        ? events.bonusComplete : result.freeSpinsAwarded > 0 ? events.results.bonus[0]
          : total > 0 ? events.results.premium[0] : events.results['no-win'][0]
      if (cue && !stopRequested.current && !(fast && total === 0 && !(wasFreeSpin && result.freeSpinsRemaining === 0))) audio.playCue(cue)
      if (collection && !reducedMotion) await waitForPresentation(500, abort.signal)
      if (total > 0) {
        const wager = slotPointsToRand(result.wagerPoints, result.pointValueInCents)
        const tier = getArcaneWinPresentationTier(total, wager)
        const timings = getPaylineWinTimings(tier, fast)
        await waitForPresentation(110 + timings.hold + timings.flight, abort.signal)
      }
      if (current()) setBalance(result.slotsCreditsBalance)
      if (api.pendingSpin()) {
        const loaded = await api.state()
        if (current()) applyState(loaded)
      }
    } catch (error) {
      if (!current()) return
      setAutoSpinning(false); setSpinError(message(error))
      if (!settled) { setDisplayedReels(oldReels); setReelMotion(Array(5).fill('idle')) }
      const insufficientFunds = error instanceof ArcaneRequestError &&
        (error.status === 402 || error.code === 'insufficient-slot-credits')
      if (insufficientFunds) {
        setIsReloadPromptOpen(true)
        setSpinError(null)
        if (typeof error.available === 'number' && Number.isFinite(error.available) && error.available >= 0) {
          setBalance(error.available)
          return
        }
      }
      // Re-read persisted feature/wallet state after conflicts or an ambiguous reply; never synthesize a refund.
      const loaded = await api.state().catch(() => null)
      if (!current()) return
      if (loaded) applyState(loaded)
      if (api.pendingSpin() || api.uncertainSpin()) setSpinAvailability('unavailable')
    } finally {
      finishSpinAnimation(moving)
      if (current()) {
        audio.stopLoop(spinCue)
        setIsSpinning(false); setIsStopRequested(false); busy.current = false; stopRequested.current = false
      }
      if (animation.current === moving) animation.current = null
      if (presentation.current === abort) presentation.current = null
    }
  }
  spinRef.current = spin
  function handleSpinButtonClick() {
    if (blocked || commandPending.current) return
    if (busy.current) {
      stopRequested.current = true; setIsStopRequested(true); setAutoSpinning(false)
      if (animation.current) requestQuickStop(animation.current)
      presentation.current?.abort(); audio.stopResultCues()
    } else void spinRef.current()
  }
  function selectWager(nextIndex: number) {
    if (busy.current || commandPending.current || state.freeSpinsRemaining > 0 || lockedPotionWagerPoints !== null || pending) return
    const index = Math.min(rules.wagerOptions.length - 1, Math.max(0, Math.round(nextIndex)))
    setAutoSpinning(false); setSpinError(null)
    if (index !== wagerIndex && ARCANE_ARCHIVES_SOUNDS.events.betStep) audio.playCue(ARCANE_ARCHIVES_SOUNDS.events.betStep)
    setWagerIndex(index)
  }
  const controlsRef = useRef({ handleSpinButtonClick, selectWager, wagerIndex, blocked })
  controlsRef.current = { handleSpinButtonClick, selectWager, wagerIndex, blocked }
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      const target = event.target instanceof HTMLElement ? event.target : null
      const action = getSlotKeyboardAction(event.key, {
        hasOpenDialog: controlsRef.current.blocked || document.querySelector('dialog[open], [role="dialog"][aria-modal="true"]') !== null,
        isEditableTarget: Boolean(target?.closest('input, textarea, select, [contenteditable="true"]')),
        modified: event.altKey || event.ctrlKey || event.metaKey, repeated: event.repeat,
      })
      if (!action) return
      event.preventDefault()
      if (action === 'toggle-mute') audioRef.current.toggleMuted()
      else if (action === 'spin-or-stop') controlsRef.current.handleSpinButtonClick()
      else if (action === 'decrease-wager') controlsRef.current.selectWager(controlsRef.current.wagerIndex - 1)
      else if (action === 'increase-wager') controlsRef.current.selectWager(controlsRef.current.wagerIndex + 1)
      else { setAutoSpinning(false); setIsHelpOpen(true) }
    }
    window.addEventListener('keydown', key, true)
    return () => window.removeEventListener('keydown', key, true)
  }, [])
  useEffect(() => {
    if (!isAutoSpinning || isSpinning || blocked || isArcaneUpdating || isTomeActivationRequired || spinAvailability !== 'available' || document.hidden) return
    const timer = window.setTimeout(() => void spinRef.current(), rules.autoSpinDelayMs / rules.autoSpinSpeedMultiplier)
    return () => window.clearTimeout(timer)
  }, [isAutoSpinning, isSpinning, blocked, isArcaneUpdating, isTomeActivationRequired, spinAvailability])
  function reelStripStyle(index: number): CSSProperties {
    const count = Math.max(1, displayedReels[index]?.length ?? 4)
    const moving = ['accelerating', 'spinning', 'braking'].includes(reelMotion[index])
    const visible = moving && count > rules.reelTravelRowCount ? count - rules.reelTravelRowCount : count
    return { '--slot-symbol-rows': count, '--slot-strip-height': `${count / visible * 100}%`,
      '--slot-spin-travel': `${-(count - visible) / count * 100}%` } as CSSProperties
  }
  const wandCollection = hasCompletedSpin ? lastResult?.arcaneWandCollection ?? null : null
  const lightningCollection = hasCompletedSpin ? wandCollection ? {
    collector: wandCollection.wand, cells: wandCollection.cells, totalPoints: wandCollection.totalPoints,
  } : lastResult?.lightningCollection ?? null : null
  const winningPositions = winningPaylines.flatMap(line => line.matches.flatMap(paid => paid.match.positions))
  return {
    account, balance, state, pointValueInCents, reducedMotion, prefersReducedMotion: reducedMotion,
    displayedReels, reelMotion, reelStripStyle, lastStoppedReel, isSpinning, isAutoSpinning,
    setIsAutoSpinning: setAutoSpinning, isStopRequested, spin, handleSpinButtonClick,
    spinAvailability, isSpinServiceDisabled: spinAvailability !== 'available', spinError, setSpinError,
    hasCompletedSpin, winningPaylines, winningPositions, resultKey, lastSpinOutcomeKey: resultKey, lastWin,
    lastResult, wandCollection, arcaneWandCollection: wandCollection, lightningCollection,
    lastWager: lastResult ? slotPointsToRand(lastResult.wagerPoints, lastResult.pointValueInCents) : activeWager,
    tomeState, potions, tomeIngredientsAwarded, confirmedPotionUse, changeTomePage, activatePotion,
    isArcaneUpdating, arcaneError, playPotionCue, isTomeActivationRequired, isPotionWagerLocked: lockedPotionWagerPoints !== null,
    wagerOptions: rules.wagerOptions, wagerIndex, selectWager, selectedWagerPoints,
    preferredWager, selectedWager: preferredWager, activeWager, activeWagerDisplay: activeWager, activeWagerPoints,
    canAffordSelectedWager, freeSpinsRemaining: state.freeSpinsRemaining,
    freeSpinWagerPoints: state.freeSpinWagerPoints, freeSpinFeatureMode: state.freeSpinFeatureMode,
    useFreeGameForNextSpin, isSpecialGameActive, roundSummary, dismissRoundSummary: () => setRoundSummary(null),
    isHelpOpen, setIsHelpOpen, isSettingsOpen, setIsSettingsOpen,
    isSymbolValuesOpen, setIsSymbolValuesOpen, isSymbolGuideOpen: isSymbolValuesOpen, setIsSymbolGuideOpen: setIsSymbolValuesOpen,
    isReloadPromptOpen, setIsReloadPromptOpen, setPresentationBusy,
    audio, audioPreferences: audio.preferences, playCue: audio.playCue, playSequence: audio.playSequence,
    setVolume: audio.setVolume, toggleMuted: audio.toggleMuted, toggleResultsOnly: audio.toggleResultsOnly,
    onNavigate: () => setAutoSpinning(false),
  }
}
export type ArcaneArchivesController = ReturnType<typeof useArcaneArchives>
