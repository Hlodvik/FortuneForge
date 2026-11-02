import type { CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import reelRichesMarlin from '../../assets/slots/games/reel-riches/optimized/reel-riches-blue-marlin-v2.webp'
import reelRichesBonusLake from '../../assets/slots/games/reel-riches/optimized/reel-riches-moonlit-bonus-v1.webp'
import { GameOutcomeBanner } from '../../components/GameOutcomeBanner'
import { SiteNavbar } from '../../components/SiteNavbar'
import { GameSupportControls } from '../../components/GameSupportControls'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { PiratesSpinControls } from './components/PiratesSpinControls'
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
import { getSlotOutcomePresentation } from './presentation/spinPresentation'
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
    balanceActionHref,
    balanceRecoveryHref,
    balanceRecoveryLabel,
    demoAvailability,
    demoAvailabilityMessage,
    energyBalance,
    energyFlyover,
    energyImpactKey,
    energyMeterCapacity,
    energyMeterRef,
    featureSet,
    freeSpinsRemaining,
    handleSpinButtonClick,
    hasCompletedSpin,
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
    lastSpinOutcomeKey,
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
    ? getSpecialRoundLabel(specialRound, collectionAwardPresentation.featureMode)
    : null
  const isPiratesFortune = cabinetTheme.id === 'pirates-fortune-moonlit-cove-v1'
  const isReelRiches = cabinetTheme.id === 'reel-riches-cabinet-v1'
  const isWukong = cabinetTheme.id === 'wukong-celestial-arcade-v1'
  const reelRichesIntroVisible = isReelRiches && freeSpinsRemaining > 0 &&
    !isSpecialGameActive && !isSpinning
  const reelRichesBonusVisible = isReelRiches && (reelRichesIntroVisible || isSpecialGameActive)
  const reelRichesFeatureLabel = isReelRiches && specialRound
    ? getSpecialRoundLabel(specialRound, controller.freeSpinFeatureMode)
    : null
  const [reelRichesFeatureName, reelRichesFeatureDetail] =
    reelRichesFeatureLabel?.split(' · ', 2) ?? ['Trophy Catch', undefined]
  const specialControlsActive = isSpecialGameActive || collectionAwardPresentation !== null ||
    reelRichesIntroVisible
  const completedChestImage = completedCollection?.containerImage ?? collectionFeature?.containerImage
  const completedGemImage = isPiratesFortune && completedCollection
    ? getSlotSymbolDefinition(symbolSet, completedCollection.symbol).image
    : null
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
    ...(reelRichesBonusVisible
      ? { '--slot-page-backdrop': `url("${reelRichesBonusLake}")` }
      : {}),
  } as CSSProperties
  const activeCabinetTheme = reelRichesBonusVisible
    ? { ...cabinetTheme, visualsBackdropImage: reelRichesBonusLake }
    : cabinetTheme
  const bestWinMatch = bestWin?.matches.reduce((best, candidate) => (
    candidate.amountPoints > best.amountPoints ? candidate : best
  ))
  const slotOutcome = getSlotOutcomePresentation({
    activeWager: activeWagerDisplay,
    bestPayline: bestWin,
    bestSymbolLabel: bestWinMatch
      ? getSlotSymbolDefinition(symbolSet, bestWinMatch.match.symbolId).label
      : null,
    canAffordSelectedWager,
    demoAvailabilityMessage,
    energyLabel: energyFeature?.label ?? null,
    gameTitle: cabinetTheme.title,
    hasCompletedSpin,
    isSpinning,
    lastEnergyAwarded,
    lastEnergyMultiplierApplied,
    lastFreeSpinsAwarded,
    lastWin,
    spinError,
    spinStage,
    freeSpinsRemaining,
    useFreeGameForNextSpin,
  })
  // Moving reels and the Spin/Stop control already communicate an ordinary
  // spin. Reserve this extra panel for wins, features, and actionable errors.
  const showOutcomeBanner = slotOutcome.tone !== 'neutral' && !reelRichesIntroVisible &&
    !(isReelRiches && isSpecialGameActive && slotOutcome.label === 'Free game ready')
  const pirateUtilityButtons = <div className="slots-page__pirate-utility-buttons" role="group" aria-label="Game options">
    <button className="slots-page__help-button" type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={isHelpOpen}
      onClick={() => { setIsAutoSpinning(false); setIsHelpOpen(true) }}>
      <span aria-hidden="true">?</span>
    </button>
    <button className="slots-page__settings-button" type="button" aria-label="Open settings" aria-haspopup="dialog" aria-expanded={isSettingsOpen}
      onClick={() => { setIsAutoSpinning(false); setIsSettingsOpen(true) }}>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" />
        <circle cx="12" cy="11.5" r="3" />
      </svg>
    </button>
  </div>

  return (
    <div
      className={slotsPageClassName}
      style={pageStyle}
      data-slot-theme={cabinetTheme.id}
      data-slot-celebration={cabinetTheme.celebrationEffect}
      data-special-round={specialRound?.id}
      data-special-earn-style={specialRound?.earnStyle}
      data-reel-riches-intro={reelRichesIntroVisible ? 'true' : undefined}
      data-reel-riches-bonus={reelRichesBonusVisible ? 'true' : undefined}
      data-reel-riches-win={isReelRiches && isSpecialGameActive ? lastSpinOutcome?.kind : undefined}
    >
      <SiteNavbar
        mode="game"
        theme="slots"
        account={controller.account}
        authenticated={balanceActionHref !== null}
        reportTitle={cabinetTheme.title}
        balance={balance}
        onNavigate={() => setIsAutoSpinning(false)}
        actions={<>
          {balanceActionHref !== null && <PaymentAlertsMenu />}
          {!isPiratesFortune && <><button
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
            </button></>}
        </>}
      />

      <main className="slots-page__main">
        <GameSupportControls account={controller.account} gameTitle={cabinetTheme.title} />
        <h1 className="slots-page__accessible-title">{cabinetTheme.title}</h1>
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
                    displayCount={
                      isSpecialGameActive && heldCompletedCollectionId === collection.sealId
                        ? collection.requiredCount
                        : undefined
                    }
                    isCelebrating={collectionAwardPresentation?.collectionId === collection.sealId}
                    presentation={collectionFeature.presentation ?? 'seal-pile'}
                    showCount={collectionFeature.presentation !== 'gem-hoard'}
                    statusDetail={
                      !isPiratesFortune && isSpecialGameActive &&
                      heldCompletedCollectionId === collection.sealId &&
                      specialRoundGemCount > 0
                        ? `${specialRoundGemCount} ${isReelRiches ? 'catches' : 'gems'} banked`
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
                  {isWukong && (
                    <span className="slots-page__energy-milestones">
                      {[25, 50, 75, 100].map((milestone) => (
                        <i
                          className={energyBalance / energyMeterCapacity * 100 >= milestone ? 'is-lit' : ''}
                          key={milestone}
                          style={{ '--energy-milestone': `${milestone}%` } as CSSProperties}
                        />
                      ))}
                    </span>
                  )}
                </span>
                <strong>{creditFormatter.format(energyBalance)}/{energyMeterCapacity}</strong>
              </span>
            </div>
            )}

            {isSpecialGameActive && specialRound && isReelRiches && (
              <aside className="reel-riches__bonus-hud" aria-live="polite">
                <span className="reel-riches__bonus-hud-title">Bonus round</span>
                <strong>{reelRichesFeatureName}</strong>
                {reelRichesFeatureDetail && <small className="reel-riches__bonus-hud-detail">{reelRichesFeatureDetail}</small>}
                <div className="reel-riches__bonus-hud-stats">
                  <span>Free casts left <b>{freeSpinsRemaining}</b></span>
                  <span>Feature win <b>{formatRand(specialRoundWinnings)}</b></span>
                </div>
              </aside>
            )}
            {isSpecialGameActive && specialRound && !isReelRiches && (
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
              cabinetTheme={activeCabinetTheme}
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

          <div className="slots-page__playbar" aria-label={isPiratesFortune ? 'Wager and spin controls' : 'Balance, wager, and spin controls'}>
            {!isPiratesFortune && <SymbolValueGuide
              symbolSet={symbolSet}
              showSidePanel={isPiratesFortune}
              sidePanelClassName={isPiratesFortune ? 'symbol-value-guide--pirates' : undefined}
              showValueTokens={Boolean(moneyGrabFeature)}
            />}

            {!isPiratesFortune && <div
              ref={creditTileRef}
              className="slots-page__balance slots-page__control-tile"
              aria-label={`Balance: ${formatRand(balance)}`}
            >
              <span className="slots-page__balance-label">Balance</span>
              <span className="slots-page__balance-line">
                <span className="slots-page__balance-value">{formatRand(balance)}</span>
              </span>
            </div>}

            {isPiratesFortune ? <PiratesSpinControls
              activeWager={activeWagerDisplay}
              disabled={isDemoSpinDisabled}
              freeSpinsRemaining={freeSpinsRemaining}
              isAutoSpinning={isAutoSpinning}
              isFreeSpinBadgePopping={isFreeSpinBadgePopping}
              isSpinning={isSpinning}
              isStopRequested={isStopRequested}
              onAutoSpinChange={(active) => { setSpinError(null); setIsAutoSpinning(active) }}
              onSelectWager={controller.selectWager}
              onSpin={handleSpinButtonClick}
              pointValueInCents={controller.pointValueInCents}
              showFreeSpinBadge={showFreeSpinBadge}
              specialControlsActive={specialControlsActive}
              specialRoundLabel={specialRoundLabel}
              wagerIndex={wagerIndex}
              wagerOptions={wagerOptions}
            /> : <div
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
                    variant={isPiratesFortune ? 'pirate-helm' : isWukong ? 'wukong-rune' : 'default'}
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
            </div>}

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
          <div className="slots-page__keyboard-help" aria-label="Keyboard controls">
            <span><kbd>Space</kbd> Spin / stop</span>
            <span><kbd>←</kbd><kbd>→</kbd> Wager</span>
            <span><kbd>M</kbd> Mute</span>
            <span><kbd>?</kbd> Rules</span>
          </div>
        </div>
        </div>
      </main>

      {isPiratesFortune && <SymbolValueGuide symbolSet={symbolSet} showSidePanel showValueTokens={Boolean(moneyGrabFeature)} />}
      {isPiratesFortune && pirateUtilityButtons}

      {lastSpinOutcome && lastSpinOutcome.kind !== 'loss' && cabinetTheme.celebrationEffect && !reelRichesBonusVisible && (
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

      {isReelRiches && isSpecialGameActive && lastSpinOutcome &&
        lastSpinOutcome.kind !== 'loss' && lastSpinOutcome.kind !== 'bonus' && (
          <div
            key={resultAtmosphereId}
            className="reel-riches__catch-burst"
            data-outcome={lastSpinOutcome.kind}
            aria-hidden="true"
          >
            {Array.from({ length: 14 }, (_, index) => <i key={index} />)}
          </div>
      )}

      {(isSpecialGameActive || reelRichesIntroVisible) && (
        <div className="slots-page__special-game-dimmer" aria-hidden="true" />
      )}

      {reelRichesIntroVisible && (
        <section className="reel-riches__bonus-intro" role="status" aria-live="polite">
          <img src={reelRichesMarlin} alt="" aria-hidden="true" />
          <div className="reel-riches__bonus-intro-copy">
            <span>Feature unlocked</span>
            <h2>{reelRichesFeatureName}</h2>
            <p>{freeSpinsRemaining} free casts are ready</p>
            {reelRichesFeatureDetail && <small>{reelRichesFeatureDetail}</small>}
            <b>Press Spin to cast</b>
          </div>
        </section>
      )}

      {collectionAwardPresentation && (
          <section
            className="slots-page__collection-award-dialog"
            role="status"
            aria-live="assertive"
            aria-labelledby="collection-award-title"
          >
            {completedGemImage ? (
              <span className="slots-page__collection-award-gems" aria-hidden="true">
                {Array.from({ length: 5 }, (_, index) => <img src={completedGemImage} alt="" key={index} />)}
              </span>
            ) : completedChestImage && (
              <img className="slots-page__collection-award-chest" src={completedChestImage} alt="" aria-hidden="true" />
            )}
            <span className="slots-page__collection-award-kicker">
              {isPiratesFortune ? 'Treasure feature unlocked' : isWukong ? 'Celestial seal awakened' : 'Collection complete'}
            </span>
            <h2 id="collection-award-title">{completedCollection?.label ?? 'Gem collection'} complete</h2>
            <p>
              {collectionAwardPresentation.freeSpins} {specialRound?.title ?? 'free game'}
              {collectionAwardPresentation.freeSpins === 1 ? ' is' : 's are'} now loaded.
              {' '}{isPiratesFortune
                ? 'Your gem pile glows while new gems build toward the next run.'
                : isWukong
                  ? 'The seal stays awakened while matching seals are banked for the next quest.'
                  : 'New collection pieces are banked for the next run.'}
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

      {showOutcomeBanner && (
        <footer
          className={`slots-page__footer${spinError || demoAvailability === 'unavailable' ? ' slots-page__footer--error' : ''}`}
        >
          <GameOutcomeBanner
            key={lastSpinOutcomeKey}
            className="slots-page__outcome"
            {...slotOutcome}
          />
        </footer>
      )}

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
        recoveryHref={balanceRecoveryHref}
        recoveryLabel={balanceRecoveryLabel}
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
