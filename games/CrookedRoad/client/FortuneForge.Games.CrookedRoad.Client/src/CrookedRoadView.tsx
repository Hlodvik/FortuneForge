import { useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import {
  AudioSettingsDialog, InsufficientBalanceDialog, SidecarSpinControls, SlotMachine, SlotSymbol,
  shouldUseAnimatedSymbol, WinHelpDialog,
} from '@fortuneforge/slot-client-core'
import { WinningPaylines, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import { CrookedRoadHelp } from './CrookedRoadHelp'
import { CrookedRoadPanel } from './CrookedRoadPanel'
import { crookedRoadCabinet, crookedRoadRules, crookedRoadSymbols } from './manifest'
import { ROAD_DESTINATIONS, type RoadBonus } from './road'
import { CROOKED_ROAD_ARTWORK } from './symbols'
import type { CrookedRoadController } from './useCrookedRoad'
import '@fortuneforge/slot-client-core/styles.css'
import './styles.css'
import './crookedRoadGame.css'

const money = (amount: number) => new Intl.NumberFormat('en-ZA', {
  style: 'currency', currency: 'ZAR', minimumFractionDigits: 2, maximumFractionDigits: 2,
}).format(amount)

const paylineAppearance = {
  classPrefix: 'crooked-road-paylines', variant: 'crooked-road', labelOffset: 22, labelMinimumY: 30,
  amountGradient: [
    { offset: '0%', color: '#fff5c5' }, { offset: '45%', color: '#f2d27e' }, { offset: '100%', color: '#b98935' },
  ],
}

export type CrookedRoadViewProps = {
  controller: CrookedRoadController
  paymentAlerts?: ReactNode
  balanceRecoveryHref?: string
}

/** The paid game's screen. The host supplies its shell and authenticated ports. */
export function CrookedRoadView({ controller: c, paymentAlerts, balanceRecoveryHref = '/balance' }: CrookedRoadViewProps) {
  const helpClose = useRef<HTMLButtonElement>(null)
  const balanceClose = useRef<HTMLButtonElement>(null)
  const [activePath, setActivePath] = useState<ActivePaidPath | null>(null)
  const activeBonus: RoadBonus | null = c.state?.gameFeatureState?.activeBonus ?? null
  const reviewingJourney = !activeBonus && c.isFeaturePresenting && c.lastFeature?.completed === true
  const journeyVisible = activeBonus !== null || reviewingJourney
  const nativeTakeover = journeyVisible && (activeBonus?.kind ?? c.lastFeature?.kind) !== 'well'
  const settled = c.hasCompletedSpin && !c.isSpinning
  const winningCells = useMemo(() => c.winningPaylines.flatMap(line =>
    line.matches.flatMap(match => match.match.positions)), [c.winningPaylines])
  const highlighted = settled && activePath?.resultKey === c.resultKey ? activePath.positions : winningCells
  const closeHelp = () => c.setIsHelpOpen(false)
  const openHelp = () => { c.onNavigate(); c.setIsHelpOpen(true) }
  const openSettings = () => { c.onNavigate(); c.setIsSettingsOpen(true) }
  const choiceRequired = activeBonus && activeBonus.choice === null &&
    (activeBonus.kind === 'hill' || activeBonus.kind === 'well' && activeBonus.step === 0)
  const status = c.spinAvailability === 'checking' ? 'Checking your saved road…'
    : c.spinAvailability === 'unavailable' ? 'The game is unavailable. Your saved road is safe.'
    : choiceRequired ? `Choose ${activeBonus.kind === 'well' ? 'a spring' : 'a door'} before using Spin.`
    : reviewingJourney ? 'Review your journey, then return to the road.'
    : c.isGameFeatureUpdating ? 'Saving your road…'
    : c.isSpinning ? 'Finbar is following his fortune…'
    : activeBonus ? `${ROAD_DESTINATIONS[activeBonus.kind].label} · No additional wager`
    : null
  const pageStyle = {
    '--slot-page-backdrop': `url("${CROOKED_ROAD_ARTWORK.backgrounds.village}")`,
    '--slot-symbol-rows': 3,
  } as CSSProperties

  return <div className={`slots-page slots-page--theme-backdrop crooked-road-game${c.isAutoSpinning || c.isStopRequested ? ' slots-page--fast-spin' : ''}`}
    style={pageStyle} data-slot-theme={crookedRoadCabinet.id} data-slot-controls="sidecar"
    data-slot-celebration="crooked-road-clover" data-game-id={crookedRoadRules.gameId}
    data-game-busy={c.isSpinning || c.isGameFeatureUpdating || undefined}>
    <main className="slots-page__main">
      <h1 className="slots-page__accessible-title">The Crooked Road</h1>
      <div className="slots-page__layout"><div className="slots-page__stage">
        <img className="slots-page__title-art" src={CROOKED_ROAD_ARTWORK.wordmark} width={960} height={480} alt="" />
        <div className="slots-page__cabinet-spotlight" data-payline-board aria-label="Five reels with three rows" aria-busy={c.isSpinning}>
          <SlotMachine cabinetTheme={crookedRoadCabinet} reelCount={crookedRoadRules.initialReels.length}
            renderReel={reelIndex => <div className={`slot-reel__symbols slot-reel__symbols--${c.reelMotion[reelIndex]}`}
              style={c.reelStripStyle(reelIndex)}>
              {c.displayedReels[reelIndex].map((symbol, rowIndex) => {
                const order = highlighted.findIndex(cell => cell.reel === reelIndex && cell.row === rowIndex)
                return <SlotSymbol key={rowIndex} symbol={symbol} symbolSet={crookedRoadSymbols}
                  reelIndex={reelIndex} rowIndex={rowIndex}
                  animated={shouldUseAnimatedSymbol(c.reducedMotion, c.reelMotion[reelIndex])}
                  highlighted={settled && order >= 0} highlightOrder={order} />
              })}
            </div>} />
          <WinningPaylines appearance={paylineAppearance} boardSelector="[data-payline-board]"
            reelsSelector=".slot-machine__reels" cellSelector=".slot-symbol[data-reel-index][data-row-index]"
            paylines={c.winningPaylines} resultKey={c.resultKey} visible={settled} reducedMotion={c.reducedMotion}
            formatWinAmount={points => `+${money(points * crookedRoadRules.pointValueInCents / 100)}`}
            onActivePathChange={setActivePath} />
        </div>
        <div className="slots-page__playbar" aria-label="Wager and spin controls">
          <SidecarSpinControls activeWager={c.activeWager} disabled={c.spinDisabled && !c.isAutoSpinning}
            menuClassName="crooked-road-game__bet-menu"
            freeSpinsRemaining={c.state?.freeSpinsRemaining ?? 0} isAutoSpinning={c.isAutoSpinning}
            isFreeSpinBadgePopping={false} isSpinning={c.isSpinning} isStopRequested={c.isStopRequested}
            onAutoSpinChange={c.setIsAutoSpinning} onSelectWager={c.selectWager} onSpin={c.handleSpinButtonClick}
            pointValueInCents={crookedRoadRules.pointValueInCents} showFreeSpinBadge={false}
            specialControlsActive={nativeTakeover} specialRoundLabel="Journey spin"
            wagerLocked={c.isWagerLocked || c.controlsDisabled} wagerIndex={c.wagerIndex} wagerOptions={c.wagerOptions} />
          <div className="crooked-road-game__readout" aria-live="polite" aria-atomic="true">
            {c.spinError ? <p className="crooked-road-game__error" role="alert">{c.spinError}
              {c.spinAvailability === 'unavailable' && <button type="button" onClick={() => { void c.retryConnection() }}>Reconnect</button>}</p>
              : status ? <p className="crooked-road-game__status">{status}</p>
              : settled && c.lastWin > 0 ? <output className="crooked-road-game__win" aria-label="Last spin win">A fortunate find <strong>+{money(c.lastWin)}</strong></output>
              : <p className="crooked-road-game__status">Spin the reels. Shape the road. Follow your fortune.</p>}
          </div>
        </div>
        <CrookedRoadPanel state={c.state?.gameFeatureState ?? null} feature={c.lastFeature} spinKey={c.spinKey}
          isSpinning={c.isSpinning} reducedMotion={c.reducedMotion} pointValueInCents={crookedRoadRules.pointValueInCents}
          actionPending={c.isGameFeatureUpdating} error={c.gameFeatureError} onAction={c.performGameFeatureAction}
          onPresentationBusyChange={c.setFeaturePresenting}
          disabled={c.spinAvailability !== 'available' || c.isHelpOpen || c.isSettingsOpen || c.isReloadPromptOpen} onOpen={c.onNavigate} />
        {paymentAlerts && <div className="slots-page__local-controls crooked-road-game__payment-alerts">{paymentAlerts}</div>}
      </div></div>
    </main>
    <div className="slots-page__utility-buttons" role="group" aria-label="Game options">
      <button className="slots-page__help-button" type="button" aria-label="How to win" aria-haspopup="dialog"
        aria-expanded={c.isHelpOpen} onClick={openHelp}><span aria-hidden="true">?</span></button>
      <button className="slots-page__settings-button" type="button" aria-label="Open settings" aria-haspopup="dialog"
        aria-expanded={c.isSettingsOpen} onClick={openSettings}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m9 4 .8-2h4.4L15 4l2 .9 2.1-.2 2.2 3.8-1.3 1.8v2.3l1.3 1.8-2.2 3.8-2.1-.2-2 .9-.8 2H9.8l-.8-2-2-.9-2.1.2L2.7 14.4 4 12.6v-2.3L2.7 8.5l2.2-3.8 2.1.2Z" /><circle cx="12" cy="11.5" r="3" />
        </svg>
      </button>
    </div>
    {settled && c.lastWin > 0 && !journeyVisible && <div key={c.resultKey} className="crooked-road-game__win-sparks" aria-hidden="true">☘ ✦ ☘ ✦ ☘</div>}
    <div className="slots-page__keyboard-help" role="group" aria-label="Keyboard controls">
      <span><kbd>Space</kbd> Spin / stop</span><span><kbd>←</kbd><kbd>→</kbd> Wager</span>
      <button className="slots-page__mute-shortcut" type="button" aria-pressed={c.audio.preferences.mode === 'muted'} onClick={c.audio.toggleMuted}>
        <kbd aria-hidden="true">M</kbd> Mute
      </button>
    </div>
    <AudioSettingsDialog theme="crooked-road" isOpen={c.isSettingsOpen} onClose={() => c.setIsSettingsOpen(false)}
      preferences={c.audio.preferences} onToggleMuted={c.audio.toggleMuted}
      onToggleResultsOnly={c.audio.toggleResultsOnly} onVolumeChange={c.audio.setVolume}>
      <p className="crooked-road-game__settings-note">Original harp, bell and woodland sounds accompany Finbar’s road. Your sound preferences stay on this device.</p>
    </AudioSettingsDialog>
    <WinHelpDialog theme="crooked-road" isOpen={c.isHelpOpen} closeButtonRef={helpClose} onClose={closeHelp}
      title="A guide to The Crooked Road" eyebrow="Reels, roads and Otherworld journeys">
      <CrookedRoadHelp onClose={closeHelp} />
    </WinHelpDialog>
    <InsufficientBalanceDialog theme="crooked-road" isOpen={c.isReloadPromptOpen} closeButtonRef={balanceClose}
      selectedWager={c.activeWager} balance={c.balance} recoveryHref={balanceRecoveryHref} recoveryLabel="Add Rand"
      onClose={() => c.setIsReloadPromptOpen(false)} />
  </div>
}
