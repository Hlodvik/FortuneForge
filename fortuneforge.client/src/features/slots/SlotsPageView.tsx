import type { CSSProperties } from 'react'
import { ForgeCoin } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../games/slots/shared/mascot/MascotCompanion'
import { AudioSettingsDialog } from './components/AudioSettingsDialog'
import { CollectionProgressDisplay } from './components/CollectionProgressDisplay'
import { SlotMachine } from './components/SlotMachine'
import { SlotSymbol } from './components/SlotSymbol'
import { SpinButton } from './components/SpinButton'
import { SymbolValueGuide } from './components/SymbolValueGuide'
import { getSlotSymbolDefinition } from './config/symbolSets'
import { InsufficientBalanceDialog } from './InsufficientBalanceDialog'
import { shouldUseAnimatedSymbol } from './presentation/spinLifecycle'
import { creditFormatter, formatRand, getSlotSymbolValueLabel } from './slotPagePresentation'
import type { SlotsPageController } from './useSlotsPageController'
import { WinHelpDialog } from './WinHelpDialog'

export function SlotsPageView(controller: SlotsPageController) {
  const {
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
    help,
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
    moneyGrabPresentation,
    pageBackdropStyle,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    reloadPromptCloseButtonRef,
    selectedWager,
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
  } = controller
  const collectionFeature = featureSet.collections
  const energyFeature = featureSet.energy
  const moneyGrabFeature = featureSet.moneyGrab

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
            {demoMode ? (
              <a
                className="slots-page__demo-badge"
                href="/demo"
                onClick={() => setIsAutoSpinning(false)}
              >
                Demo · {formatRand(demoStartingBalance)} start
              </a>
            ) : (
              <>
                <a
                  className="slots-page__purchase-credits"
                  href="/home/rand"
                  aria-label="Add Rand"
                  onClick={() => setIsAutoSpinning(false)}
                >
                  <ForgeCoin className="slots-page__purchase-credits-coin" />
                  <span>Add Rand</span>
                </a>
                <PaymentAlertsMenu />
              </>
            )}
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
          <div className="slots-page__stage">
          {(collectionFeature || energyFeature) && (
          <div className="slots-page__meter-stack">
            {collectionFeature && (
            <div className="slots-page__seal-collections" aria-label={collectionFeature.ariaLabel}>
              {visibleSealCollections.map((collection) => {
                const seal = collectionFeature.entries.find((entry) => entry.id === collection.sealId)
                if (!seal) return null
                return (
                  <CollectionProgressDisplay
                    collection={collection}
                    definition={seal}
                    image={getSlotSymbolDefinition(symbolSet, seal.symbol).image}
                    isImpacting={sealImpactId === collection.sealId}
                    itemLabel={collectionFeature.itemLabel ?? 'seals'}
                    key={collection.sealId}
                    presentation={collectionFeature.presentation ?? 'seal-pile'}
                  />
                )
              })}
            </div>
            )}

            {energyFeature && (
            <div
              key={`energy-meter-${energyImpactKey}`}
              ref={energyMeterRef}
              className={`slots-page__energy-meter${energyImpactKey > 0 ? ' slots-page__energy-meter--impact' : ''}`}
              role="progressbar"
              aria-label={`${energyFeature.label}: ${creditFormatter.format(energyBalance)}`}
              aria-valuemin={0}
              aria-valuemax={energyMeterCapacity}
              aria-valuenow={Math.min(energyMeterCapacity, energyBalance)}
            >
              <img src={getSlotSymbolDefinition(symbolSet, energyFeature.symbol).image} alt="" aria-hidden="true" />
              <span className="slots-page__energy-copy">
                <span className="slots-page__energy-label">{energyFeature.label}</span>
                <span className="slots-page__energy-track" aria-hidden="true">
                  <span
                    className="slots-page__energy-fill"
                    style={{ width: `${Math.min(100, energyBalance / energyMeterCapacity * 100)}%` }}
                  />
                </span>
                <strong>{creditFormatter.format(energyBalance)}/{energyMeterCapacity}</strong>
              </span>
            </div>
            )}
          </div>
          )}

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
                    valueLabel={getSlotSymbolValueLabel(
                      getSlotSymbolDefinition(symbolSet, symbol),
                      activeWagerDisplay,
                    )}
                    reelIndex={reelIndex}
                     rowIndex={rowIndex}
                    animated={shouldUseAnimatedSymbol(
                      prefersReducedMotion,
                      reelMotion[reelIndex],
                    )}
                    beingGrabbed={moneyGrabPresentation?.tokens.some(
                      (token) => token.reel === reelIndex && token.row === rowIndex,
                    ) ?? false}
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
            <SymbolValueGuide symbolSet={symbolSet} />

            <div
              ref={creditTileRef}
              className="slots-page__balance slots-page__control-tile"
              aria-label={`Balance: ${formatRand(balance)}`}
            >
              <span className="slots-page__balance-label">{demoMode ? 'Demo balance' : 'Balance'}</span>
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
                    disabled={isDemoSpinDisabled}
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
                  disabled={isDemoSpinDisabled}
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

      {energyFlyover && energyFeature && (
        <img
          key={energyFlyover.id}
          className="slots-page__energy-flyover"
          src={getSlotSymbolDefinition(symbolSet, energyFeature.symbol).image}
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

      {sealFlyover && collectionFeature && (
        <img
          key={sealFlyover.id}
          className={`slots-page__seal-flyover slots-page__seal-flyover--${sealFlyover.collectionId}`}
          data-seal-id={sealFlyover.collectionId}
          src={getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).image}
          alt=""
          aria-hidden="true"
          style={{
            left: sealFlyover.left,
            top: sealFlyover.top,
            width: sealFlyover.width,
            height: sealFlyover.height,
            animationDuration: `${sealFlyover.durationMs}ms`,
            '--seal-travel-x': `${sealFlyover.travelX}px`,
            '--seal-travel-y': `${sealFlyover.travelY}px`,
          } as CSSProperties}
        />
      )}

      {moneyGrabPresentation && moneyGrabFeature && (
        <div
          key={moneyGrabPresentation.id}
          className="slots-page__money-grab"
          role="status"
          aria-label={`${moneyGrabFeature.actorName} grabbed ${formatRand(moneyGrabPresentation.amount)}`}
        >
          {moneyGrabPresentation.tokens.map((token) => {
            const definition = getSlotSymbolDefinition(symbolSet, token.symbol)
            const valueLabel = getSlotSymbolValueLabel(definition, activeWagerDisplay)
            return (
              <div
                key={token.id}
                className="slots-page__money-grab-token"
                data-value-label={valueLabel}
                aria-hidden="true"
                style={{
                  left: token.left,
                  top: token.top,
                  width: token.width,
                  height: token.height,
                  animationDelay: `${token.delayMs}ms`,
                  animationDuration: `${token.durationMs}ms`,
                  '--money-grab-travel-x': `${token.travelX}px`,
                  '--money-grab-travel-y': `${token.travelY}px`,
                } as CSSProperties}
              >
                <img src={definition.image} alt="" />
              </div>
            )
          })}

          <img
            className="slots-page__money-grab-paw"
            src={getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).image}
            alt=""
            aria-hidden="true"
            style={{
              left: moneyGrabPresentation.pawLeft,
              top: moneyGrabPresentation.pawTop,
              width: moneyGrabPresentation.pawSize,
              height: moneyGrabPresentation.pawSize,
              animationDuration: `${moneyGrabPresentation.pawDurationMs}ms`,
              '--money-grab-travel-x': `${moneyGrabPresentation.pawTravelX}px`,
              '--money-grab-travel-y': `${moneyGrabPresentation.pawTravelY}px`,
            } as CSSProperties}
          />

          <div
            className="slots-page__money-grab-award"
            aria-hidden="true"
            style={{
              left: moneyGrabPresentation.popupLeft,
              top: moneyGrabPresentation.popupTop,
              animationDelay: `${moneyGrabPresentation.popupDelayMs}ms`,
              animationDuration: `${moneyGrabPresentation.popupDurationMs}ms`,
            }}
          >
            <img src={getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).image} alt="" />
            <span>
              <small>{moneyGrabFeature.awardLabel}</small>
              <strong>+{formatRand(moneyGrabPresentation.amount)}</strong>
            </span>
          </div>
        </div>
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
        className={`slots-page__footer${spinError || demoAvailability === 'unavailable' ? ' slots-page__footer--error' : ''}`}
        aria-live="polite"
      >
        {demoAvailabilityMessage
          ?? spinError
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

      <WinHelpDialog
        isOpen={isHelpOpen}
        closeButtonRef={helpCloseButtonRef}
        symbolSet={symbolSet}
        help={help}
        onClose={() => setIsHelpOpen(false)}
      />

      <InsufficientBalanceDialog
        isOpen={isReloadPromptOpen}
        closeButtonRef={reloadPromptCloseButtonRef}
        selectedWager={selectedWager}
        balance={balance}
        demoMode={demoMode}
        onClose={() => setIsReloadPromptOpen(false)}
      />

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
