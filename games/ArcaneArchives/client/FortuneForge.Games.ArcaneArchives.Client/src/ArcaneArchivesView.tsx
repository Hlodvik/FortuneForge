import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  AudioSettingsDialog, formatRand, InsufficientBalanceDialog, shouldUseAnimatedSymbol,
  SidecarSpinControls, SlotMachine, SlotSymbol, slotPointsToRand, SymbolValueGuide, WinHelpDialog,
  type LightningCollection,
} from '@fortuneforge/slot-client-core'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { ARCANE_ARCHIVES_CABINET_THEME as cabinet } from './cabinetTheme'
import { ARCANE_ARCHIVES_SYMBOLS as symbols } from './symbols'
import { ARCANE_ARCHIVES_WORDMARK } from './presentation'
import { ArcaneLightningCollection } from './ArcaneLightningCollection'
import { ArcanePotionWager } from './ArcanePotionWager'
import { ArcaneTomePanel } from './ArcaneTomePanel'
import { ARCANE_ARCHIVES_EXPERIENCE_SET } from './manifest'
import { ARCANE_ARCHIVES_SOUNDS } from './sounds'
import { getArcaneWinOutcomeKind } from './arcaneWinPresentation'
import { useArcaneWinPopup } from './useArcaneWinPopup'
import { ARCANE_LIGHTNING_PAYLINE_ID, ARCANE_WAND_PAYLINE_ID, type ArcaneArchivesGameProps } from './types'
import type { useArcaneArchives } from './useArcaneArchives'
import './arcaneArchivesView.css'

const money = (amount: number) => `R${new Intl.NumberFormat('en-US', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
}).format(amount)}`
const winHelp = ARCANE_ARCHIVES_EXPERIENCE_SET.presentation!.winHelp!
const WinHelpContent = winHelp.Content
const introductionStorageKey = winHelp.firstOpenStorageKey
const pageStyle = {
  '--slot-page-backdrop': `url("${cabinet.pageBackdropImage}")`,
  '--slot-result-primary': cabinet.palette.trimBright,
  '--slot-result-secondary': cabinet.palette.accent,
  '--slot-result-glow': cabinet.palette.glow,
  '--slot-payline-base': cabinet.paylineTrace!.color,
  '--slot-payline-glow': cabinet.paylineTrace!.glowColor,
  '--slot-payline-label': cabinet.palette.text,
  '--slot-payline-label-outline': cabinet.palette.shellBottom,
} as CSSProperties

