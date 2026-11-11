import { ResponsiveSlotImage } from './components/ResponsiveSlotImage'
import { useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'



import { GameOutcomeBanner } from '../../components/GameOutcomeBanner'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../games/slots/shared/mascot/MascotCompanion'
import { AudioSettingsDialog } from './components/AudioSettingsDialog'
import { SidecarSpinControls } from '@fortuneforge/slot-client-core/components/SidecarSpinControls'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { CollectionProgressDisplay } from './components/CollectionProgressDisplay'
import { SlotMachine } from './components/SlotMachine'
import { SlotPlayGuide } from './components/SlotPlayGuide'
import { SlotSymbol } from './components/SlotSymbol'
import { SpinButton } from './components/SpinButton'
import { SymbolValueGuide } from './components/SymbolValueGuide'
import { getSpecialRoundLabel } from './config/slotFeatures'
import { getSlotSymbolDefinition, getSlotSymbolImageSrcSet } from './config/symbolSets'
import { InsufficientBalanceDialog } from './InsufficientBalanceDialog'
import { getSlotOutcomePresentation } from './presentation/spinPresentation'
import { shouldUseAnimatedSymbol } from './presentation/spinLifecycle'
import { creditFormatter, formatRand, getSlotSymbolValueLabel, slotPointsToRand } from './slotPagePresentation'
import type { SlotsPageController } from './useSlotsPageController'
import { WinHelpDialog } from './WinHelpDialog'
import './slotWinningPaylines.css'

const vaultMoneyFormatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const formatVaultMoney = (value: number) => `R${vaultMoneyFormatter.format(value)}`


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
    spinAvailability,
    spinAvailabilityMessage,
    energyBalance,
    energyFlyover,
    energyImpactKey,
    energyMeterCapacity,
    energyMeterRef,
    featureSet,
    freeSpinsRemaining,
    godsTumbleFrame,
    handleSpinButtonClick,
    hasCompletedSpin,
    helpCloseButtonRef,
    help,
    heldCompletedCollectionId,
    isAutoSpinning,
    isFreeSpinBadgePopping,
    isGodsTumbleBoard,
    isHelpOpen,
    isSpinServiceDisabled,
    energyAward,
    isEnergyAwardCompleting,
    isReloadPromptOpen,
    isSpecialGameActive,
    isSpecialSpinDelayActive,
    isSettingsOpen,
    isSpinning,
    isStopRequested,
    lastCollectorAward,
    lastEnergyAwarded,
    lastEnergyMultiplierApplied,
    lastFreeSpinsAwarded,
    lastSpinOutcomeKey,
    lastWin,
    lastSpinOutcome,
    lightningCollection,
    mascotActionKey,
    mascotPhase,
    mascotSet,
    mascotSuccessFrame,
    moneyGrabPresentation,
    pageBackdropStyle,
    presentation,
    prefersReducedMotion,
    reelMotion,
    reelStripStyle,
    rainbowBonusSummary,
    dismissRainbowBonusSummary,
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
    usesGodsTumble,
    visibleSealCollections,
    wagerIndex,
    wagerOptions,
    winAwardFlyover,
    winningPaylineCount,
    winningPaylines,
    winningPositions,
  } = controller
  const LightningCollectionOverlay = presentation?.LightningCollectionOverlay
  const TumbleAwardBadge = presentation?.TumbleAwardBadge
  const CollectionFlyover = presentation?.CollectionFlyover
  const specialBackdropImage = presentation?.specialBackdropImage
  const specialIntroImage = presentation?.specialIntroImage
  const collectionFeature = isGodsTumbleBoard && !useFreeGameForNextSpin ? undefined : featureSet.collections
  const godsTumbleAward = godsTumbleFrame?.phase === 'award' ? godsTumbleFrame.award : null
  const godsTumbleHighlights = godsTumbleAward?.positions ??
    (godsTumbleFrame?.phase === 'clear' ? godsTumbleFrame.cleared : undefined)
  const formatTumbleAmount = (points: number) => formatVaultMoney(slotPointsToRand(points, controller.pointValueInCents))
  const energyFeature = featureSet.energy
  const EnergyAwardMeter = energyFeature?.completionAward?.Meter
  const panelEnergy = EnergyAwardMeter ? undefined : energyFeature
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
  const isArcaneArchives = cabinetTheme.id === 'arcane-archives-cabinet-v1'







  const cabinetRef = useRef<HTMLDivElement>(null)
  const isWukong = cabinetTheme.id === 'wukong-celestial-arcade-v1'
  const usesSidecarControls = !isWukong && !isPiratesFortune && cabinetTheme.controlLayout !== 'integrated'
  const usesPiratesControls = isPiratesFortune || usesSidecarControls
  const activeWagerLabel = formatRand(activeWagerDisplay)
  const isRainbowRealm = cabinetTheme.id === 'rainbow-realm-fruit-arcade-v1'
  const paylineTrace = cabinetTheme.paylineTrace
  const usesPaylineTrace = isRainbowRealm || Boolean(paylineTrace)
  const tracedPaylines = useMemo(() => isRainbowRealm
    ? winningPaylines.filter(({ paylineId }) => paylineId >= 1 && paylineId <= help.paylineCount)
    : winningPaylines, [help.paylineCount, isRainbowRealm, winningPaylines])
  const hasPaylineTrace = usesPaylineTrace && tracedPaylines.length > 0
  const [activePaidPath, setActivePaidPath] = useState<ActivePaidPath | null>(null)
  const highlightedPositions = (paylineTrace) && hasCompletedSpin && hasPaylineTrace &&
    activePaidPath?.resultKey === lastSpinOutcomeKey ? activePaidPath.positions : winningPositions
  const showCollectionMeter = !isRainbowRealm
  const rainbowBonusSymbol = isRainbowRealm ? getSlotSymbolDefinition(symbolSet, 'FREE') : undefined
  const rainbowBonusImage = rainbowBonusSymbol?.responsiveImages?.[320] ?? rainbowBonusSymbol?.image
  const reelRichesIntroVisible = isReelRiches && freeSpinsRemaining > 0 &&
    !isSpecialGameActive && !isSpinning
  const reelRichesBonusVisible = isReelRiches && (reelRichesIntroVisible || isSpecialGameActive)
  const rainbowBonusVisible = isRainbowRealm &&
    (freeSpinsRemaining > 0 || isSpecialGameActive || rainbowBonusSummary !== null)
  const rainbowIntroVisible = rainbowBonusVisible && !isSpecialGameActive &&
    !isSpinning && collectionAwardPresentation === null && rainbowBonusSummary === null
  const rainbowFeatureLabel = isRainbowRealm && specialRound
    ? getSpecialRoundLabel(specialRound, controller.freeSpinFeatureMode)
    : null
  const reelRichesFeatureLabel = isReelRiches && specialRound
    ? getSpecialRoundLabel(specialRound, controller.freeSpinFeatureMode)
    : null
  const [reelRichesFeatureName, reelRichesFeatureDetail] =
    reelRichesFeatureLabel?.split(' · ', 2) ?? ['Trophy Catch', undefined]
  const specialControlsActive = isSpecialGameActive || collectionAwardPresentation !== null ||
    reelRichesIntroVisible || rainbowIntroVisible ||
    rainbowBonusSummary !== null
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
    ...(paylineTrace && {
      '--slot-payline-base': paylineTrace.color,
      '--slot-payline-glow': paylineTrace.glowColor,
      '--slot-payline-label': cabinetTheme.palette.text,
      '--slot-payline-label-outline': cabinetTheme.palette.shellBottom,
    }),
    ...(reelRichesBonusVisible && specialBackdropImage
      ? { '--slot-page-backdrop': `url("${specialBackdropImage}")` }
      : {}),
  } as CSSProperties
  const activeCabinetTheme = reelRichesBonusVisible && specialBackdropImage
    ? { ...cabinetTheme, visualsBackdropImage: specialBackdropImage }
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
    demoAvailabilityMessage: spinAvailabilityMessage,
    energyLabel: energyFeature?.label ?? null,
    gameTitle: cabinetTheme.title,
    hasCompletedSpin,
    isSpinning,
    lastEnergyAwarded: panelEnergy ? lastEnergyAwarded : 0,
    lastEnergyMultiplierApplied: Boolean(energyFeature) && lastEnergyMultiplierApplied,
    lastFreeSpinsAwarded,
    lastWin,
    spinError,
    spinStage,
    freeSpinsRemaining,
    useFreeGameForNextSpin,
    featureAward: lightningCollection
      ? { label: 'Lightning collection', amount: slotPointsToRand(lightningCollection.totalPoints, controller.pointValueInCents) }
      : moneyGrabFeature?.presentation === 'separate-award' && lastCollectorAward > 0
        ? { label: moneyGrabFeature.awardLabel, amount: lastCollectorAward }
        : undefined,
  })
  // Moving reels and the Spin/Stop control already communicate an ordinary
  // spin. Reserve this extra panel for wins, features, and actionable errors.
  const showOutcomeBanner = slotOutcome.tone !== 'neutral' && !reelRichesIntroVisible &&
    !rainbowIntroVisible && rainbowBonusSummary === null &&
    !((isReelRiches || isRainbowRealm) && isSpecialGameActive && slotOutcome.label === 'Free game ready') &&
    !(isGodsTumbleBoard && slotOutcome.tone === 'win')
  const utilityButtons = <div className="slots-page__utility-buttons" role="group" aria-label="Game options">
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
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" />
        <circle cx="12" cy="11.5" r="3" />
      </svg>
    </button>

  </div>
  const guideSymbols = symbolSet
  const symbolValueGuide = !isGodsTumbleBoard && <SymbolValueGuide
    symbolSet={guideSymbols}
    showSidePanel={usesPiratesControls}
    sidePanelClassName={isPiratesFortune ? 'symbol-value-guide--pirates' : undefined}
    showValueTokens={Boolean(moneyGrabFeature?.valueSymbolPrefix)}
    isOpen={controller.isSymbolGuideOpen}
    onOpenChange={controller.setIsSymbolGuideOpen}
  />

  return (
    <InGameShell
      account={controller.account ?? undefined}
      reportTitle={cabinetTheme.title}
      balance={balance}
      authenticated={balanceActionHref !== null}
      onNavigate={() => setIsAutoSpinning(false)}
      bodyClassName="in-game-shell__body--slots"
    >
    <div
      className={rainbowBonusVisible && !isSpecialGameActive
        ? `${slotsPageClassName} slots-page--special-game`
        : slotsPageClassName}
      style={pageStyle}
      data-slot-theme={cabinetTheme.id}
      data-slot-payline-trace={paylineTrace ? 'true' : undefined}
      data-slot-controls={usesSidecarControls ? 'sidecar' : undefined}
      data-slot-celebration={cabinetTheme.celebrationEffect}
      data-special-round={specialRound?.id}
      data-special-earn-style={specialRound?.earnStyle}
      data-reel-riches-intro={reelRichesIntroVisible ? 'true' : undefined}
      data-reel-riches-bonus={reelRichesBonusVisible ? 'true' : undefined}
      data-reel-riches-win={isReelRiches && isSpecialGameActive ? lastSpinOutcome?.kind : undefined}
      data-rainbow-intro={rainbowIntroVisible ? 'true' : undefined}
      data-rainbow-bonus={rainbowBonusVisible ? 'true' : undefined}




    >
      <main className="slots-page__main">
        <h1 className="slots-page__accessible-title">{cabinetTheme.title}</h1>
        {presentation?.mainWordmark && <img {...presentation.mainWordmark} alt="" />}
        <div className="slots-page__layout">
          <div className="slots-page__stage">
          {cabinetTheme.titleArtwork && (
            <img className="slots-page__title-art" {...cabinetTheme.titleArtwork} alt="" />
          )}
          {presentation?.stageWordmark && <img {...presentation.stageWordmark} alt="" />}
          {((showCollectionMeter && collectionFeature) || panelEnergy || isSpecialGameActive) && (
          <div className="slots-page__meter-stack">
            {showCollectionMeter && collectionFeature && (
            <div className="slots-page__seal-collections" aria-label={collectionFeature.ariaLabel}>
              {visibleSealCollections.map((collection) => {
                const seal = collectionFeature.entries.find((entry) => entry.id === collection.sealId)
                if (!seal) return null
                return (
                  <CollectionProgressDisplay
                    collection={collection}
                    definition={seal}
                    image={getSlotSymbolDefinition(symbolSet, seal.symbol).image}
                    imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, seal.symbol).responsiveImages)}
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

            {panelEnergy && (
            <div
              key={`energy-meter-${energyImpactKey}`}
              ref={energyMeterRef}
              className={`slots-page__energy-meter${energyImpactKey > 0 ? ' slots-page__energy-meter--impact' : ''}`}
              role="progressbar"
              aria-label={`${panelEnergy.label}: ${creditFormatter.format(energyBalance)}`}
              aria-valuemin={0}
              aria-valuemax={energyMeterCapacity}
              aria-valuenow={Math.min(energyMeterCapacity, energyBalance)}
            >
              <ResponsiveSlotImage imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, panelEnergy.symbol).responsiveImages)} src={getSlotSymbolDefinition(symbolSet, panelEnergy.symbol).image} alt="" aria-hidden="true" />
              <span className="slots-page__energy-copy">
                <span className="slots-page__energy-label">{panelEnergy.label}</span>
                <span className="slots-page__energy-track" aria-hidden="true">
                  <span
                    className="slots-page__energy-fill"
                    style={{ width: `${Math.min(100, energyBalance / energyMeterCapacity * 100)}%` }}
                  />
                  {!!panelEnergy.milestones?.length && (
                    <span className="slots-page__energy-milestones">
                      {panelEnergy.milestones.map((milestone) => (
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
                <span>{isRainbowRealm
                  ? `${specialRoundLabel ?? specialRound.title} · ${freeSpinsRemaining} bonus ${freeSpinsRemaining === 1 ? 'spin' : 'spins'} left`
                  : `${specialRoundLabel ?? specialRound.title} winnings`}</span>
                <strong>{formatRand(specialRoundWinnings)}</strong>
                <small>
                  {isRainbowRealm && rainbowFeatureLabel && rainbowFeatureLabel !== specialRound.title
                    ? rainbowFeatureLabel
                    : isRainbowRealm
                    ? 'Bonus winnings'
                    : isSpecialSpinDelayActive
                    ? 'Next spin starts shortly — press Spin to continue now.'
                    : isSpinning
                      ? 'Special spin underway.'
                      : 'Your special-game total.'}
                </small>
              </aside>
            )}
          </div>
          )}

          <div ref={cabinetRef} className={`slots-page__cabinet-spotlight${collectionAwardPresentation || isSpecialGameActive ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
            <SlotMachine
              cabinetTheme={activeCabinetTheme}
              reelCount={displayedReels.length}
              reelOverlay={lightningCollection && LightningCollectionOverlay ? (
                <LightningCollectionOverlay
                  collection={lightningCollection}
                  reducedMotion={prefersReducedMotion}
                />
              ) : godsTumbleAward && TumbleAwardBadge ? (
                <TumbleAwardBadge
                  key={godsTumbleAward.symbol}
                  positions={godsTumbleAward.positions}
                  columns={displayedReels.length}
                  rows={displayedReels[0].length}
                  amount={`+${formatTumbleAmount(godsTumbleAward.amountPoints)}`}
                />
              ) : undefined}
              renderReel={(reelIndex) => (
              <div
                className={`slot-reel__symbols slot-reel__symbols--${reelMotion[reelIndex]}`}
                style={reelStripStyle(reelIndex)}
              >
                {displayedReels[reelIndex].map((symbol, rowIndex) => {
                  const isTumbleHighlight = godsTumbleHighlights?.some(
                    (position) => position.reel === reelIndex && position.row === rowIndex,
                  )

                  const highlightOrder = highlightedPositions.findIndex(
                    (position) => position.reel === reelIndex && position.row === rowIndex,
                  )
                  const dropRows = godsTumbleFrame?.phase === 'drop'
                    ? godsTumbleFrame.dropRows[reelIndex]?.[rowIndex] ?? 0
                    : 0

                  const collectedValue = lightningCollection?.cells.find(
                    (position) => position.reel === reelIndex && position.row === rowIndex,
                  )
                  const tile = <SlotSymbol
                    key={`row-${rowIndex}`}
                    symbol={symbol}
                    symbolSet={symbolSet}
                    valueLabel={collectedValue
                      ? formatRand(slotPointsToRand(collectedValue.amountPoints, controller.pointValueInCents))
                      : getSlotSymbolValueLabel(
                        getSlotSymbolDefinition(symbolSet, symbol),
                        activeWagerDisplay,
                        isArcaneArchives && !symbol.startsWith('RAND_') ? controller.pointValueInCents : undefined,
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
                    highlighted={isTumbleHighlight ||
                      highlightOrder >= 0}
                    highlightOrder={highlightOrder}
                  />

                  if (!isTumbleHighlight && dropRows === 0) return tile
                  const phase = dropRows > 0 ? 'drop' : godsTumbleAward ? 'award' : 'clear'
                  return (
                    <div
                      key={`row-${rowIndex}`}
                      className={`gods-tumble__cell gods-tumble__cell--${phase}`}
                      style={dropRows > 0 ? { '--gods-tumble-rows': dropRows } as CSSProperties : undefined}
                    >
                      {tile}
                    </div>
                  )
                })}
              </div>
              )}
            />
            {usesPaylineTrace && <WinningPaylines
              boardSelector=".slots-page__cabinet-spotlight"
              reelsSelector=".slot-machine__reels"
              cellSelector=".slot-symbol[data-reel-index][data-row-index]"
              appearance={{
                classPrefix: 'slot-winning-paylines',
                variant: isRainbowRealm ? 'rainbow' : 'cabinet',
                steadyTrail: isRainbowRealm,
                steadyTrailClassName: isRainbowRealm ? 'rainbow-realm__steady-payline' : undefined,
                reducedMotionTrail: !isRainbowRealm,
                labelInset: isRainbowRealm ? 0 : 64,
                labelOffset: isRainbowRealm ? 0 : 16,
                labelMinimumY: isRainbowRealm ? 0 : 26,
                labelBaseline: isRainbowRealm ? 'central' : undefined,
              }}
              formatWinAmount={points => `+${formatVaultMoney(slotPointsToRand(points, controller.pointValueInCents))}`}
              paylines={tracedPaylines}
              resultKey={lastSpinOutcomeKey}
              visible={hasCompletedSpin}
              reducedMotion={prefersReducedMotion}
              onActivePathChange={paylineTrace ? setActivePaidPath : undefined}
            />}

          </div>

          {isGodsTumbleBoard && (
            <div className="gods-tumble__score" role="status" aria-atomic="true">
              {godsTumbleFrame ? (
                <>
                  <span>Tumble {godsTumbleFrame.step} · +{formatTumbleAmount(godsTumbleAward?.amountPoints ?? godsTumbleFrame.amountPoints)}</span>
                  <strong>Total {formatTumbleAmount(godsTumbleFrame.totalPoints)}</strong>
                </>
              ) : hasCompletedSpin && !isSpinning && lastWin > 0 ? (
                <strong className="gods-tumble__final-win">+{formatVaultMoney(lastWin)}!</strong>
              ) : null}
            </div>
          )}

          {EnergyAwardMeter && (
            <EnergyAwardMeter
              balance={energyBalance}
              capacity={energyMeterCapacity}
              meterRef={energyMeterRef}
              award={energyAward}
              completing={isEnergyAwardCompleting}
            />
          )}

          <div className="slots-page__playbar" aria-label={usesPiratesControls ? 'Wager and spin controls' : 'Balance, wager, and spin controls'}>
            {!usesPiratesControls && symbolValueGuide}

            {!usesPiratesControls && (
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
            )}

            {usesPiratesControls ? (
              <SidecarSpinControls
                activeWager={activeWagerDisplay}
                disabled={isSpinServiceDisabled || (usesSidecarControls && rainbowBonusSummary !== null)}
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
                wagerFirst={isRainbowRealm}
                wagerIndex={wagerIndex}
                wagerOptions={wagerOptions}
              />
            ) : <div
              className={`slots-page__spin-controls${specialControlsActive ? ' slots-page__spin-controls--special' : ''}`}
              aria-label={specialControlsActive ? 'Special game spin control' : 'Spin, autospin, and wager controls'}
            >
              <div className="slots-page__spin-stack">
                <div className="slots-page__spin-button-shell">
                  <SpinButton
                    disabled={isSpinServiceDisabled || rainbowBonusSummary !== null}
                    iconSrc={cabinetTheme.spinIconImage}
                    isSpinning={isSpinning}
                    isStopRequested={isStopRequested}
                    onSpin={handleSpinButtonClick}
                    variant={isPiratesFortune ? 'helm' : 'default'}
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
                      disabled={isSpinServiceDisabled}
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
                      <strong>{activeWagerLabel}</strong>
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
          {(balanceActionHref !== null || !usesPiratesControls) && <div className="slots-page__local-controls">
            <div className="slots-page__utility-controls" aria-label="Game options">
              {balanceActionHref !== null && <PaymentAlertsMenu />}
              {!usesPiratesControls && utilityButtons}
            </div>
          </div>}
        </div>
        </div>
      </main>

      {usesPiratesControls && symbolValueGuide}
      {usesPiratesControls && utilityButtons}

      {lastSpinOutcome && lastSpinOutcome.kind !== 'loss' && cabinetTheme.celebrationEffect && !reelRichesBonusVisible && (
        <div
          key={resultAtmosphereId}
          className="slots-page__result-atmosphere"
          data-celebration={cabinetTheme.celebrationEffect}
          data-outcome={lastSpinOutcome.kind}
          aria-hidden="true"
        >
          <span /><span />
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

      {(isSpecialGameActive || reelRichesIntroVisible || rainbowBonusVisible) && (
        <div className="slots-page__special-game-dimmer" aria-hidden="true" />
      )}







      {reelRichesIntroVisible && specialIntroImage && (
        <section className="reel-riches__bonus-intro" role="status" aria-live="polite">
          <img src={specialIntroImage} alt="" aria-hidden="true" />
          <div className="reel-riches__bonus-intro-copy">
            <span>Feature unlocked</span>
            <h2>{reelRichesFeatureName}</h2>
            <p>{freeSpinsRemaining} free casts are ready</p>
            {reelRichesFeatureDetail && <small>{reelRichesFeatureDetail}</small>}
            <b>Press Spin to cast</b>
          </div>
        </section>
      )}

      {rainbowIntroVisible && (
        <section className="rainbow-realm__bonus-intro" role="status" aria-live="polite">
          <img src={rainbowBonusImage} alt="" aria-hidden="true" />
          <div className="rainbow-realm__bonus-intro-copy">
            <span>{controller.freeSpinFeatureMode ? 'Orchard glass filled' : 'Rainbows landed'}</span>
            <h2>Orchard Rush</h2>
            <p>{freeSpinsRemaining} bonus spins are ready</p>
            {rainbowFeatureLabel && rainbowFeatureLabel !== specialRound?.title &&
              <small>{rainbowFeatureLabel}</small>}
            <b>Press Spin to enter</b>
          </div>
        </section>
      )}

      {rainbowBonusSummary && (
        <section
          className="rainbow-realm__bonus-intro rainbow-realm__bonus-summary"
          role="dialog"
          aria-modal="true"
          aria-labelledby="rainbow-bonus-summary-title"
          onKeyDown={(event) => {
            if (event.key === 'Tab') event.preventDefault()
            if (event.key === 'Escape') dismissRainbowBonusSummary()
          }}
        >
          <img src={rainbowBonusImage} alt="" aria-hidden="true" />
          <div className="rainbow-realm__bonus-intro-copy rainbow-realm__bonus-summary-copy">
            <span>Bonus round complete</span>
            <h2 id="rainbow-bonus-summary-title">Orchard Rush</h2>
            {rainbowBonusSummary.totalRand !== null ? (
              <p>Bonus win <strong>{formatRand(rainbowBonusSummary.totalRand)}</strong></p>
            ) : (
              <p>Your bonus round is complete.</p>
            )}
            <button type="button" autoFocus onClick={dismissRainbowBonusSummary}>Continue</button>
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
              {collectionFeature?.completionTitle ??
                (isPiratesFortune ? 'Treasure feature unlocked' : isWukong ? 'Celestial seal awakened' : isRainbowRealm ? 'Orchard glass filled' : 'Collection complete')}
            </span>
            <h2 id="collection-award-title">{completedCollection?.label ?? 'Gem collection'} complete</h2>
            <p>
              {collectionAwardPresentation.freeSpins} {specialRound?.title ?? 'free game'}
              {collectionAwardPresentation.freeSpins === 1 ? ' is' : 's are'} now loaded.
              {' '}{collectionFeature?.completionDetail ??
                (isPiratesFortune
                  ? 'Your gem pile glows while new gems build toward the next run.'
                  : 'New collection pieces are banked for the next run.')}
            </p>
            {completedFeatureDetail && (
              <p>Every free game: {completedFeatureDetail}.</p>
            )}
            <p className="slots-page__collection-award-hint">Press the Spin button to begin.</p>
          </section>
      )}

      {energyFlyover && energyFeature && (
        <ResponsiveSlotImage
          key={energyFlyover.id}
          className="slots-page__energy-flyover"
          imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, energyFeature.symbol).responsiveImages)} src={getSlotSymbolDefinition(symbolSet, energyFeature.symbol).image}
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
        collectionFeature.presentation === 'gem-hoard' && CollectionFlyover ? (
          <CollectionFlyover
            flyover={sealFlyover}
            image={getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).image}
                    imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).responsiveImages)}
          />
        ) : (
          <ResponsiveSlotImage
            key={sealFlyover.id}
            className={`slots-page__seal-flyover slots-page__seal-flyover--${sealFlyover.collectionId}`}
            data-seal-id={sealFlyover.collectionId}
            imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).responsiveImages)} src={getSlotSymbolDefinition(symbolSet, sealFlyover.symbol).image}
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
                <ResponsiveSlotImage imageSrcSet={getSlotSymbolImageSrcSet(definition.responsiveImages)} src={definition.image} alt="" />
              </div>
            )
          })}

          <ResponsiveSlotImage
            className="slots-page__money-grab-paw"
            imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).responsiveImages)} src={getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).image}
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
            <ResponsiveSlotImage imageSrcSet={getSlotSymbolImageSrcSet(getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).responsiveImages)} src={getSlotSymbolDefinition(symbolSet, moneyGrabFeature.collectorSymbol).image} alt="" />
            <span>
              <small>{moneyGrabFeature.awardLabel}</small>
              <strong>+{formatRand(moneyGrabPresentation.amount)}</strong>
            </span>
          </div>
        </div>
      )}

      {winAwardFlyover && !hasPaylineTrace && (
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
          className={`slots-page__footer${spinError || spinAvailability === 'unavailable' ? ' slots-page__footer--error' : ''}`}
        >
          <GameOutcomeBanner
            key={lastSpinOutcomeKey}
            className="slots-page__outcome"
            {...slotOutcome}
          />
        </footer>
      )}

      <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
          <span><kbd>Space</kbd> Spin / stop</span>
          <span><kbd>←</kbd><kbd>→</kbd> Wager</span>
          <button className="slots-page__mute-shortcut" type="button"
            aria-pressed={audioPreferences.mode === 'muted'} onClick={toggleMuted}>
            <kbd aria-hidden="true">M</kbd> Mute
          </button>
          <span><kbd>?</kbd> Rules</span>
      </div>

      <AudioSettingsDialog
        musicCredit={cabinetTheme.id === 'gods-of-olympus-cabinet-v1' ? <>
            <a href="https://opengameart.org/content/secret-sanctum" target="_blank" rel="noopener noreferrer">Secret Sanctum</a> was composed, performed, mixed and mastered by Viktor Kraus, using orchestral samples from the Vienna Symphonic Library. Licensed under <a href="https://creativecommons.org/licenses/by/3.0/" target="_blank" rel="noopener noreferrer">CC BY 3.0</a>. Converted to Ogg Vorbis.
          </> : undefined}
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
        symbolSet={guideSymbols}
        help={help}
        onClose={() => setIsHelpOpen(false)}
      >
        {usesGodsTumble && !useFreeGameForNextSpin && !isSpecialGameActive ? (
          <>
            <p className="win-help__intro">Match eight or more of the same symbol anywhere on the reels.</p>
            <p className="win-help__intro">Matching symbols clear, the symbols above fall into place, and new ones drop in. Each new match adds to the same spin’s win.</p>
          </>
        ) : undefined}
      </WinHelpDialog>

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
    </InGameShell>
  )
}
