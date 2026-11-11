import { useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  AudioSettingsDialog,
  getSlotSymbolDefinition,
  InsufficientBalanceDialog,
  ResponsiveSlotImage,
  SlotMachine,
  SlotSymbol,
  SpinButton,
  SymbolValueGuide,
  WinHelpDialog,
} from '@fortuneforge/slot-client-core'
import { MidnightCollectionProgress } from './components/MidnightCollectionProgress.js'
import { MidnightHelp } from './components/MidnightHelp.js'
import { MidnightWinningPaylines, type ActivePaidPath } from './components/MidnightWinningPaylines.js'
import { midnightCabinet, midnightCollections, midnightRules, midnightSpecialRound, midnightSymbols } from './manifest.js'
import { midnightVaultGuideForModel } from './math.js'
import { MIDNIGHT_IMAGES, MIDNIGHT_WORDMARK_SRC_SET } from './symbols.js'
import type { MidnightVaultGameProps } from './types.js'
import { shouldUseAnimatedSymbol, type MidnightVaultController } from './useMidnightVault.js'
import './styles.css'

const moneyFormatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const money = (value: number) => `R${moneyFormatter.format(value)}`

export function MidnightVaultView({ controller: c, renderShell, paymentAlerts }: {
  controller: MidnightVaultController
} & Pick<MidnightVaultGameProps, 'renderShell' | 'paymentAlerts'>) {
  const cabinetRef = useRef<HTMLDivElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  const tracedPositions = c.hasCompletedSpin && activePath?.resultKey === c.lastSpinOutcomeKey
    ? activePath.positions
    : c.winningPositions
  const guideSymbols = useMemo(
    () => midnightVaultGuideForModel(midnightSymbols, c.state.bonusRound?.mathModelId),
    [c.state.bonusRound?.mathModelId],
  )
  const specialControlsActive = c.isSpecialGameActive || c.midnightBonusVisible || c.collectionAwardPresentation !== null
  const completedDefinition = c.collectionAwardPresentation
    ? midnightCollections.find(entry => entry.id === c.collectionAwardPresentation?.collectionId)
    : undefined
  const completedCollection = c.collectionAwardPresentation
    ? c.state.sealCollections.find(entry => entry.sealId === c.collectionAwardPresentation?.collectionId)
    : undefined
  const completedGem = completedDefinition
    ? getSlotSymbolDefinition(midnightSymbols, completedDefinition.symbol).image
    : null
  const spinDisabled = c.spinAvailability !== 'available' || c.blocked
  const openHelp = () => { c.setIsAutoSpinning(false); c.setIsHelpOpen(true) }
  const openSettings = () => { c.setIsAutoSpinning(false); c.setIsSettingsOpen(true) }

  const content = <div
    className={c.slotsPageClassName}
    style={c.pageStyle}
    data-slot-theme={midnightCabinet.id}
    data-slot-celebration={midnightCabinet.celebrationEffect}
    data-special-round={midnightSpecialRound.id}
    data-special-earn-style={midnightSpecialRound.earnStyle}
    data-midnight-vault-bonus={c.midnightBonusVisible ? 'true' : undefined}
    data-midnight-vault-mode={c.midnightBonusVisible ? c.state.freeSpinFeatureMode ?? 'standard' : undefined}
  >
    <main className="slots-page__main">
      <h1 className="midnight-vault__accessible-title">Midnight Vault</h1>
      <div className="slots-page__layout">
        <div className="slots-page__stage">
          <div className="midnight-vault__masthead" aria-hidden="true">
            <h2 className="midnight-vault__wordmark">
              <ResponsiveSlotImage src={MIDNIGHT_IMAGES.WORDMARK} imageSrcSet={MIDNIGHT_WORDMARK_SRC_SET}
                width={1024} height={341} alt="" />
            </h2>
          </div>

          <div className="slots-page__meter-stack">
            <div className="slots-page__seal-collections" aria-label="Vault jewel collections">
              {c.state.sealCollections.map(collection => <MidnightCollectionProgress
                collection={collection}
                symbolSet={midnightSymbols}
                displayCount={c.isSpecialGameActive && c.heldCompletedCollectionId === collection.sealId
                  ? collection.requiredCount : undefined}
                celebrating={c.collectionAwardPresentation?.collectionId === collection.sealId}
                impacting={c.sealImpactId === collection.sealId}
                key={collection.sealId}
              />)}
            </div>
            {c.midnightBonusVisible && <aside className="midnight-vault__bonus-hud" aria-live="polite">
              <span>{c.state.freeSpinFeatureMode === 'rows-paw' ? 'Enhanced Vault Breach' : 'Vault Breach'}</span>
              <strong>{c.state.freeSpinsRemaining} of {c.state.bonusRound?.totalSpins ?? 10} spins remaining</strong>
              <small>{c.state.freeSpinFeatureMode === 'rows-paw' ? 'Six rows · extra Vault wilds' : 'Four rows'} · {money(c.activeWagerDisplay)} locked</small>
              {c.state.queuedBonusRounds.length > 0 && <small>{c.state.queuedBonusRounds.length} bonus {c.state.queuedBonusRounds.length === 1 ? 'round' : 'rounds'} queued</small>}
              {c.state.bonusRound?.mathModelId === 'midnight-vault-paytable-v1' && <small>Previously earned round · original payout rules</small>}
              <b>Bonus win {money(c.specialRoundWinnings)}</b>
            </aside>}
          </div>

          <div ref={cabinetRef} className={`slots-page__cabinet-spotlight${c.collectionAwardPresentation || c.isSpecialGameActive || c.midnightBonusVisible ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
            <SlotMachine cabinetTheme={midnightCabinet} reelCount={c.displayedReels.length} renderReel={reelIndex => <div
              className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`}
              style={c.reelStripStyle(reelIndex)}
            >
              {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
                const highlightOrder = tracedPositions.findIndex(position => position.reel === reelIndex && position.row === rowIndex)
                return <SlotSymbol key={`row-${rowIndex}`} symbol={symbol} symbolSet={midnightSymbols}
                  reelIndex={reelIndex} rowIndex={rowIndex}
                  animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
                  highlighted={highlightOrder >= 0} highlightOrder={highlightOrder} />
              })}
            </div>} />
            <MidnightWinningPaylines paylines={c.winningPaylines} resultKey={c.lastSpinOutcomeKey}
              visible={c.hasCompletedSpin} reducedMotion={c.reducedMotion} onActivePathChange={setActivePath} />
          </div>

          <div className="slots-page__playbar" aria-label="Balance, wager, and spin controls">
            <div className="slots-page__balance slots-page__control-tile" aria-label={`Balance: ${money(c.balance)}`}>
              <span className="slots-page__balance-label">Balance</span>
              <span className="slots-page__balance-line"><span className="slots-page__balance-value">{money(c.balance)}</span></span>
            </div>
            <div className="midnight-vault__last-win" aria-label={`Last win: ${money(c.lastWin)}`}>
              <span>Win</span><strong>{money(c.lastWin)}</strong>
            </div>
            <div className={`slots-page__spin-controls${specialControlsActive ? ' slots-page__spin-controls--special' : ''}`}
              aria-label={specialControlsActive ? 'Special game spin control' : 'Spin, autospin, and wager controls'}>
              <div className="slots-page__spin-stack">
                {specialControlsActive && <div className="slots-page__wager-control midnight-vault__locked-wager"
                  aria-label={`Locked wager: ${money(c.activeWagerDisplay)}`}>
                  <output className="slots-page__wager-display"><span>Locked bet</span><strong>{money(c.activeWagerDisplay)}</strong></output>
                </div>}
                <div className="slots-page__spin-button-shell">
                  <SpinButton disabled={spinDisabled} isSpinning={c.isSpinning} isStopRequested={c.isStopRequested}
                    onSpin={c.handleSpinButtonClick} variant="vault-dial" />
                </div>
                {!specialControlsActive && <button
                  className={`slots-page__auto-spin midnight-vault__autospin${c.isAutoSpinning ? ' slots-page__auto-spin--active' : ''}`}
                  type="button" disabled={spinDisabled || c.isSpinning} aria-pressed={c.isAutoSpinning}
                  onClick={() => { c.setSpinError(null); c.setIsAutoSpinning(current => !current) }}
                  aria-label={c.isAutoSpinning ? 'Stop autospin' : 'Start autospin'}>
                  <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                    <path d="M21 12a9 9 0 1 1-9-9c2.5 0 4.9 1 6.7 2.7L21 8M21 3v5h-5" />
                    {c.isAutoSpinning
                      ? <rect className="midnight-vault__autospin-action" x="9" y="9" width="6" height="6" rx=".75" />
                      : <path className="midnight-vault__autospin-action" d="m10 8 6 4-6 4Z" />}
                  </svg>
                </button>}
                {!specialControlsActive && <div className="slots-page__wager-control" role="group" aria-label={`Wager: ${money(c.activeWagerDisplay)}`}>
                  <button className="slots-page__wager-nudge" type="button" aria-label="Decrease wager"
                    disabled={c.isSpinning || c.isAutoSpinning || c.state.freeSpinsRemaining > 0 || c.wagerIndex === 0}
                    onClick={() => c.changeWager(-1)}><svg viewBox="0 0 100 100" aria-hidden="true"><path d="M28 50H72" /></svg></button>
                  <output className="slots-page__wager-display" aria-live="polite"><span>Bet</span><strong>{money(c.activeWagerDisplay)}</strong></output>
                  <button className="slots-page__wager-nudge" type="button" aria-label="Increase wager"
                    disabled={c.isSpinning || c.isAutoSpinning || c.state.freeSpinsRemaining > 0 || c.wagerIndex === midnightRules.wagerOptions.length - 1}
                    onClick={() => c.changeWager(1)}><svg viewBox="0 0 100 100" aria-hidden="true"><path d="M28 50H72" /><path d="M50 28V72" /></svg></button>
                </div>}
              </div>
            </div>
          </div>
        </div>

        <div className="midnight-vault__utility-controls" role="group" aria-label="Game options">
          <button type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={c.isHelpOpen} onClick={openHelp}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M9.5 9a2.5 2.5 0 0 1 5 0c0 1.7-2.5 1.8-2.5 3.8M12 16.5v.1" /></svg>
          </button>
          <button type="button" aria-label="Open settings" aria-haspopup="dialog" aria-expanded={c.isSettingsOpen} onClick={openSettings}>
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" /><circle cx="12" cy="11.5" r="3" /></svg>
          </button>
          <SymbolValueGuide symbolSet={guideSymbols} showValueTokens={false} theme="midnight-vault"
            isOpen={c.isSymbolGuideOpen} onOpenChange={c.setIsSymbolGuideOpen}
            triggerContent={<svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" width="24" height="24"
              fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
              <circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7h.01" />
            </svg>} />
          {paymentAlerts}
        </div>
      </div>
    </main>

    {c.lastSpinOutcome && c.lastSpinOutcome.kind !== 'loss' && <div key={c.resultAtmosphereId}
      className="slots-page__result-atmosphere" data-celebration="vault-jewel-glint"
      data-outcome={c.lastSpinOutcome.kind} aria-hidden="true"><span /><span /></div>}
    {(c.isSpecialGameActive || c.midnightBonusVisible) && <div className="slots-page__special-game-dimmer" aria-hidden="true" />}

    {c.collectionAwardPresentation && <section className="slots-page__collection-award-dialog" role="status"
      aria-live="assertive" aria-labelledby="collection-award-title">
      {completedGem && <span className="slots-page__collection-award-gems" aria-hidden="true">
        {Array.from({ length: 5 }, (_, index) => <img src={completedGem} alt="" key={index} />)}
      </span>}
      <span className="slots-page__collection-award-kicker">Enhanced Vault Breach unlocked</span>
      <h2 id="collection-award-title">{completedDefinition?.shortLabel ?? 'Gem collection'} complete</h2>
      <p>{c.collectionAwardPresentation.freeSpins} bonus spin{c.collectionAwardPresentation.freeSpins === 1 ? ' is' : 's are'} now loaded. New collection pieces are banked for the next run.</p>
      {c.collectionAwardPresentation.featureMode === 'rows-paw' && <p>Every bonus spin: Enhanced Vault Breach · six rows with extra Vault wilds.</p>}
      {completedCollection && <p className="slots-page__collection-award-hint">Press the Spin button to begin.</p>}
    </section>}

    {c.winAward && <div key={c.winAward.id}
      className={`slots-page__win-award slots-page__win-award--${c.winAward.winTier} slots-page__win-award--${c.winAward.tier}${c.winAward.isFlying ? ' slots-page__win-award--flying' : ''}`}
      aria-hidden="true" style={{ left: '50%', top: '50%', animationDuration: '1.2s' } as CSSProperties}>
      <span>{c.lastSpinOutcome?.title ?? 'Win'}</span><strong>+{money(c.winAward.displayAmount)}</strong>
    </div>}

    {(c.spinError || c.spinAvailability === 'unavailable' || c.lastSpinOutcome?.kind === 'loss') && <footer
      className={`slots-page__footer${c.spinError || c.spinAvailability === 'unavailable' ? ' slots-page__footer--error' : ''}`}>
      <div className={`game-outcome${c.spinError || c.spinAvailability === 'unavailable' ? ' game-outcome--error' : ''}`} role="status">
        {c.spinError ?? (c.spinAvailability === 'unavailable' ? 'The Midnight Vault game service is offline.' : c.lastSpinOutcome?.title)}
      </div>
    </footer>}

    <AudioSettingsDialog theme="midnight-vault" isOpen={c.isSettingsOpen} onClose={() => c.setIsSettingsOpen(false)}
      preferences={c.audio.preferences} onToggleMuted={c.audio.toggleMuted} onToggleResultsOnly={c.audio.toggleResultsOnly}
      onVolumeChange={c.audio.setVolume} musicCredit={<>Music: <a href="https://incompetech.com/music/royalty-free/index.html?Search=Search&isrc=USUAN1100199" target="_blank" rel="noopener noreferrer">I Knew a Guy</a> by Kevin MacLeod (incompetech.com), licensed under <a href="https://creativecommons.org/licenses/by/4.0/" target="_blank" rel="noopener noreferrer">CC BY 4.0</a>. The background recording is unmodified. The win cue uses a short excerpt with fades and a gain adjustment.</>} />
    <WinHelpDialog theme="midnight-vault" isOpen={c.isHelpOpen} closeButtonRef={c.helpCloseButtonRef}
      onClose={() => c.setIsHelpOpen(false)}><MidnightHelp /></WinHelpDialog>
    <InsufficientBalanceDialog theme="midnight-vault" isOpen={c.isReloadPromptOpen}
      closeButtonRef={c.reloadPromptCloseButtonRef} selectedWager={c.activeWagerDisplay} balance={c.balance}
      recoveryHref={c.demoMode && typeof location !== 'undefined' ? location.pathname : '/balance'}
      recoveryLabel={c.demoMode ? 'Reset balance' : 'Add Rand'} onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>

  return renderShell
    ? renderShell({ balance: c.balance, onNavigate: () => c.setIsAutoSpinning(false), children: content })
    : content
}
