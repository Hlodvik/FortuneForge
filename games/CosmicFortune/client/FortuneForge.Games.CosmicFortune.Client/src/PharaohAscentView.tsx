import { useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { AudioSettingsDialog, InsufficientBalanceDialog, SidecarSpinControls, SlotMachine,
  SlotSymbol, shouldUseAnimatedSymbol, WinHelpDialog } from '@fortuneforge/slot-client-core'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { COSMIC_FORTUNE_CABINET_THEME as cabinet } from './cabinetTheme'
import { COSMIC_FORTUNE_SYMBOLS as symbols } from './symbols'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'
import { COSMIC_FORTUNE_EXPERIENCE_SET as experience } from './manifest'
import { SealedTombPanel } from './SealedTombPanel'
import { PharaohAscentPanel } from './PharaohAscentPanel'
import { PharaohHelp } from './PharaohHelp'
import { decodeTombState } from './tomb'
import type { PharaohAscentController } from './usePharaohAscent'
import '@fortuneforge/slot-client-core/styles/machine.css'
import '@fortuneforge/slot-client-core/styles/machineOverrides.css'
import '@fortuneforge/slot-client-core/styles/dialogs.css'
import '@fortuneforge/slot-client-core/styles/sidecarSpinControls.css'
import './styles.css'
import './pharaohAscentGame.css'

const money = (amount: number) => new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(amount)

/** The game owns this entire playable screen; the host injects only shell and account ports. */
export function PharaohAscentView({ controller: c, paymentAlerts }: { controller: PharaohAscentController; paymentAlerts?: ReactNode }) {
  const helpClose = useRef<HTMLButtonElement>(null)
  const balanceClose = useRef<HTMLButtonElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  const tomb = decodeTombState(c.state?.gameFeatureState)
  const expeditionVisible = Boolean(tomb?.activeRun || tomb?.lastRun || tomb?.pendingAction || c.pendingGameAction)
  const settled = c.hasCompletedSpin && !c.isSpinning
  const winningCells = useMemo(() => c.winningPaylines.flatMap(line => line.matches.flatMap(match => match.match.positions)), [c.winningPaylines])
  const highlighted = settled && activePath?.resultKey === c.resultKey ? activePath.positions : winningCells
  const openHelp = () => { c.onNavigate(); c.setIsHelpOpen(true) }
  const closeHelp = () => c.setIsHelpOpen(false)
  const status = c.spinAvailability === 'checking' ? 'Opening your saved expedition…'
    : c.spinAvailability === 'unavailable' ? 'Reconnect to continue your saved game.'
    : c.isGameFeatureUpdating ? 'The ancient stones are shifting…'
    : c.isSpinning ? 'Following the scarabs…'
    : c.state?.freeSpinsRemaining ? `${c.state.freeSpinsRemaining} guardian spins · no additional wager`
    : 'Follow the scarabs. Break the seal. Choose your path.'
  const style = { '--pharaoh-backdrop': `url("${art.backdrop}")`, '--slot-symbol-rows': c.visibleRows } as CSSProperties

  return <div className="pharaoh-game" style={style} data-game-id={experience.rules.gameId}
    data-expedition-active={expeditionVisible || undefined} data-game-busy={c.isSpinning || c.isGameFeatureUpdating || undefined}>
    <main className="pharaoh-game__stage">
      <header className="pharaoh-game__brand"><h1 className="pharaoh-game__accessible">Pharaoh’s Ascent</h1>
        <img src={art.wordmark} width={1280} height={280} alt="" />
        <p>Where the reels end, the expedition begins.</p>
      </header>
      <div className="pharaoh-game__cabinet" data-payline-board aria-label="Five Egyptian reels" aria-busy={c.isSpinning}>
        <SlotMachine cabinetTheme={cabinet} reelCount={5} renderReel={reelIndex =>
          <div className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`} style={c.reelStripStyle(reelIndex)}>
            {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
              const order = highlighted.findIndex(cell => cell.reel === reelIndex && cell.row === rowIndex)
              return <SlotSymbol key={rowIndex} symbol={symbol} symbolSet={symbols} reelIndex={reelIndex} rowIndex={rowIndex}
                animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])} highlighted={settled && order >= 0} highlightOrder={order} />
            })}
          </div>} />
        <WinningPaylines appearance={{ classPrefix: 'pharaoh-paylines', variant: 'cabinet', labelOffset: 18, labelMinimumY: 24 }}
          boardSelector="[data-payline-board]" reelsSelector=".slot-machine__reels" cellSelector=".slot-symbol[data-reel-index][data-row-index]"
          paylines={c.winningPaylines} resultKey={c.resultKey} visible={settled} reducedMotion={c.reducedMotion}
          formatWinAmount={points => `+${money(points * experience.rules.pointValueInCents / 100)}`} onActivePathChange={setActivePath} />
      </div>
      <div className="pharaoh-game__controls" aria-label="Wager and spin controls">
        <SidecarSpinControls activeWager={c.activeWager} disabled={c.spinDisabled && !c.isAutoSpinning}
          freeSpinsRemaining={c.state?.freeSpinsRemaining ?? 0} isAutoSpinning={c.isAutoSpinning} isFreeSpinBadgePopping={false}
          isSpinning={c.isSpinning} isStopRequested={c.isStopRequested} onAutoSpinChange={c.setIsAutoSpinning}
          onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick} pointValueInCents={experience.rules.pointValueInCents}
          showFreeSpinBadge={Boolean(c.state?.freeSpinsRemaining)} specialRoundLabel="Guardian spin"
          specialControlsActive={false}
          wagerLocked={c.isWagerLocked || c.controlsDisabled} wagerIndex={c.wagerIndex} wagerOptions={c.wagerOptions} />
        <div className="pharaoh-game__readout" aria-live="polite" aria-atomic="true">
          {c.spinError ? <p className="pharaoh-game__error" role="alert">{c.spinError}
            {c.spinAvailability === 'unavailable' && <button type="button" onClick={c.retryConnection}>Reconnect</button>}</p>
            : settled && c.lastWin > 0 ? <output aria-label="Last spin win">A golden discovery <strong>+{money(c.lastWin)}</strong></output>
              : <p>{status}</p>}
        </div>
      </div>
      <div className="pharaoh-game__features">
        <SealedTombPanel state={c.state?.gameFeatureState ?? null}
          pendingAction={c.pendingGameAction}
          isSpinning={c.isSpinning} reducedMotion={c.reducedMotion} pointValueInCents={experience.rules.pointValueInCents}
          actionPending={c.isGameFeatureUpdating} error={c.gameFeatureError} onAction={c.performGameFeatureAction}
          disabled={c.spinAvailability !== 'available' || c.isHelpOpen || c.isSettingsOpen || c.isReloadPromptOpen} onOpen={c.onNavigate} />
        {!expeditionVisible && <details className="pharaoh-game__guardian-fold" onToggle={event => { if (event.currentTarget.open) c.onNavigate() }}>
          <summary>Guardian seals &amp; Sunfire <span aria-hidden="true">⌄</span></summary>
          <PharaohAscentPanel state={c.state?.gameFeatureState ?? null} feature={c.lastFeature} spinKey={c.resultKey}
            isSpinning={c.isSpinning} reducedMotion={c.reducedMotion} pointValueInCents={experience.rules.pointValueInCents}
            onOpen={c.onNavigate} />
        </details>}
      </div>
      {paymentAlerts && <div className="pharaoh-game__payment-alerts">{paymentAlerts}</div>}
    </main>
    <footer className="pharaoh-game__footer">
      <div className="pharaoh-game__utilities" role="group" aria-label="Game options">
        <button type="button" aria-label="How to win" aria-haspopup="dialog" aria-expanded={c.isHelpOpen} onClick={openHelp}>How to win</button>
        <button type="button" aria-label="Open settings" aria-haspopup="dialog" aria-expanded={c.isSettingsOpen} onClick={() => { c.onNavigate(); c.setIsSettingsOpen(true) }}>Sound</button>
      </div>
      <div className="pharaoh-game__keyboard"><span><kbd>Space</kbd> Spin / stop</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
        <button type="button" aria-pressed={c.audio.preferences.mode === 'muted'} onClick={c.audio.toggleMuted}><kbd>M</kbd> Mute</button>
      </div>
    </footer>
    <WinHelpDialog theme="pharaohs-ascent" isOpen={c.isHelpOpen} closeButtonRef={helpClose} onClose={closeHelp}
      title="A guide to Pharaoh’s Ascent" eyebrow="The Sealed Tomb"><PharaohHelp onClose={closeHelp} /></WinHelpDialog>
    <AudioSettingsDialog theme="pharaohs-ascent" isOpen={c.isSettingsOpen} onClose={() => c.setIsSettingsOpen(false)}
      preferences={c.audio.preferences} onToggleMuted={c.audio.toggleMuted} onToggleResultsOnly={c.audio.toggleResultsOnly} onVolumeChange={c.audio.setVolume} />
    <InsufficientBalanceDialog theme="pharaohs-ascent" isOpen={c.isReloadPromptOpen} closeButtonRef={balanceClose}
      selectedWager={c.activeWager} balance={c.balance} recoveryHref="/balance" recoveryLabel="Add Rand" onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>
}
