import { useMemo, type CSSProperties } from 'react'
import {
  AudioSettingsDialog,
  getSlotSymbolDefinition,
  getSlotSymbolValueLabel,
  InsufficientBalanceDialog,
  ResponsiveSlotImage,
  SidecarSpinControls,
  SlotMachine,
  SlotSymbol,
  SymbolValueGuide,
  WinHelpDialog,
  formatRand,
} from '@fortuneforge/slot-client-core'
import { GoldCrucibleMeter } from './GoldCrucible.js'
import { GOLD_FOUNDRY_CABINET, GOLD_FOUNDRY_FEATURES, GOLD_FOUNDRY_HELP, GOLD_FOUNDRY_RULES } from './manifest.js'
import { GOLD_FOUNDRY_SYMBOLS } from './symbols.js'
import type { GoldFoundryGameProps } from './types.js'
import { shouldUseAnimatedSymbol, type GoldFoundryController } from './useGoldFoundry.js'
import './styles.css'

function FoundryHelp() {
  return <>
    <p className="win-help__intro">Match symbols from left to right across any of the 14 fixed paylines.</p>
    <section className="win-help__section">
      <h3>Paylines</h3>
      <p>{GOLD_FOUNDRY_HELP.fourMatchPayoutNote}</p>
    </section>
    {GOLD_FOUNDRY_HELP.extraSections?.map(section => <section className="win-help__section" key={section.title}>
      <span className="win-help__badge">{section.badge}</span><h3>{section.title}</h3><p>{section.body}</p>
    </section>)}
  </>
}

