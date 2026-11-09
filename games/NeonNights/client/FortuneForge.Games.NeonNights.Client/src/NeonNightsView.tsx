import { useMemo, useRef, useState, type CSSProperties } from 'react'
import {
  AudioSettingsDialog, getSlotSymbolDefinition, InsufficientBalanceDialog, SidecarSpinControls,
  SlotMachine, SlotSymbol, SymbolValueGuide, shouldUseAnimatedSymbol, WinHelpDialog,
} from '@fortuneforge/slot-client-core'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { neonCabinet, neonRules, neonSymbols } from './manifest.js'
import { NeonDiscoFloor } from './components/NeonDiscoFloor.js'
import { NeonHelp } from './components/NeonHelp.js'
import { NeonRoundSummary } from './components/NeonRoundSummary.js'
import { NeonSpecialPlaysIntro } from './components/NeonSpecialPlaysIntro.js'
import type { NeonNightsController } from './useNeonNights.js'
import type { NeonNightsGameProps, NeonTriggerSymbol } from './types.js'
import './styles.css'

const money = (amount: number) => `R${new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)}`
const adminSymbols = [{ symbol: '6', label: 'Sports car' }, { symbol: 'POWER', label: 'Disco ball' }, { symbol: 'SEAL_RAND', label: 'Arcade cabinet' }] as const
const paylineAppearance = { classPrefix: 'neon-winning-paylines', variant: 'neon', labelOffset: 22, labelMinimumY: 34,
  amountGradient: [{ offset: '0%', color: '#ffe54c' }, { offset: '24%', color: '#ffdb21' }, { offset: '49%', color: '#ffc00d' }, { offset: '76%', color: '#f99508' }, { offset: '100%', color: '#d45607' }] }

