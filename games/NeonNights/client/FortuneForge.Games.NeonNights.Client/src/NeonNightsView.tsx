import { useCallback, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import {
  AudioSettingsDialog, getSlotSymbolDefinition, InsufficientBalanceDialog, SidecarSpinControls,
  SlotMachine, SlotSymbol, SymbolValueGuide, shouldUseAnimatedSymbol, WinHelpDialog,
} from '@fortuneforge/slot-client-core'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { neonDiscoMusic } from './audio.js'
import { neonCabinet, neonCarSymbols, neonRules, neonSymbols } from './manifest.js'
import { NeonDiscoFloor } from './components/NeonDiscoFloor.js'
import { NeonDiscoClusters } from './components/NeonDiscoClusters.js'
import { NeonDiscoHelp } from './components/NeonDiscoHelp.js'
import { NeonHelp } from './components/NeonHelp.js'
import { NeonNitroFlights } from './components/NeonNitroFlights.js'
import { NeonRoundSummary } from './components/NeonRoundSummary.js'
import { NeonSpecialPlaysIntro, type DiscoFlare } from './components/NeonSpecialPlaysIntro.js'
import { neonDiscoSymbols } from './disco.js'
import { isCarPlayMode, neonCarPlayMode } from './roundState.js'
import type { NeonNightsController } from './useNeonNights.js'
import { discoMultipliers, type NeonNightsGameProps, type NeonTriggerSymbol } from './types.js'
import './styles.css'
import './components/NeonDiscoIntroFlare.css'

const money = (amount: number) => `R${new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)}`
const adminSymbols = [{ symbol: '6', label: 'Sports car' }, { symbol: 'POWER', label: 'Disco ball' }] as const
const nitroBottleImage = getSlotSymbolDefinition(neonCarSymbols, 'NITRO').image
const reelPickPulseMs = 1_000
const paylineAppearance = { classPrefix: 'neon-winning-paylines', variant: 'neon', labelOffset: 22, labelMinimumY: 34,
  amountGradient: [{ offset: '0%', color: '#ffe54c' }, { offset: '24%', color: '#ffdb21' }, { offset: '49%', color: '#ffc00d' }, { offset: '76%', color: '#f99508' }, { offset: '100%', color: '#d45607' }] }

export function NeonNightsView({ controller: c, renderShell }: { controller: NeonNightsController } & Pick<NeonNightsGameProps, 'renderShell'>) {
  const cabinetRef = useRef<HTMLDivElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  const [discoFlare, setDiscoFlare] = useState<DiscoFlare | null>(null)
  const discoVisible = c.discoActive && !c.specialIntro
  const carControlsActive = !discoVisible && (c.specialActive || c.arcadeExpanding)
  const specialSpinCount = discoVisible ? c.state.neonDiscoSpinsRemaining
    : carControlsActive && !c.specialIntro ? c.state.freeSpinsRemaining : null
  const showSpinTotal = c.hasCompletedSpin && !c.isSpinning && c.winningPaylines.length > 1 && c.lastWin > 0
  const nitro = c.nitroPresentation
  const choice = c.nitroChoice
  const choosingNitro = Boolean(choice) || c.isChoosingNitro
  const showNitroOptions = Boolean(choice) && !c.isSpinning && !c.isChoosingNitro
  const showRespinOptions = !choosingNitro && c.respinSelecting && c.canChooseRespin
  const reelPick = showNitroOptions ? 'WILD' : showRespinOptions ? 'RESPIN' : null
  const eligibleReels = showNitroOptions ? c.nitroChoiceReels : showRespinOptions ? c.respinReels : []
  const pickReels = [...eligibleReels].sort((left, right) => left - right)
  const showRoundSummary = c.roundSummary !== null && !reelPick && !c.isRespinning && !choosingNitro
  const nitroBottles = nitro ?? choice?.neonNitroWild
  const secondNitro = nitroBottles?.secondNitro
  const nitroPhase = nitro?.phase ?? (showNitroOptions ? 'choosing' : undefined)
  const finishIntro = useCallback((flare?: DiscoFlare) => {
    if (flare && c.discoActive) setDiscoFlare(flare)
    if (c.specialIntro) c.finishSpecialIntro(c.specialIntro.symbol)
  }, [c.discoActive, c.finishSpecialIntro, c.specialIntro])
  const fallbackPositions = useMemo(() => c.winningPaylines.flatMap(line => line.matches.flatMap(match => match.match.positions)), [c.winningPaylines])
  const highlighted = discoVisible
    ? c.cascadeFrame && c.cascadeFrame.phase !== 'drop' ? c.cascadeFrame.cleared : []
    : c.hasCompletedSpin && activePath?.resultKey === c.resultKey ? activePath.positions : fallbackPositions
  const showCarValues = (carControlsActive && c.state.freeSpinFeatureMode === neonCarPlayMode) ||
    c.roundSummary?.mode === neonCarPlayMode
  const symbolSet = discoVisible ? neonDiscoSymbols : showCarValues ? neonCarSymbols : neonSymbols
  const special = c.specialActive || discoVisible || c.arcadeExpanding
  const openHelp = () => { c.setIsAutoSpinning(false); c.setIsHelpOpen(true) }
  const openSettings = () => { c.setIsAutoSpinning(false); c.setIsSettingsOpen(true) }
  const canAdmin = (c.demoMode && import.meta.env.DEV) || c.account?.role.toLowerCase() === 'admin'
  const content = <div
    className={`slots-page slots-page--theme-backdrop${special ? ' slots-page--special-game' : ''}${c.isAutoSpinning || c.isStopRequested ? ' slots-page--fast-spin' : ''}`}
    style={c.pageStyle} data-slot-theme={neonCabinet.id} data-slot-controls="sidecar"
    data-slot-celebration="neon-scan" data-special-round="neon-midnight-mix" data-special-earn-style="orbit"
    data-neon-arcade-expanding={c.arcadeExpanding ? 'true' : undefined}
    data-neon-arcade-rows={c.carCabinetExpanded ? 'true' : undefined}
    data-neon-car-round={carControlsActive ? 'true' : undefined}
    data-neon-disco-round={discoVisible ? 'true' : undefined} data-neon-disco-cascade={c.cascadeFrame?.phase}
    data-neon-disco-deal={c.discoDealPhase ?? undefined}
    data-neon-nitro-phase={nitroPhase}
    data-neon-respin-selection={showRespinOptions ? 'true' : undefined}
  >
    <main className="slots-page__main">
      <h1 className="slots-page__accessible-title">Neon Nights</h1>
      <div className="slots-page__layout"><div className="slots-page__stage">
        <div className="neon-title-lockup">
          <img className="slots-page__title-art" {...neonCabinet.titleArtwork} alt="" />
          {reelPick && <div className="neon-reel-pick-prompt" role="status">{reelPick}</div>}
        </div>
        {discoVisible && <div className="neon-disco-multipliers" role="list" aria-label="Disco multipliers">
          {discoMultipliers.map(multiplier => <div className="neon-disco-multipliers__badge" role="listitem" key={multiplier}
            data-active={c.discoMultiplier === multiplier ? 'true' : undefined}
            aria-current={c.discoMultiplier === multiplier ? 'step' : undefined}>
            <span>×{multiplier}</span>
          </div>)}
        </div>}
        <div ref={cabinetRef} className={`slots-page__cabinet-spotlight${special ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
          <SlotMachine cabinetTheme={neonCabinet} reelCount={5} renderReel={reelIndex => {
            const hasBottle = reelIndex === nitroBottles?.trigger.reel || reelIndex === secondNitro?.reel
            const nitroReel = nitro && (reelIndex === nitro.targetReel ? 'target' : hasBottle ? 'bottle' : 'other')
            const pickOrder = pickReels.indexOf(reelIndex)
            const pickStyle = pickOrder < 0 ? undefined : {
              '--neon-reel-pick-delay': `${pickOrder * reelPickPulseMs}ms`,
              '--neon-reel-pick-cycle': `${pickReels.length * reelPickPulseMs}ms`,
              '--neon-reel-pick-peak': `${50 / pickReels.length}%`, '--neon-reel-pick-end': `${100 / pickReels.length}%`,
            } as CSSProperties
            return <div className="neon-reel-face" data-neon-reel-pick={pickOrder >= 0 ? 'true' : undefined} style={pickStyle}>
              <div className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`}
                data-neon-nitro-reel={nitroReel ?? undefined} style={c.reelStripStyle(reelIndex)}>
              {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
                const order = highlighted.findIndex(cell => cell.reel === reelIndex && cell.row === rowIndex)
                const cleared = c.cascadeFrame?.cleared.some(cell => cell.reel === reelIndex && cell.row === rowIndex) ?? false
                const dropRows = c.cascadeFrame?.phase === 'drop' ? c.cascadeFrame.dropRows[reelIndex]?.[rowIndex] ?? 0 : 0
                const tile = <SlotSymbol key={`row-${rowIndex}`} symbol={symbol} symbolSet={symbolSet}
                  reelIndex={reelIndex} rowIndex={rowIndex} animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
                  highlighted={!reelPick && order >= 0} highlightOrder={discoVisible ? 0 : order} />
                if (nitroPhase && (nitroBottles?.trigger.reel === reelIndex && nitroBottles.trigger.row === rowIndex ||
                    secondNitro?.reel === reelIndex && secondNitro.row === rowIndex)) {
                  return <div className="neon-nitro-bottle" key={`row-${rowIndex}`}>{tile}
                    {symbol !== 'NITRO' && <img className="neon-nitro-bottle__afterimage" src={nitroBottleImage} alt="" aria-hidden="true" />}
                  </div>
                }
                if (c.discoDealPhase) return <div key={`row-${rowIndex}`}
                  className={`neon-disco-deal__cell neon-disco-deal__cell--${c.discoDealPhase}`}
                  style={c.discoDealStyle(reelIndex, rowIndex)}>{tile}</div>
                return c.cascadeFrame && (cleared || dropRows > 0) ? <div key={`row-${rowIndex}`}
                  className={`neon-disco-cascade__cell neon-disco-cascade__cell--${dropRows > 0 ? 'drop' : c.cascadeFrame.phase}`}
                  style={dropRows > 0 ? { '--neon-disco-drop-rows': dropRows } as CSSProperties : undefined}>{tile}</div> : tile
                })}
              </div>
              {nitroReel === 'target' && <span className="neon-nitro-edge" aria-hidden="true" />}
              {pickOrder >= 0 && <button className="neon-reel-pick-button" type="button"
                aria-label={showNitroOptions ? `Turn reel ${reelIndex + 1} Wild` : `Respin reel ${reelIndex + 1}`}
                onClick={() => { void (showNitroOptions ? c.chooseNitroReel(reelIndex) : c.chooseRespinReel(reelIndex)) }} />}
            </div>
          }} />
          {nitro?.phase === 'expanding' && <NeonNitroFlights reel={nitro.targetReel}
            image={getSlotSymbolDefinition(neonCarSymbols, 'ACE').image} />}
          {discoVisible ? <NeonDiscoClusters cabinetRef={cabinetRef} clusters={c.winningPaylines}
            resultKey={c.resultKey} visible={c.hasCompletedSpin && c.cascadeFrame !== null && c.cascadeFrame.phase !== 'drop'}
            reducedMotion={c.reducedMotion} formatAmount={points => `+${money(points / 100)}`} />
          : <WinningPaylines appearance={paylineAppearance} boardSelector=".slots-page__cabinet-spotlight"
            reelsSelector=".slot-machine__reels" cellSelector=".slot-symbol[data-reel-index][data-row-index]"
            paylines={c.winningPaylines} resultKey={c.resultKey} visible={c.hasCompletedSpin && !reelPick}
            reducedMotion={c.reducedMotion} formatWinAmount={points => `+${money(points / 100)}`} onActivePathChange={setActivePath} />}
        </div>
        {c.carCabinetExpanded || discoVisible ? <div className="neon-spin-total neon-round-subtotal">
          <output className="neon-win-amount" aria-label={discoVisible ? 'Disco Plays subtotal' : 'Car Plays subtotal'}>{money(c.roundWinnings)}</output>
        </div> : <div className="neon-spin-total">
          {showSpinTotal && <output className="neon-win-amount" aria-label="Total win">+{money(c.lastWin)}</output>}
        </div>}
        <div className="slots-page__playbar" aria-label="Wager and spin controls">
          <SidecarSpinControls activeWager={c.activeWager} disabled={c.spinAvailability !== 'available' || Boolean(c.specialIntro) || Boolean(c.roundSummary) || c.arcadeExpanding || c.isRespinning || choosingNitro}
            freeSpinsRemaining={c.state.freeSpinsRemaining} isAutoSpinning={c.isAutoSpinning} isFreeSpinBadgePopping={false}
            autoSpinReplacement={specialSpinCount === null ? undefined : (
              <output className="neon-special-spin-count" aria-label={discoVisible ? 'Disco spins remaining' : 'Car spins remaining'}>×{specialSpinCount}</output>
            )}
            isSpinning={c.isSpinning} isStopRequested={c.isStopRequested} allowStop={!c.discoActive && !c.isRespinning} onAutoSpinChange={active => { c.setSpinError(null); c.setIsAutoSpinning(active) }}
            onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick} pointValueInCents={1} showFreeSpinBadge={false}
            specialControlsActive={carControlsActive} specialRoundLabel="Special Plays" wagerIndex={c.wagerIndex} wagerOptions={neonRules.wagerOptions} />
          {showRespinOptions && c.roundSummary && <button type="button" className="neon-respin-button"
            onClick={c.cancelRespinSelection}>Continue</button>}
        </div>
      </div></div>
    </main>
    <SymbolValueGuide symbolSet={symbolSet} showSidePanel={!c.specialIntro} sidePanelMode={special ? 'on-demand' : 'auto'}
      sidePanelClassName="symbol-value-guide--neon" showValueTokens={false}
      wager={c.activeWager} isOpen={c.isSymbolGuideOpen} onOpenChange={c.setIsSymbolGuideOpen} />
    <div className="slots-page__utility-buttons neon-utility-buttons" role="group" aria-label="Game options">
      <button className="slots-page__help-button" type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={c.isHelpOpen} onClick={openHelp}>
        <span aria-hidden="true">?</span>
      </button>
      <button className="slots-page__settings-button" type="button" aria-label="Open settings" aria-haspopup="dialog" aria-expanded={c.isSettingsOpen} onClick={openSettings}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" /><circle cx="12" cy="11.5" r="3" />
        </svg>
      </button>
      {canAdmin && <div className="neon-admin-triggers" role="group" aria-label="Admin Special Plays triggers">{adminSymbols.map(({ symbol, label }) => <button
        key={symbol} className="neon-admin-triggers__button" type="button" aria-label={`Trigger ${label} Special Plays`}
        disabled={c.spinAvailability !== 'available' || c.isSpinning || c.isAdminTriggering || c.state.freeSpinsRemaining > 0 || c.state.neonDiscoSpinsRemaining > 0 || Boolean(c.roundSummary) || Boolean(c.specialIntro) || c.arcadeExpanding}
        onClick={() => { void c.triggerAdmin(symbol as NeonTriggerSymbol) }}><img src={getSlotSymbolDefinition(neonSymbols, symbol).image} alt="" aria-hidden="true" /></button>)}</div>}
    </div>
    {c.hasCompletedSpin && c.lastWin > 0 && <div key={c.resultKey} className="slots-page__result-atmosphere" data-celebration="neon-scan" data-outcome="win" aria-hidden="true">{Array.from({ length: 14 }, (_, index) => <i key={index} />)}</div>}
    {special && <div className="slots-page__special-game-dimmer" aria-hidden="true" />}
    {discoVisible && <NeonDiscoFloor bpm={neonDiscoMusic.bpm} getPlaybackSeconds={c.getDiscoMusicTime} />}
    {c.specialIntro && !c.isSpinning && <NeonSpecialPlaysIntro cabinetRef={cabinetRef} symbol={c.specialIntro.symbol}
      volume={c.audio.preferences.mode === 'muted' ? 0 : c.audio.preferences.volume / 100}
      onDiscoMusicStart={c.discoActive ? c.startDiscoIntroMusic : undefined}
      onComplete={finishIntro} />}
    {discoFlare && createPortal(
      <div className="neon-disco-intro-flare" aria-hidden="true"
        style={{
          '--disco-flare-x': `${discoFlare.x}px`,
          '--disco-flare-y': `${discoFlare.y}px`,
          '--disco-flare-strength': discoFlare.strength,
        } as CSSProperties}
        onAnimationEnd={event => {
          if (event.animationName === 'neon-disco-intro-flare-fade') setDiscoFlare(null)
        }} />,
      document.body,
    )}
    {showRoundSummary && c.roundSummary && <NeonRoundSummary cabinetRef={cabinetRef} title={isCarPlayMode(c.roundSummary.mode) ? 'Car Plays' : c.roundSummary.mode === 'neon-disco' ? 'Disco Plays' : 'Special Plays'}
      amount={money(c.roundSummary.totalRand)} onDismiss={c.dismissRoundSummary} busy={c.respinDismissPending}
      error={c.spinError} />}
    {c.winAward && <div key={c.winAward.id}
      className={`slots-page__win-award slots-page__win-award--neon slots-page__win-award--${c.winAward.winTier} slots-page__win-award--${c.winAward.tier}${c.winAward.flight ? ' slots-page__win-award--flying' : ''}${c.winAward.flight === 'spin-total' ? ' neon-win-award--to-total' : ''}`}
      aria-hidden="true" style={{ left: c.winAward.left, top: c.winAward.top, animationDuration: `${c.winAward.durationMs}ms`, '--win-travel-x': `${c.winAward.travelX}px`, '--win-travel-y': `${c.winAward.travelY}px`, '--neon-win-total-scale': c.winAward.endScale } as CSSProperties}>
      {c.winAward.flight !== 'spin-total' && <span>{c.winAward.tier !== 'win' || c.winAward.winTier === 'big' ? 'BIG WIN!' : c.winAward.winTier === 'great' ? 'GREAT WIN!' : 'NICE!'}</span>}
      <strong className="neon-win-amount">+{money(c.winAward.displayAmount)}</strong>
    </div>}
    {c.spinError && !showRoundSummary && <div className="neon-spin-error" role="status">{c.spinError}</div>}
    <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
      <span><kbd>Space</kbd> {c.discoActive ? 'Spin' : 'Spin / stop'}</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
      <button className="slots-page__mute-shortcut" type="button" aria-pressed={c.audio.preferences.mode === 'muted'} onClick={c.audio.toggleMuted}><kbd aria-hidden="true">M</kbd> Mute</button>
    </div>
    <AudioSettingsDialog theme="neon-nights" isOpen={c.isSettingsOpen} onClose={() => c.setIsSettingsOpen(false)} preferences={c.audio.preferences}
      onToggleMuted={c.audio.toggleMuted} onToggleResultsOnly={c.audio.toggleResultsOnly} onVolumeChange={c.audio.setVolume} />
    <WinHelpDialog theme="neon-nights" isOpen={c.isHelpOpen} closeButtonRef={c.helpCloseButtonRef} onClose={() => c.setIsHelpOpen(false)} title="How to win" eyebrow="Neon Nights">
      {c.discoActive ? <NeonDiscoHelp wager={c.activeWager} /> : <NeonHelp carPlay={carControlsActive} />}
    </WinHelpDialog>
    <InsufficientBalanceDialog theme="neon-nights" isOpen={c.isReloadPromptOpen} closeButtonRef={c.reloadPromptCloseButtonRef}
      selectedWager={c.activeWager} balance={c.balance} recoveryHref={c.demoMode ? location.pathname : '/balance'} recoveryLabel={c.demoMode ? 'Reset balance' : 'Add Rand'} onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>
  return renderShell ? renderShell({ balance: c.balance, onNavigate: () => c.setIsAutoSpinning(false), children: content }) : content
}