export function ArcaneArchivesView({ controller: c, renderShell }: {
  controller: ReturnType<typeof useArcaneArchives>
} & Pick<ArcaneArchivesGameProps, 'renderShell'>) {
  const cabinetRef = useRef<HTMLDivElement>(null)
  const totalRef = useRef<HTMLDivElement>(null)
  const amountRef = useRef<HTMLElement>(null)
  const helpCloseButtonRef = useRef<HTMLButtonElement>(null)
  const reloadCloseButtonRef = useRef<HTMLButtonElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  // These synthetic collector awards have their own constellation presentation.
  const paylines = useMemo(() => c.winningPaylines.filter(line =>
    line.paylineId !== ARCANE_LIGHTNING_PAYLINE_ID && line.paylineId !== ARCANE_WAND_PAYLINE_ID), [c.winningPaylines])
  const collection: LightningCollection | null = c.wandCollection
    ? { collector: c.wandCollection.wand, cells: c.wandCollection.cells, totalPoints: c.wandCollection.totalPoints }
    : c.lightningCollection
  const multipleAwards = paylines.length + Number(collection !== null) > 1
  const lastWager = c.lastWager
  const outcomeKind = getArcaneWinOutcomeKind(c.lastWin, lastWager, c.lastResult?.freeSpinsAwarded ?? 0)
  const narrative = ARCANE_ARCHIVES_EXPERIENCE_SET.outcomeNarrative!
  const winTitle = outcomeKind === 'big-win' ? narrative.bigWinTitle
    : outcomeKind === 'great-win' ? narrative.greatWinTitle : narrative.winTitle
  const highlighted = c.hasCompletedSpin && activePath?.resultKey === c.resultKey
    ? activePath.positions : c.winningPositions
  const popup = useArcaneWinPopup({ amount: c.lastWin, wager: lastWager,
    resultKey: c.resultKey, visible: c.hasCompletedSpin, interrupted: c.isStopRequested, reducedMotion: c.reducedMotion,
    fast: c.isAutoSpinning, collection: collection !== null, multipleAwards,
    cabinetRef, totalRef, amountRef })
  const closeHelp = useCallback(() => c.setIsHelpOpen(false), [c.setIsHelpOpen])
  const openHelp = () => { c.setIsAutoSpinning(false); c.setIsHelpOpen(true) }
  const openSettings = () => { c.setIsAutoSpinning(false); c.setIsSettingsOpen(true) }
  const disabled = c.spinAvailability !== 'available' || c.isArcaneUpdating || c.isTomeActivationRequired

  useEffect(() => {
    if (!introductionStorageKey) return
    try {
      if (window.localStorage.getItem(introductionStorageKey) === '1') return
      window.localStorage.setItem(introductionStorageKey, '1')
    } catch { /* The introduction remains available when device storage is unavailable. */ }
    c.setIsHelpOpen(true)
  }, [c.setIsHelpOpen])

  const content = <div className={`slots-page slots-page--theme-backdrop${c.isAutoSpinning || c.isStopRequested ? ' slots-page--fast-spin' : ''}`}
    data-slot-theme={cabinet.id} data-slot-controls="sidecar" data-slot-payline-trace="true"
    data-slot-celebration={cabinet.celebrationEffect} style={pageStyle}
    onClickCapture={event => {
      if (event.target instanceof Element && event.target.closest('[data-slot-utility-cue]')) {
        const cue = ARCANE_ARCHIVES_SOUNDS.events.utilityButtonClick
        if (cue) c.playCue(cue)
      }
    }}>
    <main className="slots-page__main">
      <h1 className="slots-page__accessible-title">{cabinet.title}</h1>
      <div className="slots-page__layout"><div className="slots-page__stage">
        <img className="arcane-archives__wordmark" src={ARCANE_ARCHIVES_WORDMARK} width={768} height={512} alt="" />
        <div ref={cabinetRef} data-result-ready={c.hasCompletedSpin || undefined}
          className={`slots-page__cabinet-spotlight${c.isSpecialGameActive ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
          <SlotMachine cabinetTheme={cabinet} reelCount={c.displayedReels.length}
            reelOverlay={collection && c.hasCompletedSpin ? <ArcaneLightningCollection key={c.resultKey}
              collection={collection} reducedMotion={c.reducedMotion} /> : undefined}
            renderReel={reelIndex => <div className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`}
              style={c.reelStripStyle(reelIndex)}>
              {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
                if (c.tomeState?.activePage && symbol === 'ACE') return <div key={rowIndex} className="slot-symbol" aria-hidden="true" />
                const highlightOrder = highlighted.findIndex(position => position.reel === reelIndex && position.row === rowIndex)
                const collected = collection?.cells.find(position => position.reel === reelIndex && position.row === rowIndex)
                return <SlotSymbol key={`row-${rowIndex}`} symbol={symbol} symbolSet={symbols}
                  reelIndex={reelIndex} rowIndex={rowIndex}
                  animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
                  highlighted={highlightOrder >= 0} highlightOrder={highlightOrder}
                  valueLabel={collected ? formatRand(slotPointsToRand(collected.amountPoints, c.pointValueInCents)) : undefined} />
              })}
            </div>} />
          <WinningPaylines boardSelector=".slots-page__cabinet-spotlight" reelsSelector=".slot-machine__reels"
            cellSelector=".slot-symbol[data-reel-index][data-row-index]"
            appearance={{ classPrefix: 'slot-winning-paylines', variant: 'cabinet', reducedMotionTrail: true,
              labelInset: 64, labelOffset: 22, labelMinimumY: 34 }}
            paylines={paylines} resultKey={c.resultKey} visible={c.hasCompletedSpin}
            reducedMotion={c.reducedMotion} formatWinAmount={points => `+${money(slotPointsToRand(points, c.pointValueInCents))}`}
            onActivePathChange={setActivePath} />
        </div>
        <div className="slots-page__spin-total" ref={totalRef}>
          {c.hasCompletedSpin && !c.isSpinning && multipleAwards && c.lastWin > 0 && <output
            className="slots-page__win-amount" aria-label="Total win">+{money(c.lastWin)}</output>}
        </div>
        {c.tomeState && <ArcaneTomePanel state={c.tomeState} potions={c.potions}
          awarded={c.tomeIngredientsAwarded} cabinetRef={cabinetRef}
          settledSpin={c.hasCompletedSpin ? { key: c.resultKey, reels: c.displayedReels } : null}
          reducedMotion={c.reducedMotion} disabled={c.isSpinning || c.isArcaneUpdating}
          error={c.arcaneError} pointValueInCents={c.pointValueInCents}
          onOpen={() => c.setIsAutoSpinning(false)}
          onChangePage={(page, activate) => { void c.changeTomePage(page, activate) }}
          onUsePotion={id => { void c.activatePotion(id) }} confirmedPotionUse={c.confirmedPotionUse}
          onPotionCue={c.playPotionCue} />}
        <div className="slots-page__playbar" aria-label="Wager and spin controls">
          <SidecarSpinControls activeWager={c.activeWagerDisplay}
            wagerLocked={c.isPotionWagerLocked}
            wagerReadout={<ArcanePotionWager wager={c.activeWagerDisplay} selectedWager={c.selectedWager}
              lockedPotionId={c.freeSpinsRemaining === 0 && c.isPotionWagerLocked ? c.potions?.active?.bottle.id ?? null : null}
              confirmedPotionUse={c.confirmedPotionUse} reducedMotion={c.reducedMotion} />}
            disabled={disabled} freeSpinsRemaining={c.freeSpinsRemaining}
            isAutoSpinning={c.isAutoSpinning} isFreeSpinBadgePopping={false}
            isSpinning={c.isSpinning} isStopRequested={c.isStopRequested}
            onAutoSpinChange={c.setIsAutoSpinning} onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick}
            pointValueInCents={c.pointValueInCents} showFreeSpinBadge={c.freeSpinsRemaining > 0}
            specialControlsActive={false} specialRoundLabel="Free spell"
            wagerIndex={c.wagerIndex} wagerOptions={c.wagerOptions} />
        </div>
        <output className="slots-page__win-announcement" aria-live="polite" aria-atomic="true">
          {c.hasCompletedSpin && c.lastWin > 0 ? `Arcane reward ${money(c.lastWin)}` : ''}
        </output>
        {(c.spinError || c.spinAvailability !== 'available' || c.isTomeActivationRequired) && <div className="arcane-archives__status"
          role={c.spinError ? 'alert' : 'status'}>
          {c.spinError ?? (c.isTomeActivationRequired ? 'Activate a Tome page to spin.' : 'Opening the Archives…')}
        </div>}
      </div></div>
    </main>
    <SymbolValueGuide symbolSet={symbols} showSidePanel sidePanelClassName="symbol-value-guide--arcane"
      wager={c.activeWagerDisplay} pointValueInCents={c.pointValueInCents}
      isOpen={c.isSymbolValuesOpen} onOpenChange={open => { if (open) c.setIsAutoSpinning(false); c.setIsSymbolValuesOpen(open) }} />
    <div className="slots-page__utility-buttons">
      <button className="slots-page__help-button" type="button" aria-label="How to win" data-slot-utility-cue
        aria-haspopup="dialog" aria-expanded={c.isHelpOpen} onClick={openHelp}>
        <span aria-hidden="true">?</span><span className="slots-page__help-button-label">How to win</span>
      </button>
      <button className="slots-page__settings-button" type="button" aria-label="Open settings" data-slot-utility-cue
        aria-haspopup="dialog" aria-expanded={c.isSettingsOpen} onClick={openSettings}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m9.5 3-.7 2.3-2 .9-2.2-.5-2 3.5 1.5 1.8v2.3L2.6 15l2 3.5 2.2-.5 2 .9.7 2.1h5l.7-2.1 2-.9 2.2.5 2-3.5-1.5-1.7v-2.3l1.5-1.8-2-3.5-2.2.5-2-.9L14.5 3Z" /><circle cx="12" cy="12" r="3" />
        </svg>
      </button>
    </div>
    {c.hasCompletedSpin && outcomeKind !== 'loss' && <div key={c.resultKey}
      className="slots-page__result-atmosphere" data-celebration={cabinet.celebrationEffect}
      data-outcome={outcomeKind} aria-hidden="true"><span /><span /></div>}
    {popup && <div key={popup.id} className={`slots-page__win-award slots-page__win-award--payline-popup slots-page__win-award--${popup.tier}${popup.flying ? ' slots-page__win-award--flying' : ''}${popup.toTotal ? ' slots-page__win-award--to-total' : ''}`}
      aria-hidden="true" style={{ left: popup.left, top: popup.top, animationDuration: `${popup.duration}ms`,
        '--win-travel-x': `${popup.travelX}px`, '--win-travel-y': `${popup.travelY}px`, '--win-end-scale': popup.endScale } as CSSProperties}>
      {!popup.toTotal && <span>{winTitle}</span>}
      <strong ref={amountRef} className="slots-page__win-amount">+{money(popup.amount)}</strong>
    </div>}
    <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
      <span><kbd>Space</kbd> Spin / stop</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
      <button className="slots-page__mute-shortcut" type="button" aria-pressed={c.audioPreferences.mode === 'muted'} onClick={c.toggleMuted}>
        <kbd aria-hidden="true">M</kbd> Mute
      </button><span><kbd>?</kbd> Rules</span>
    </div>
    <AudioSettingsDialog isOpen={c.isSettingsOpen} preferences={c.audioPreferences}
      onClose={() => c.setIsSettingsOpen(false)} onToggleMuted={c.toggleMuted}
      onToggleResultsOnly={c.toggleResultsOnly} onVolumeChange={c.setVolume} />
    <WinHelpDialog isOpen={c.isHelpOpen} closeButtonRef={helpCloseButtonRef} onClose={closeHelp}
      title={winHelp.title} eyebrow={winHelp.eyebrow} theme={winHelp.theme}>
      <WinHelpContent onClose={closeHelp} />
    </WinHelpDialog>
    <InsufficientBalanceDialog isOpen={c.isReloadPromptOpen} closeButtonRef={reloadCloseButtonRef}
      selectedWager={c.activeWagerDisplay} balance={c.balance} recoveryHref="/account" recoveryLabel="View account"
      onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>

  return renderShell ? renderShell({ balance: c.balance, onNavigate: () => c.setIsAutoSpinning(false), children: content }) : content
}