export function NeonNightsView({ controller: c, renderShell, paymentAlerts }: { controller: NeonNightsController } & Pick<NeonNightsGameProps, 'renderShell' | 'paymentAlerts'>) {
  const cabinetRef = useRef<HTMLDivElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  const fallbackPositions = useMemo(() => c.winningPaylines.flatMap(line => line.matches.flatMap(match => match.match.positions)), [c.winningPaylines])
  const highlighted = c.hasCompletedSpin && activePath?.resultKey === c.resultKey ? activePath.positions : fallbackPositions
  const special = c.specialActive || c.discoActive
  const openHelp = () => { c.setIsAutoSpinning(false); c.setIsHelpOpen(true) }
  const openSettings = () => { c.setIsAutoSpinning(false); c.setIsSettingsOpen(true) }
  const canAdmin = (c.demoMode && import.meta.env.DEV) || c.account?.role.toLowerCase() === 'admin'
  const content = <div
    className={`slots-page slots-page--theme-backdrop${special ? ' slots-page--special-game' : ''}${c.isAutoSpinning || c.isStopRequested ? ' slots-page--fast-spin' : ''}`}
    style={c.pageStyle} data-slot-theme={neonCabinet.id} data-slot-controls="sidecar"
    data-slot-celebration="neon-scan" data-special-round="neon-midnight-mix" data-special-earn-style="orbit"
    data-neon-arcade-expanding={c.arcadeExpanding ? 'true' : undefined}
    data-neon-arcade-rows={c.displayedReels[0]?.length === 7 ? 'true' : undefined}
    data-neon-disco-round={c.discoActive ? 'true' : undefined} data-neon-disco-cascade={c.cascadeFrame?.phase}
  >
    <main className="slots-page__main">
      <h1 className="slots-page__accessible-title">Neon Nights</h1>
      <div className="slots-page__layout"><div className="slots-page__stage">
        <img className="slots-page__title-art" {...neonCabinet.titleArtwork} alt="" />
        <div ref={cabinetRef} className={`slots-page__cabinet-spotlight${special ? ' slots-page__cabinet-spotlight--lit' : ''}`}>
          <SlotMachine cabinetTheme={neonCabinet} reelCount={5} renderReel={reelIndex => <div
            className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`} style={c.reelStripStyle(reelIndex)}
          >{c.displayedReels[reelIndex].map((symbol, rowIndex) => {
            const order = highlighted.findIndex(cell => cell.reel === reelIndex && cell.row === rowIndex)
            const cleared = c.cascadeFrame?.cleared.some(cell => cell.reel === reelIndex && cell.row === rowIndex) ?? false
            const dropRows = c.cascadeFrame?.phase === 'drop' ? c.cascadeFrame.dropRows[reelIndex]?.[rowIndex] ?? 0 : 0
            const tile = <SlotSymbol key={`row-${rowIndex}`} symbol={symbol} symbolSet={neonSymbols}
              reelIndex={reelIndex} rowIndex={rowIndex} animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
              highlighted={(c.cascadeFrame?.phase === 'award' && cleared) || order >= 0} highlightOrder={order} />
            return c.cascadeFrame && (cleared || dropRows > 0) ? <div key={`row-${rowIndex}`}
              className={`neon-disco-cascade__cell neon-disco-cascade__cell--${dropRows > 0 ? 'drop' : c.cascadeFrame.phase}`}
              style={dropRows > 0 ? { '--neon-disco-drop-rows': dropRows } as CSSProperties : undefined}>{tile}</div> : tile
          })}</div>} />
          <WinningPaylines appearance={paylineAppearance} boardSelector=".slots-page__cabinet-spotlight"
            reelsSelector=".slot-machine__reels" cellSelector=".slot-symbol[data-reel-index][data-row-index]"
            paylines={c.winningPaylines} resultKey={c.resultKey} visible={c.hasCompletedSpin}
            reducedMotion={c.reducedMotion} formatWinAmount={points => `+${money(points / 100)}`} onActivePathChange={setActivePath} />
        </div>
        <div className="slots-page__playbar" aria-label="Wager and spin controls">
          <SidecarSpinControls activeWager={c.activeWager} disabled={c.spinAvailability !== 'available' || Boolean(c.specialIntro) || Boolean(c.roundSummary) || c.arcadeExpanding}
            freeSpinsRemaining={c.state.freeSpinsRemaining} isAutoSpinning={c.isAutoSpinning} isFreeSpinBadgePopping={false}
            isSpinning={c.isSpinning} isStopRequested={c.isStopRequested} onAutoSpinChange={active => { c.setSpinError(null); c.setIsAutoSpinning(active) }}
            onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick} pointValueInCents={1} showFreeSpinBadge={false}
            specialControlsActive={c.specialActive || c.arcadeExpanding} specialRoundLabel="Special Plays" wagerIndex={c.wagerIndex} wagerOptions={neonRules.wagerOptions} />
          {paymentAlerts}
        </div>
      </div></div>
    </main>
    <SymbolValueGuide symbolSet={neonSymbols} showSidePanel sidePanelClassName="symbol-value-guide--neon" showValueTokens={false}
      wager={c.activeWager} isOpen={c.isSymbolGuideOpen} onOpenChange={c.setIsSymbolGuideOpen} />
    <div className="slots-page__utility-buttons slots-page__utility-buttons--pirates" role="group" aria-label="Game options">
      <button className="slots-page__help-button" type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={c.isHelpOpen} onClick={openHelp}>
        <span aria-hidden="true">?</span><span className="slots-page__help-button-label">How to win</span>
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
    {c.discoActive && <NeonDiscoFloor bpm={100} getPlaybackSeconds={c.getDiscoMusicTime} />}
    {c.specialIntro && !c.isSpinning && <NeonSpecialPlaysIntro cabinetRef={cabinetRef} position={c.specialIntro.position} symbol={c.specialIntro.symbol}
      symbolImage={getSlotSymbolDefinition(neonSymbols, c.specialIntro.symbol).image}
      volume={c.audio.preferences.mode === 'muted' ? 0 : c.audio.preferences.volume / 100} onComplete={() => c.finishSpecialIntro(c.specialIntro!.symbol)} />}
    {c.roundSummary && <NeonRoundSummary cabinetRef={cabinetRef} title={c.roundSummary.mode === 'arcade-rows' ? 'Arcade Plays' : c.roundSummary.mode === 'neon-disco' ? 'Disco Plays' : 'Special Plays'}
      amount={money(c.roundSummary.totalRand)} onDismiss={c.dismissRoundSummary} />}
    {c.winAward && <div key={c.winAward.id}
      className={`slots-page__win-award slots-page__win-award--neon slots-page__win-award--${c.winAward.winTier} slots-page__win-award--${c.winAward.tier}${c.winAward.isFlying ? ' slots-page__win-award--flying' : ''}`}
      aria-hidden="true" style={{ left: c.winAward.left, top: c.winAward.top, animationDuration: `${c.winAward.durationMs}ms`, '--win-travel-x': `${c.winAward.travelX}px`, '--win-travel-y': `${c.winAward.travelY}px` } as CSSProperties}>
      <span>{c.winAward.tier !== 'win' || c.winAward.winTier === 'big' ? 'BIG WIN!' : c.winAward.winTier === 'great' ? 'GREAT WIN!' : 'NICE!'}</span><strong>+{money(c.winAward.displayAmount)}</strong>
    </div>}
    {c.spinError && <footer className="slots-page__footer slots-page__footer--error"><div className="game-outcome game-outcome--error" role="status">{c.spinError}</div></footer>}
    <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
      <span><kbd>Space</kbd> Spin / stop</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
      <button className="slots-page__mute-shortcut" type="button" aria-pressed={c.audio.preferences.mode === 'muted'} onClick={c.audio.toggleMuted}><kbd aria-hidden="true">M</kbd> Mute</button>
    </div>
    <AudioSettingsDialog theme="neon-nights" isOpen={c.isSettingsOpen} onClose={() => c.setIsSettingsOpen(false)} preferences={c.audio.preferences}
      onToggleMuted={c.audio.toggleMuted} onToggleResultsOnly={c.audio.toggleResultsOnly} onVolumeChange={c.audio.setVolume} />
    <WinHelpDialog theme="neon-nights" isOpen={c.isHelpOpen} closeButtonRef={c.helpCloseButtonRef} onClose={() => c.setIsHelpOpen(false)} title="How to win" eyebrow="Neon Nights">
      <NeonHelp />
    </WinHelpDialog>
    <InsufficientBalanceDialog theme="neon-nights" isOpen={c.isReloadPromptOpen} closeButtonRef={c.reloadPromptCloseButtonRef}
      selectedWager={c.activeWager} balance={c.balance} recoveryHref={c.demoMode ? location.pathname : '/balance'} recoveryLabel={c.demoMode ? 'Reset balance' : 'Add Rand'} onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>
  return renderShell ? renderShell({ balance: c.balance, onNavigate: () => c.setIsAutoSpinning(false), children: content }) : content
}