export function GoldFoundryView({ controller: c, renderShell, paymentAlerts }: {
  controller: GoldFoundryController
} & Pick<GoldFoundryGameProps, 'renderShell' | 'paymentAlerts'>) {
  const highlightPositions = useMemo(() => c.winningPositions, [c.winningPositions])
  const spinDisabled = c.spinAvailability !== 'available' || c.blocked
  const openHelp = () => { c.setIsAutoSpinning(false); c.setIsHelpOpen(true) }
  const openSettings = () => { c.setIsAutoSpinning(false); c.setIsSettingsOpen(true) }
  const featureOnly = c.lastCollectorAward > 0 && c.lastWin === c.lastCollectorAward
  const outcomeLabel = featureOnly ? GOLD_FOUNDRY_FEATURES.moneyGrab.awardLabel
    : c.lastSpinOutcome?.kind === 'loss' ? 'Result' : 'Spin win'

  const content = <div className={`slots-page slots-page--theme-backdrop${c.isFastSpinActive ? ' slots-page--fast-spin' : ''}`}
    style={c.pageStyle} data-slot-theme={GOLD_FOUNDRY_CABINET.id} data-slot-controls="sidecar"
    data-slot-celebration={GOLD_FOUNDRY_CABINET.celebrationEffect}>
    <main className="slots-page__main">
      <h1 className="slots-page__accessible-title">Gold Foundry</h1>
      <div className="slots-page__layout">
        <div className="slots-page__stage">
          <img className="slots-page__title-art" {...GOLD_FOUNDRY_CABINET.titleArtwork!} alt="" />
          <div className="slots-page__cabinet-spotlight">
            <SlotMachine cabinetTheme={GOLD_FOUNDRY_CABINET} reelCount={c.displayedReels.length}
              renderReel={reelIndex => <div className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`}
                style={c.reelStripStyle(reelIndex)}>
                {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
                  const highlightOrder = highlightPositions.findIndex(position => position.reel === reelIndex && position.row === rowIndex)
                  return <SlotSymbol key={`row-${rowIndex}`} symbol={symbol} symbolSet={GOLD_FOUNDRY_SYMBOLS}
                    reelIndex={reelIndex} rowIndex={rowIndex}
                    animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
                    beingGrabbed={c.moneyGrabPresentation?.tokens.some(token =>
                      token.reel === reelIndex && token.row === rowIndex) ?? false}
                    highlighted={highlightOrder >= 0} highlightOrder={highlightOrder} />
                })}
              </div>} />
          </div>

          <GoldCrucibleMeter balance={c.state.energyBalance} capacity={GOLD_FOUNDRY_RULES.energyMeterCapacity}
            meterRef={c.energyMeterRef} award={c.crucibleAward} completing={c.isCruciblePouring} />

          <div className="slots-page__playbar" aria-label="Wager and spin controls">
            <SidecarSpinControls activeWager={c.activeWagerDisplay} disabled={spinDisabled && !c.isSpinning}
              freeSpinsRemaining={0} isAutoSpinning={c.isAutoSpinning} isFreeSpinBadgePopping={false}
              isSpinning={c.isSpinning} isStopRequested={c.isStopRequested}
              onAutoSpinChange={active => { c.setSpinError(null); c.setIsAutoSpinning(active) }}
              onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick}
              pointValueInCents={GOLD_FOUNDRY_RULES.pointValueInCents} showFreeSpinBadge={false}
              specialControlsActive={false} specialRoundLabel={null} wagerIndex={c.wagerIndex}
              wagerOptions={GOLD_FOUNDRY_RULES.wagerOptions} />
          </div>

          {paymentAlerts && <div className="slots-page__local-controls"><div className="slots-page__utility-controls" aria-label="Account alerts">
            {paymentAlerts}
          </div></div>}
        </div>
      </div>
    </main>

    <SymbolValueGuide symbolSet={GOLD_FOUNDRY_SYMBOLS} showSidePanel sidePanelClassName="symbol-value-guide--pirates"
      showValueTokens={false} wager={c.activeWagerDisplay} isOpen={c.isSymbolGuideOpen} onOpenChange={c.setIsSymbolGuideOpen} />
    <div className="slots-page__utility-buttons slots-page__utility-buttons--pirates" role="group" aria-label="Game options">
      <button className="slots-page__help-button" type="button" aria-label="How to win" aria-haspopup="dialog"
        aria-expanded={c.isHelpOpen} onClick={openHelp}><span aria-hidden="true">?</span><span className="slots-page__help-button-label">How to win</span></button>
      <button className="slots-page__settings-button" type="button" aria-label="Open settings" aria-haspopup="dialog"
        aria-expanded={c.isSettingsOpen} onClick={openSettings}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" />
          <circle cx="12" cy="11.5" r="3" />
        </svg>
      </button>
    </div>

    {c.lastSpinOutcome && c.lastSpinOutcome.kind !== 'loss' && <div key={c.resultAtmosphereId}
      className="slots-page__result-atmosphere" data-celebration="foundry-sparks"
      data-outcome={c.lastSpinOutcome.kind} aria-hidden="true"><span /><span /></div>}

    {c.energyFlyover && <ResponsiveSlotImage key={c.energyFlyover.id} className="slots-page__energy-flyover"
      src={getSlotSymbolDefinition(GOLD_FOUNDRY_SYMBOLS, 'BOLT').image} alt="" aria-hidden="true"
      style={{ left: c.energyFlyover.left, top: c.energyFlyover.top, width: c.energyFlyover.width,
        height: c.energyFlyover.height, animationDuration: `${c.energyFlyover.durationMs}ms`,
        '--energy-travel-x': `${c.energyFlyover.travelX}px`, '--energy-travel-y': `${c.energyFlyover.travelY}px` } as CSSProperties} />}

    {c.moneyGrabPresentation && <div key={c.moneyGrabPresentation.id} className="slots-page__money-grab" role="status"
      aria-label={`${GOLD_FOUNDRY_FEATURES.moneyGrab.actorName} grabbed ${formatRand(c.moneyGrabPresentation.amount)}`}>
      {c.moneyGrabPresentation.tokens.map(token => {
        const definition = getSlotSymbolDefinition(GOLD_FOUNDRY_SYMBOLS, token.symbol)
        return <div key={token.id} className="slots-page__money-grab-token"
          data-value-label={getSlotSymbolValueLabel(definition, c.activeWagerDisplay)}
          aria-hidden="true" style={{ left: token.left, top: token.top, width: token.width, height: token.height,
            animationDelay: `${token.delayMs}ms`, animationDuration: `${token.durationMs}ms`,
            '--money-grab-travel-x': `${token.travelX}px`, '--money-grab-travel-y': `${token.travelY}px` } as CSSProperties}>
          <ResponsiveSlotImage src={definition.image} alt="" />
        </div>
      })}
      <ResponsiveSlotImage className="slots-page__money-grab-paw"
        src={getSlotSymbolDefinition(GOLD_FOUNDRY_SYMBOLS, 'PAW').image} alt="" aria-hidden="true"
        style={{ left: c.moneyGrabPresentation.pawLeft, top: c.moneyGrabPresentation.pawTop,
          width: c.moneyGrabPresentation.pawSize, height: c.moneyGrabPresentation.pawSize,
          animationDuration: `${c.moneyGrabPresentation.pawDurationMs}ms`,
          '--money-grab-travel-x': `${c.moneyGrabPresentation.pawTravelX}px`,
          '--money-grab-travel-y': `${c.moneyGrabPresentation.pawTravelY}px` } as CSSProperties} />
      <div className="slots-page__money-grab-award" aria-hidden="true"
        style={{ left: c.moneyGrabPresentation.popupLeft, top: c.moneyGrabPresentation.popupTop,
          animationDelay: `${c.moneyGrabPresentation.popupDelayMs}ms`,
          animationDuration: `${c.moneyGrabPresentation.popupDurationMs}ms` }}>
        <ResponsiveSlotImage src={getSlotSymbolDefinition(GOLD_FOUNDRY_SYMBOLS, 'PAW').image} alt="" />
        <span><small>{GOLD_FOUNDRY_FEATURES.moneyGrab.awardLabel}</small>
          <strong>+{formatRand(c.moneyGrabPresentation.amount)}</strong></span>
      </div>
    </div>}

    {c.winAward && <div key={c.winAward.id}
      className={`slots-page__win-award slots-page__win-award--${c.winAward.winTier} slots-page__win-award--${c.winAward.tier}${c.winAward.isFlying ? ' slots-page__win-award--flying' : ''}`}
      aria-hidden="true" style={{ left: c.winAward.left, top: c.winAward.top,
        animationDuration: `${c.winAward.durationMs}ms`, '--win-travel-x': `${c.winAward.travelX}px`,
        '--win-travel-y': `${c.winAward.travelY}px` } as CSSProperties}>
      <span>{c.lastSpinOutcome?.title ?? 'Win'}</span><strong>+{formatRand(c.winAward.displayAmount)}</strong>
    </div>}

    {(c.hasCompletedSpin || c.spinError || c.spinAvailability === 'unavailable') && <footer
      className={`slots-page__footer${c.spinError || c.spinAvailability === 'unavailable' ? ' slots-page__footer--error' : ''}`}>
      <section className={`game-outcome-banner slots-page__outcome game-outcome-banner--${c.spinError ? 'loss' : c.lastSpinOutcome?.kind === 'loss' ? 'loss' : 'win'}`}
        role="status" aria-live="polite" aria-atomic="true">
        <span className="game-outcome-banner__label">{c.spinError ? 'Unable to spin' : outcomeLabel}</span>
        <strong className="game-outcome-banner__title">{c.spinError ?? (c.spinAvailability === 'unavailable'
          ? 'The Gold Foundry game service is offline.'
          : featureOnly ? `+${formatRand(c.lastWin)}` : c.lastSpinOutcome?.title)}</strong>
        {!c.spinError && c.lastSpinOutcome?.kind !== 'loss' && !featureOnly && <span className="game-outcome-banner__detail">+{formatRand(c.lastWin)}</span>}
        {!c.spinError && <small className="game-outcome-banner__next"><span aria-hidden="true">→</span> Choose a wager, then spin again when ready.</small>}
      </section>
    </footer>}

    <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
      <span><kbd>Space</kbd> Spin / stop</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
      <button className="slots-page__mute-shortcut" type="button" aria-pressed={c.audio.preferences.mode === 'muted'} onClick={c.audio.toggleMuted}>
        <kbd aria-hidden="true">M</kbd> Mute
      </button><span><kbd>?</kbd> Rules</span>
    </div>

    <AudioSettingsDialog isOpen={c.isSettingsOpen} preferences={c.audio.preferences}
      onClose={() => c.setIsSettingsOpen(false)} onToggleMuted={c.audio.toggleMuted}
      onToggleResultsOnly={c.audio.toggleResultsOnly} onVolumeChange={c.audio.setVolume} />
    <WinHelpDialog isOpen={c.isHelpOpen} closeButtonRef={c.helpCloseButtonRef}
      onClose={() => c.setIsHelpOpen(false)}><FoundryHelp /></WinHelpDialog>
    <InsufficientBalanceDialog isOpen={c.isReloadPromptOpen} closeButtonRef={c.reloadPromptCloseButtonRef}
      selectedWager={c.activeWagerDisplay} balance={c.balance}
      recoveryHref={c.demoMode && typeof location !== 'undefined' ? location.pathname : '/balance'}
      recoveryLabel={c.demoMode ? 'Reset balance' : 'Add Rand'} onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>

  return renderShell
    ? renderShell({ balance: c.balance, onNavigate: () => c.setIsAutoSpinning(false), children: content })
    : content
}
