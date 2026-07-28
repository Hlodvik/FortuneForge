import type { CSSProperties } from 'react'
import { ForgeCoin } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../components/WukongCompanion'
import { AudioSettingsDialog, SlotMachine, SlotSymbol, SpinButton, SymbolValueGuide } from './components'
import { InsufficientBalanceDialog } from './InsufficientBalanceDialog'
import { shouldUseAnimatedSymbol } from './presentation/spinLifecycle'
import { creditFormatter, formatRand, sealLabels } from './slotPagePresentation'
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
    energyBalance,
    energyFlyover,
    energyImpactKey,
    energyMeterCapacity,
    energyMeterRef,
    freeSpinsRemaining,
    handleSpinButtonClick,
    helpCloseButtonRef,
    isAutoSpinning,
    isFreeSpinBadgePopping,
    isHelpOpen,
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
    pageBackdropStyle,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    reloadPromptCloseButtonRef,
    selectedWager,
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

      <WinHelpDialog
        isOpen={isHelpOpen}
        closeButtonRef={helpCloseButtonRef}
        symbolSet={symbolSet}
        onClose={() => setIsHelpOpen(false)}
      />

      <InsufficientBalanceDialog
        isOpen={isReloadPromptOpen}
        closeButtonRef={reloadPromptCloseButtonRef}
        selectedWager={selectedWager}
        balance={balance}
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
