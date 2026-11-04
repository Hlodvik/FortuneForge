import { ResponsiveSlotImage } from './components/ResponsiveSlotImage'
import { useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import reelRichesMarlin from '../../assets/slots/games/reel-riches/optimized/reel-riches-blue-marlin-v2.webp'
import reelRichesBonusLake from '../../assets/slots/games/reel-riches/optimized/reel-riches-moonlit-bonus-v1.webp'
import rainbowRealmWordmark from '../../assets/slots/games/rainbow-realm/optimized/rainbow-realm-wordmark-v1.webp'
import arcaneArchivesWordmark from '../../assets/slots/games/arcane-archives/optimized/arcane-archives-wordmark-v1.webp'
import { ArcaneLightningCollection } from '../../games/slots/arcaneArchives/ArcaneLightningCollection'
import '../../games/slots/arcaneArchives/arcaneArchivesWordmark.css'
import '../../games/slots/godsOfOlympus/godsTumble.css'
import { GodsTumbleAwardBadge } from '../../games/slots/godsOfOlympus/GodsTumbleAwardBadge'
import { NeonSpecialPlaysIntro } from '../../games/slots/neonNights/NeonSpecialPlaysIntro'
import { NeonRoundSummary } from '../../games/slots/neonNights/NeonRoundSummary'
import { GameOutcomeBanner } from '../../components/GameOutcomeBanner'
import { InGameShell } from '../../components/InGameShell'
import { PaymentAlertsMenu } from '../../components/PaymentAlertsMenu'
import { MascotCompanion } from '../../games/slots/shared/mascot/MascotCompanion'
import { AudioSettingsDialog } from './components/AudioSettingsDialog'
import { PiratesSpinControls } from './components/PiratesSpinControls'
import { MidnightWinningPaylines, type ActivePaidPath } from '../../games/slots/dragonHoard/MidnightWinningPaylines'
import { CollectionProgressDisplay } from './components/CollectionProgressDisplay'
import { SlotMachine } from './components/SlotMachine'
import { SlotPlayGuide } from './components/SlotPlayGuide'
import { SlotSymbol } from './components/SlotSymbol'
import { SpinButton } from './components/SpinButton'
import { SymbolValueGuide } from './components/SymbolValueGuide'
import { TreasureGemFlyover } from './components/TreasureGemFlyover'
import { getSpecialRoundLabel } from './config/slotFeatures'
import { getSlotSymbolDefinition, getSlotSymbolImageSrcSet } from './config/symbolSets'
import { InsufficientBalanceDialog } from './InsufficientBalanceDialog'
import { getSlotOutcomePresentation } from './presentation/spinPresentation'
import { shouldUseAnimatedSymbol } from './presentation/spinLifecycle'
import { creditFormatter, formatRand, getSlotSymbolValueLabel, slotPointsToRand } from './slotPagePresentation'
import type { SlotsPageController } from './useSlotsPageController'
import { WinHelpDialog } from './WinHelpDialog'
import { midnightVaultGuideForModel } from '../../games/slots/dragonHoard/math'

const vaultMoneyFormatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const formatVaultMoney = (value: number) => `R${vaultMoneyFormatter.format(value)}`
const neonAdminPreviewSymbols = [
  { symbol: '6', label: 'Sports car' },
  { symbol: 'POWER', label: 'Disco ball' },
  { symbol: 'SEAL_RAND', label: 'Arcade cabinet' },
] as const

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
  const isMidnightVault = cabinetTheme.id === 'dragon-hoard-cabinet-v1'
  const isArcaneArchives = cabinetTheme.id === 'arcane-archives-cabinet-v1'
  const isNeonNights = cabinetTheme.id === 'neon-nights-cabinet-v1'
  const neonSpecialIntro = isNeonNights ? controller.neonSpecialIntro : null
  const neonRoundSummary = isNeonNights ? controller.neonRoundSummary : null
  const isNeonArcadeExpanding = isNeonNights && controller.isNeonArcadeExpanding
  const isNeonArcadePaidPlay = isNeonNights && controller.isNeonArcadePaidPlay
  const cabinetRef = useRef<HTMLDivElement>(null)
  const isWukong = cabinetTheme.id === 'wukong-celestial-arcade-v1'
  const usesSidecarControls = !isMidnightVault && !isWukong && !isPiratesFortune && cabinetTheme.controlLayout !== 'integrated'
  const usesPiratesControls = isPiratesFortune || usesSidecarControls
  const activeWagerLabel = isMidnightVault
    ? `R${new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(activeWagerDisplay)}`
    : formatRand(activeWagerDisplay)
  const midnightVaultBonusVisible = isMidnightVault && (freeSpinsRemaining > 0 || isSpecialGameActive)
  const isRainbowRealm = cabinetTheme.id === 'rainbow-realm-fruit-arcade-v1'
  const paylineTrace = cabinetTheme.paylineTrace
  const usesPaylineTrace = isMidnightVault || isNeonNights || isRainbowRealm || Boolean(paylineTrace)
  const tracedPaylines = useMemo(() => isRainbowRealm
    ? winningPaylines.filter(({ paylineId }) => paylineId >= 1 && paylineId <= help.paylineCount)
    : winningPaylines, [help.paylineCount, isRainbowRealm, winningPaylines])
  const hasPaylineTrace = usesPaylineTrace && tracedPaylines.length > 0
  const [activePaidPath, setActivePaidPath] = useState<ActivePaidPath | null>(null)
  const highlightedPositions = (isNeonNights || paylineTrace) && hasCompletedSpin && hasPaylineTrace &&
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
    reelRichesIntroVisible || midnightVaultBonusVisible || rainbowIntroVisible ||
    rainbowBonusSummary !== null || isNeonArcadeExpanding
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
    bonusName: isMidnightVault || isNeonNights ? specialRound?.title : undefined,
    featureAward: lightningCollection
      ? { label: 'Lightning collection', amount: slotPointsToRand(lightningCollection.totalPoints, controller.pointValueInCents) }
      : moneyGrabFeature?.presentation === 'separate-award' && lastCollectorAward > 0
        ? { label: moneyGrabFeature.awardLabel, amount: lastCollectorAward }
        : undefined,
  })
  // Moving reels and the Spin/Stop control already communicate an ordinary
  // spin. Reserve this extra panel for wins, features, and actionable errors.
  const showOutcomeBanner = slotOutcome.tone !== 'neutral' && !reelRichesIntroVisible &&
    !rainbowIntroVisible && !neonSpecialIntro && rainbowBonusSummary === null &&
    !((isReelRiches || isRainbowRealm) && isSpecialGameActive && slotOutcome.label === 'Free game ready') &&
    !(isNeonNights && (slotOutcome.tone === 'win' || slotOutcome.tone === 'milestone')) &&
    !(isGodsTumbleBoard && slotOutcome.tone === 'win') &&
    (!isMidnightVault || slotOutcome.tone === 'loss')
  const utilityButtons = <div className={`slots-page__utility-buttons${usesPiratesControls ? ' slots-page__utility-buttons--pirates' : ''}`} role="group" aria-label="Game options">
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
    {isNeonNights && ((controller.demoMode && import.meta.env.DEV) || controller.account?.role.toLowerCase() === 'admin') && (
      <div className="neon-admin-triggers" role="group" aria-label="Admin Special Plays triggers">
        {neonAdminPreviewSymbols.map(({ symbol, label }) => (
          <button
            key={symbol}
            className="neon-admin-triggers__button"
            type="button"
            aria-label={`Trigger ${label} Special Plays`}
            disabled={isSpinServiceDisabled || isSpinning || controller.isNeonAdminTriggering || freeSpinsRemaining > 0 ||
              Boolean(neonRoundSummary) ||
              Boolean(neonSpecialIntro) || isNeonArcadeExpanding}
            onClick={() => controller.triggerNeonAdminPreview(symbol)}
          >
            <img src={getSlotSymbolDefinition(symbolSet, symbol).image} alt="" aria-hidden="true" />
          </button>
        ))}
      </div>
    )}
  </div>
  const guideSymbols = isMidnightVault ? midnightVaultGuideForModel(symbolSet, controller.midnightBonusRound?.mathModelId) : symbolSet
  const symbolValueGuide = !isGodsTumbleBoard && <SymbolValueGuide
    symbolSet={guideSymbols}
    showSidePanel={usesPiratesControls}
    sidePanelClassName={isNeonNights ? 'symbol-value-guide--neon' : usesPiratesControls ? 'symbol-value-guide--pirates' : undefined}
    showValueTokens={Boolean(moneyGrabFeature?.valueSymbolPrefix)}
    theme={isMidnightVault ? 'midnight-vault' : undefined}
    wager={isNeonNights ? activeWagerDisplay : undefined}
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
      data-midnight-vault-bonus={midnightVaultBonusVisible ? 'true' : undefined}
      data-midnight-vault-mode={midnightVaultBonusVisible ? controller.freeSpinFeatureMode ?? 'standard' : undefined}
      data-neon-arcade-expanding={isNeonArcadeExpanding ? 'true' : undefined}
    >
      <main className="slots-page__main">
        <h1 className={isMidnightVault ? 'midnight-vault__accessible-title' : 'slots-page__accessible-title'}>{cabinetTheme.title}</h1>
        {isArcaneArchives && <img className="arcane-archives__wordmark" src={arcaneArchivesWordmark} width={768} height={512} alt="" />}
        <div className="slots-page__layout">
          <div className="slots-page__stage">
          {cabinetTheme.titleArtwork && (
            <img className="slots-page__title-art" {...cabinetTheme.titleArtwork} alt="" />
          )}
          {isRainbowRealm && (
            <img className="rainbow-realm__wordmark" src={rainbowRealmWordmark} width={640} height={305} alt="" />
          )}
          {isMidnightVault && (
            <div className="midnight-vault__masthead" aria-hidden="true">
              <h2 className="midnight-vault__wordmark">
                <ResponsiveSlotImage
                  src="/assets/slots/midnight-vault/v2/midnight-vault-wordmark-384.webp"
                  imageSrcSet="/assets/slots/midnight-vault/v2/midnight-vault-wordmark-384.webp 384w, /assets/slots/midnight-vault/v2/midnight-vault-wordmark-768.webp 768w, /assets/slots/midnight-vault/v2/midnight-vault-wordmark-1024.webp 1024w"
                  width={1024}
                  height={341}
                  alt=""
                />
              </h2>
            </div>
          )}
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
            {midnightVaultBonusVisible && specialRound && (
              <aside className="midnight-vault__bonus-hud" aria-live="polite">
                <span>{controller.freeSpinFeatureMode === 'rows-paw' ? 'Enhanced Vault Breach' : 'Vault Breach'}</span>
                <strong>{freeSpinsRemaining} of {controller.midnightBonusRound?.totalSpins ?? 10} spins remaining</strong>
                <small>{controller.freeSpinFeatureMode === 'rows-paw'
                  ? 'Six rows · extra Vault wilds'
                  : 'Four rows'} · {formatVaultMoney(controller.activeWagerDisplay)} locked</small>
                {controller.midnightQueuedRounds > 0 && <small>{controller.midnightQueuedRounds} bonus {controller.midnightQueuedRounds === 1 ? 'round' : 'rounds'} queued</small>}
                {controller.midnightBonusRound?.mathModelId === 'midnight-vault-paytable-v1' && <small>Previously earned round · original payout rules</small>}
                <b>Bonus win {formatVaultMoney(specialRoundWinnings)}</b>
              </aside>
            )}
            {isSpecialGameActive && specialRound && !isReelRiches && !isMidnightVault && !neonRoundSummary && (
              <aside className="slots-page__special-game-tally" aria-live="polite">
                <span>{isNeonArcadePaidPlay
                  ? `Arcade Plays · ${freeSpinsRemaining} paid ${freeSpinsRemaining === 1 ? 'play' : 'plays'} left · ${formatRand(activeWagerDisplay)} locked`
                  : isRainbowRealm || isNeonNights
                  ? `${specialRound.title} · ${freeSpinsRemaining} bonus ${freeSpinsRemaining === 1 ? 'spin' : 'spins'} left`
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

          <div ref={cabinetRef} className={`slots-page__cabinet-spotlight${collectionAwardPresentation || isSpecialGameActive || midnightVaultBonusVisible ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
            <SlotMachine
              cabinetTheme={activeCabinetTheme}
              reelCount={displayedReels.length}
              reelOverlay={lightningCollection ? (
                <ArcaneLightningCollection
                  collection={lightningCollection}
                  reducedMotion={prefersReducedMotion}
                />
              ) : godsTumbleAward && (
                <GodsTumbleAwardBadge
                  key={godsTumbleAward.symbol}
                  positions={godsTumbleAward.positions}
                  columns={displayedReels.length}
                  rows={displayedReels[0].length}
                  amount={`+${formatTumbleAmount(godsTumbleAward.amountPoints)}`}
                />
              )}
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
                    highlighted={isTumbleHighlight || highlightOrder >= 0}
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
            {usesPaylineTrace && <MidnightWinningPaylines
              palette={isNeonNights ? 'neon' : isRainbowRealm ? 'rainbow' : paylineTrace ? 'cabinet' : 'gold'}
              formatWinAmount={isMidnightVault ? undefined
                : points => `+${formatVaultMoney(slotPointsToRand(points, controller.pointValueInCents))}`}
              paylines={tracedPaylines}
              resultKey={lastSpinOutcomeKey}
              visible={hasCompletedSpin}
              reducedMotion={prefersReducedMotion}
              onActivePathChange={isNeonNights || paylineTrace ? setActivePaidPath : undefined}
            />}
            {isNeonNights && <span className="slots-page__win-announcement" role="status" aria-atomic="true">
              {hasCompletedSpin && lastWin > 0 ? `Won ${formatVaultMoney(lastWin)}.` : ''}
            </span>}
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
            {!usesPiratesControls && !isMidnightVault && symbolValueGuide}

            {!usesPiratesControls && (
              <div
                ref={creditTileRef}
                className="slots-page__balance slots-page__control-tile"
                aria-label={`Balance: ${formatRand(balance)}`}
              >
                <span className="slots-page__balance-label">Balance</span>
                <span className="slots-page__balance-line">
                  <span className="slots-page__balance-value">{isMidnightVault ? formatVaultMoney(balance) : formatRand(balance)}</span>
                </span>
              </div>
            )}

            {isMidnightVault && (
              <div className="midnight-vault__last-win" aria-label={`Last win: ${formatRand(lastWin)}`}>
                <span>Win</span>
                <strong>{formatVaultMoney(lastWin)}</strong>
              </div>
            )}

            {usesPiratesControls ? (
              <PiratesSpinControls
                activeWager={activeWagerDisplay}
                disabled={isSpinServiceDisabled || Boolean(neonSpecialIntro) || Boolean(neonRoundSummary) || isNeonArcadeExpanding || (usesSidecarControls && rainbowBonusSummary !== null)}
                freeSpinsRemaining={freeSpinsRemaining}
                isAutoSpinning={isAutoSpinning}
                isFreeSpinBadgePopping={isFreeSpinBadgePopping}
                isSpinning={isSpinning}
                isStopRequested={isStopRequested}
                onAutoSpinChange={(active) => { setSpinError(null); setIsAutoSpinning(active) }}
                onSelectWager={controller.selectWager}
                onSpin={handleSpinButtonClick}
                pointValueInCents={controller.pointValueInCents}
                showFreeSpinBadge={showFreeSpinBadge && !neonSpecialIntro}
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
                {isMidnightVault && specialControlsActive && (
                  <div className="slots-page__wager-control midnight-vault__locked-wager" aria-label={`Locked wager: ${formatRand(activeWagerDisplay)}`}>
                    <output className="slots-page__wager-display">
                      <span>Locked bet</span>
                      <strong>{activeWagerLabel}</strong>
                    </output>
                  </div>
                )}
                <div className="slots-page__spin-button-shell">
                  <SpinButton
                    disabled={isSpinServiceDisabled || rainbowBonusSummary !== null}
                    iconSrc={cabinetTheme.spinIconImage}
                    isSpinning={isSpinning}
                    isStopRequested={isStopRequested}
                    onSpin={handleSpinButtonClick}
                    variant={isMidnightVault ? 'vault-dial' : isPiratesFortune ? 'pirate-helm' : 'default'}
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
                      className={`slots-page__auto-spin${isAutoSpinning ? ' slots-page__auto-spin--active' : ''}${isMidnightVault ? ' midnight-vault__autospin' : ''}`}
                      type="button"
                      disabled={isSpinServiceDisabled}
                      aria-pressed={isAutoSpinning}
                      onClick={() => {
                        setSpinError(null)
                        setIsAutoSpinning((current) => !current)
                      }}
                      aria-label={isAutoSpinning ? 'Stop autospin' : 'Start autospin'}
                    >
                      {isMidnightVault ? (
                        <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                          <path d="M21 12a9 9 0 1 1-9-9c2.5 0 4.9 1 6.7 2.7L21 8M21 3v5h-5" />
                          {isAutoSpinning
                            ? <rect className="midnight-vault__autospin-action" x="9" y="9" width="6" height="6" rx=".75" />
                            : <path className="midnight-vault__autospin-action" d="m10 8 6 4-6 4Z" />}
                        </svg>
                      ) : <strong>Autospin</strong>}
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
          {!isMidnightVault && (balanceActionHref !== null || !usesPiratesControls) && <div className="slots-page__local-controls">
            <div className="slots-page__utility-controls" aria-label="Game options">
              {balanceActionHref !== null && <PaymentAlertsMenu />}
              {!usesPiratesControls && utilityButtons}
            </div>
          </div>}
        </div>
        {isMidnightVault && (
          <div className="midnight-vault__utility-controls" role="group" aria-label="Game options">
            <button type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={isHelpOpen}
              onClick={() => { setIsAutoSpinning(false); setIsHelpOpen(true) }}>
              <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M9.5 9a2.5 2.5 0 0 1 5 0c0 1.7-2.5 1.8-2.5 3.8M12 16.5v.1" /></svg>
            </button>
            <button type="button" aria-label="Open settings" aria-haspopup="dialog" aria-expanded={isSettingsOpen}
              onClick={() => { setIsAutoSpinning(false); setIsSettingsOpen(true) }}>
              <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" /><circle cx="12" cy="11.5" r="3" /></svg>
            </button>
            {symbolValueGuide}
            {balanceActionHref !== null && <PaymentAlertsMenu />}
          </div>
        )}
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

      {(isSpecialGameActive || reelRichesIntroVisible || midnightVaultBonusVisible || rainbowBonusVisible) && (
        <div className="slots-page__special-game-dimmer" aria-hidden="true" />
      )}

      {neonSpecialIntro && !isSpinning && (
        <NeonSpecialPlaysIntro
          cabinetRef={cabinetRef}
          position={neonSpecialIntro.position}
          symbol={neonSpecialIntro.symbol}
          symbolImage={getSlotSymbolDefinition(symbolSet, neonSpecialIntro.symbol).image}
          volume={audioPreferences.mode === 'muted' ? 0 : audioPreferences.volume / 100}
          onComplete={() => controller.finishNeonSpecialIntro(neonSpecialIntro.symbol)}
        />
      )}

      {neonRoundSummary && (
        <NeonRoundSummary
          cabinetRef={cabinetRef}
          title={neonRoundSummary.mode === 'arcade-rows' ? 'Arcade Plays' : 'Special Plays'}
          amount={formatVaultMoney(neonRoundSummary.totalRand)}
          onDismiss={controller.dismissNeonRoundSummary}
        />
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
                (isMidnightVault ? 'Enhanced Vault Breach unlocked' : isPiratesFortune ? 'Treasure feature unlocked' : isWukong ? 'Celestial seal awakened' : isRainbowRealm ? 'Orchard glass filled' : 'Collection complete')}
            </span>
            <h2 id="collection-award-title">{completedCollection?.label ?? 'Gem collection'} complete</h2>
            <p>
              {collectionAwardPresentation.freeSpins} {isMidnightVault ? 'bonus spin' : specialRound?.title ?? 'free game'}
              {collectionAwardPresentation.freeSpins === 1 ? ' is' : 's are'} now loaded.
              {' '}{collectionFeature?.completionDetail ??
                (isPiratesFortune
                  ? 'Your gem pile glows while new gems build toward the next run.'
                  : 'New collection pieces are banked for the next run.')}
            </p>
            {completedFeatureDetail && (
              <p>{isMidnightVault ? 'Every bonus spin' : 'Every free game'}: {completedFeatureDetail}.</p>
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
        collectionFeature.presentation === 'gem-hoard' ? (
          <TreasureGemFlyover
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

      {winAwardFlyover && (!hasPaylineTrace || isMidnightVault) && (
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

      {!isMidnightVault && (
        <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
          <span><kbd>Space</kbd> Spin / stop</span>
          <span><kbd>←</kbd><kbd>→</kbd> Wager</span>
          <button className="slots-page__mute-shortcut" type="button"
            aria-pressed={audioPreferences.mode === 'muted'} onClick={toggleMuted}>
            <kbd aria-hidden="true">M</kbd> Mute
          </button>
          <span><kbd>?</kbd> Rules</span>
        </div>
      )}

      <AudioSettingsDialog
        theme={isMidnightVault ? 'midnight-vault' : undefined}
        musicCredit={isMidnightVault ? <>Music: <a href="https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1100199" target="_blank" rel="noopener noreferrer">I Knew a Guy</a> by Kevin MacLeod (incompetech.com), licensed under <a href="https://creativecommons.org/licenses/by/4.0/" target="_blank" rel="noopener noreferrer">CC BY 4.0</a>. The background recording is unmodified. The win cue uses a short excerpt with fades and a gain adjustment.</>
          : cabinetTheme.id === 'gods-of-olympus-cabinet-v1' ? <>
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
        theme={isMidnightVault ? 'midnight-vault' : undefined}
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
        theme={isMidnightVault ? 'midnight-vault' : undefined}
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
