import type { CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { ForgeCoin } from '../../components/ForgeCreditAmount'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../games/slots/shared/mascot/MascotCompanion'
import { AudioSettingsDialog } from './components/AudioSettingsDialog'
import { CollectionProgressDisplay } from './components/CollectionProgressDisplay'
import { SlotMachine } from './components/SlotMachine'
import { SlotPlayGuide } from './components/SlotPlayGuide'
import { SlotSymbol } from './components/SlotSymbol'
import { SpinButton } from './components/SpinButton'
import { SymbolValueGuide } from './components/SymbolValueGuide'
import { TreasureGemFlyover } from './components/TreasureGemFlyover'
import { getSpecialRoundLabel } from './config/slotFeatures'
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
    bestWin,
    cabinetTheme,
    canAffordSelectedWager,
    changeWager,
    collectionAwardPresentation,
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
    heldCompletedCollectionId,
    isAutoSpinning,
    isFreeSpinBadgePopping,
    isHelpOpen,
    isDemoSpinDisabled,
    isReloadPromptOpen,
    isSpecialGameActive,
    isSpecialSpinDelayActive,
    isSettingsOpen,
    isSpinning,
    isStopRequested,
    lastEnergyAwarded,
    lastEnergyMultiplierApplied,
    lastFreeSpinsAwarded,
    lastWin,
    lastSpinOutcome,
    mascotActionKey,
    mascotPhase,
    mascotSet,
    mascotSuccessFrame,
    moneyGrabPresentation,
    pageBackdropStyle,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    resultAtmosphereId,
    reloadPromptCloseButtonRef,
    selectedWager,
    sealFlyover,
    sealImpactId,
    specialRoundGemCount,
    specialRoundWinnings,
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
    winningPaylineCount,
    winningPositions,
  } = controller
  const collectionFeature = featureSet.collections
  const energyFeature = featureSet.energy
  const moneyGrabFeature = featureSet.moneyGrab
  const specialRound = featureSet.specialRound
  const specialRoundLabel = specialRound
    ? getSpecialRoundLabel(specialRound, isSpecialGameActive ? controller.freeSpinFeatureMode : null)
    : null
  const completedCollection = collectionAwardPresentation && collectionFeature
    ? collectionFeature.entries.find((entry) => entry.id === collectionAwardPresentation.collectionId)
    : undefined
  const completedFeatureDetail = specialRound && collectionAwardPresentation?.featureMode
    ? specialRound.activeModes[collectionAwardPresentation.featureMode] ?? null
    : null
  const specialControlsActive = isSpecialGameActive || collectionAwardPresentation !== null
  const isPiratesFortune = cabinetTheme.id === 'pirates-fortune-moonlit-cove-v1'
  const completedChestImage = completedCollection?.containerFillImages?.[
    (completedCollection.containerFillImages?.length ?? 1) - 1
  ] ?? completedCollection?.containerImage ?? collectionFeature?.containerImage
  const topbarStyle = cabinetTheme.topbar
    ? ({
        '--slot-topbar-background': cabinetTheme.topbar.background,
        '--slot-topbar-border-color': cabinetTheme.topbar.borderColor,
        '--slot-topbar-shadow-color': cabinetTheme.topbar.shadowColor,
        '--slot-topbar-accent-color': cabinetTheme.topbar.accentColor,
      } as CSSProperties)
    : undefined
  const pageStyle = {
    ...pageBackdropStyle,
    ...topbarStyle,
  }

  return (
    <div
      className={slotsPageClassName}
      style={pageStyle}
      data-slot-theme={cabinetTheme.id}
      data-slot-celebration={cabinetTheme.celebrationEffect}
      data-special-round={specialRound?.id}
      data-special-earn-style={specialRound?.earnStyle}
    >
      <header className="slots-page__topbar">
        <div className="slots-page__brand-cluster">
          <nav className="slots-page__navigation" aria-label="Game navigation">
            <a
              className="slots-page__brand"
              href="/"
              aria-label="Return to the Fortune Forge landing page"
            >
              <span className="slots-page__brand-name">Fortune Forge</span>
            </a>
            <a
              className="slots-page__other-games"
              href={demoMode ? '/demo' : '/games'}
              onClick={() => setIsAutoSpinning(false)}
            >
              Other games
            </a>
          </nav>
          <div className="slots-page__game-identity">
            <span>{cabinetTheme.eyebrow}</span>
            <h1>{cabinetTheme.title}</h1>
            <small>{cabinetTheme.subtitle}</small>
          </div>
        </div>
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
                  aria-label={`Balance: ${formatRand(balance)}. Open recharge.`}
                  onClick={() => setIsAutoSpinning(false)}
                >
                  <ForgeCoin className="slots-page__purchase-credits-coin" />
                  <span>{formatRand(balance)}</span>
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
              <span aria-hidden="true">?</span>
              <span className="slots-page__help-button-label">How to win</span>
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
          {(collectionFeature || energyFeature || isSpecialGameActive) && (
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
                    containerImage={seal.containerImage ?? collectionFeature.containerImage}
                    containerFillImages={seal.containerFillImages}
                    displayCount={
                      isSpecialGameActive && heldCompletedCollectionId === collection.sealId
                        ? collection.requiredCount
                        : undefined
                    }
                    isCelebrating={collectionAwardPresentation?.collectionId === collection.sealId}
                    presentation={collectionFeature.presentation ?? 'seal-pile'}
                    showCount={collectionFeature.presentation !== 'gem-hoard'}
                    statusDetail={
                      isSpecialGameActive &&
                      heldCompletedCollectionId === collection.sealId &&
                      specialRoundGemCount > 0
                        ? `${specialRoundGemCount} gems banked`
                        : undefined
                    }
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

            {isSpecialGameActive && specialRound && (
              <aside className="slots-page__special-game-tally" aria-live="polite">
                <span>{specialRoundLabel ?? specialRound.title} winnings</span>
                <strong>{formatRand(specialRoundWinnings)}</strong>
                <small>
                  {isSpecialSpinDelayActive
                    ? 'Next spin starts shortly — press Spin to continue now.'
                    : isSpinning
                      ? 'Special spin underway.'
                      : 'Your special-game total.'}
                </small>
              </aside>
            )}
          </div>
          )}

          <div className={`slots-page__cabinet-spotlight${collectionAwardPresentation || isSpecialGameActive ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
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
          </div>

          <div className="slots-page__playbar" aria-label="Balance, wager, and spin controls">
            <SymbolValueGuide
              symbolSet={symbolSet}
              showSidePanel={isPiratesFortune}
              sidePanelClassName={isPiratesFortune ? 'symbol-value-guide--pirates' : undefined}
              showValueTokens={Boolean(moneyGrabFeature)}
            />

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

            <div
              className={`slots-page__spin-controls${specialControlsActive ? ' slots-page__spin-controls--special' : ''}`}
              aria-label={specialControlsActive ? 'Special game spin control' : 'Spin, autospin, and wager controls'}
            >
              <div className="slots-page__spin-stack">
                <div className="slots-page__spin-button-shell">
                  <SpinButton
                    disabled={isDemoSpinDisabled}
                    isSpinning={isSpinning}
                    isStopRequested={isStopRequested}
                    onSpin={handleSpinButtonClick}
                    variant={isPiratesFortune ? 'pirate-helm' : 'default'}
                  />
                  {showFreeSpinBadge && !specialControlsActive && (
                    <span
                      className={`slots-page__free-spin-badge${isFreeSpinBadgePopping ? ' slots-page__free-spin-badge--popping' : ''}`}
                      aria-hidden="true"
                    >
                      <strong>{specialRoundLabel ?? 'Free spin!'}</strong>
                      {freeSpinsRemaining > 1 && <span>×{freeSpinsRemaining}</span>}
                    </span>
                  )}
                </div>
                {!specialControlsActive && (
                  <>
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
                  </>
                )}
                {!specialControlsActive && (
                  <div className="slots-page__wager-control" role="group" aria-label={`Wager: ${formatRand(activeWagerDisplay)}`}>
                    <button
                      className="slots-page__wager-nudge"
                      type="button"
                      aria-label="Decrease wager"
                      disabled={isSpinning || isAutoSpinning || freeSpinsRemaining > 0 || wagerIndex === 0}
                      onClick={() => changeWager(-1)}
                    >
                      <svg viewBox="0 0 100 100" aria-hidden="true"><path d="M28 50H72" /></svg>
                    </button>
                    <output className="slots-page__wager-display" aria-live="polite">
                      <span>Bet</span>
                      <strong>{formatRand(activeWagerDisplay)}</strong>
                    </output>
                    <button
                      className="slots-page__wager-nudge"
                      type="button"
                      aria-label="Increase wager"
                      disabled={isSpinning || isAutoSpinning || freeSpinsRemaining > 0 || wagerIndex === wagerOptions.length - 1}
                      onClick={() => changeWager(1)}
                    >
                      <svg viewBox="0 0 100 100" aria-hidden="true"><path d="M28 50H72" /><path d="M50 28V72" /></svg>
                    </button>
                  </div>
                )}
              </div>
            </div>

            {isPiratesFortune && createPortal(
              <SlotPlayGuide
                bestWin={bestWin}
                collections={collectionFeature}
                help={help}
                lastWin={lastWin}
                selectedWager={activeWagerDisplay}
                specialRound={specialRound}
                symbolSet={symbolSet}
                winningPaylineCount={winningPaylineCount}
                onOpenHelp={() => {
                  setIsAutoSpinning(false)
                  setIsHelpOpen(true)
                }}
              />,
              document.body,
            )}
          </div>
          {specialRound?.showStatusPanel !== false && specialRound && (
            <aside className="slots-page__special-round" aria-live="polite">
              <span>{isSpecialGameActive ? 'Special round active' : 'Earn a special round'}</span>
              <strong>{specialRoundLabel}</strong>
              <p>{isSpecialGameActive
                ? 'The special game spins on its own. Press Spin to skip the next delay.'
                : specialRound.earnHint ?? specialRound.earnLabel}</p>
            </aside>
          )}
        </div>
        </div>
      </main>

      {lastSpinOutcome && lastSpinOutcome.kind !== 'loss' && cabinetTheme.celebrationEffect && (
        <div
          key={resultAtmosphereId}
          className="slots-page__result-atmosphere"
          data-celebration={cabinetTheme.celebrationEffect}
          data-outcome={lastSpinOutcome.kind}
          aria-hidden="true"
        >
          <span />
          <span />
        </div>
      )}

      {isSpecialGameActive && <div className="slots-page__special-game-dimmer" aria-hidden="true" />}

      {collectionAwardPresentation && (
          <section
            className="slots-page__collection-award-dialog"
            role="status"
            aria-live="assertive"
            aria-labelledby="collection-award-title"
          >
            {completedChestImage && (
              <img className="slots-page__collection-award-chest" src={completedChestImage} alt="" aria-hidden="true" />
            )}
            <span className="slots-page__collection-award-kicker">Treasure chest filled</span>
            <h2 id="collection-award-title">{completedCollection?.label ?? 'Gem collection'} complete</h2>
            <p>
              {collectionAwardPresentation.freeSpins} {specialRound?.title ?? 'free game'}
              {collectionAwardPresentation.freeSpins === 1 ? ' is' : 's are'} now loaded.
              {' '}The chest stays full while any new gems are banked for the next run.
            </p>
            {completedFeatureDetail && (
              <p>Every free game: {completedFeatureDetail}.</p>
            )}
            <p className="slots-page__collection-award-hint">Press the Spin button to begin.</p>
          </section>
      )}

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
        collectionFeature.presentation === 'gem-hoard' ? (
          <TreasureGemFlyover
            flyover={sealFlyover}
            image={getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).image}
          />
        ) : (
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
        )
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
            `slots-page__win-award--${winAwardFlyover.winTier}`,
            `slots-page__win-award--${winAwardFlyover.tier}`,
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
          <span>{lastSpinOutcome?.title ?? (winAwardFlyover.winTier === 'big'
            ? 'Big win'
            : winAwardFlyover.winTier === 'great'
              ? 'Great win'
              : 'Win')}</span>
          <strong>+{formatRand(winAwardFlyover.displayAmount)}</strong>
        </div>
      )}

      <footer
        className={[
          'slots-page__footer',
          spinError || demoAvailability === 'unavailable' ? 'slots-page__footer--error' : '',
          lastSpinOutcome ? `slots-page__footer--${lastSpinOutcome.kind}` : '',
        ].filter(Boolean).join(' ')}
        aria-live={isAutoSpinning ? 'off' : 'polite'}
        aria-atomic="true"
      >
        {demoAvailabilityMessage
          ?? spinError
          ?? (isSpinning
            ? spinStage === 'requesting'
              ? `${cabinetTheme.title} reels are spinning`
              : 'Reels are landing…'
            : lastFreeSpinsAwarded > 0
              ? `${lastFreeSpinsAwarded} ${help.freeGames?.awardLabel ?? 'free games'} won — ${freeSpinsRemaining} ready`
            : lastSpinOutcome
              ? `${lastSpinOutcome.title}${lastSpinOutcome.awardRand > 0 ? ` · ${formatRand(lastSpinOutcome.awardRand)} credited` : ''} · ${lastSpinOutcome.nextAction}`
              : lastEnergyMultiplierApplied
                ? 'Energy boost ×1.5 — meter reset'
              : lastEnergyAwarded > 0
                ? 'Energy collected — spin again when ready.'
              : useFreeGameForNextSpin
                ? `${freeSpinsRemaining} free game${freeSpinsRemaining === 1 ? '' : 's'} ready`
            : !canAffordSelectedWager
              ? 'Choose a smaller wager'
              : 'Choose a wager, then spin when ready.')}
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
