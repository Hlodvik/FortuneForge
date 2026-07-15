import { useState } from 'react'
import {
  finishSpinAnimation,
  startSpinAnimation,
  stopReelAnimation,
  type ReelMotionState,
} from './animation/slotAnimation'
import { SlotMachine, SlotSymbol } from './components'
import { requestSpin } from './services/slotsApi'
import type { SlotSymbolId } from './types/slots'

const initialReels: readonly (readonly SlotSymbolId[])[] = [
  ['2', '3', '4', '5'],
  ['3', '4', '5', '6'],
  ['4', '5', '6', '7'],
  ['5', '6', '7', 'ACE'],
  ['6', '7', 'ACE', '2'],
  ['7', 'ACE', '2', '3'],
] as const

export function SlotsPage() {
  const [displayedReels, setDisplayedReels] = useState<SlotSymbolId[][]>(() =>
    initialReels.map((reel) => [...reel]),
  )
  const [reelMotion, setReelMotion] = useState<ReelMotionState[]>(() =>
    initialReels.map(() => 'idle'),
  )
  const [isSpinning, setIsSpinning] = useState(false)
  const [spinStage, setSpinStage] = useState<'requesting' | 'stopping'>('requesting')
  const [spinError, setSpinError] = useState<string | null>(null)

  async function handleSpin() {
    if (isSpinning) {
      return
    }

    setIsSpinning(true)
    setSpinStage('requesting')
    setSpinError(null)
    const reelsBeforeSpin = displayedReels.map((reel) => [...reel])
    const displayFrame = (reelIndex: number, symbols: readonly SlotSymbolId[]) => {
      setDisplayedReels((currentReels) =>
        currentReels.map((reel, index) =>
          index === reelIndex ? [...symbols] : reel,
        ),
      )
    }
    const animation = startSpinAnimation({
      reelCount: displayedReels.length,
      rowsPerReel: displayedReels[0]?.length ?? 4,
      displayFrame,
      setReelMotion: (reelIndex, state) => {
        setReelMotion((currentMotion) =>
          currentMotion.map((currentState, index) =>
            index === reelIndex ? state : currentState,
          ),
        )
      },
    })

    try {
      const result = await requestSpin({
        gameId: 'classic-demo-v1',
        wagerPoints: 50,
      })

      if (result.reels.length !== displayedReels.length) {
        throw new Error(`Expected ${displayedReels.length} reels but received ${result.reels.length}.`)
      }

      setSpinStage('stopping')
      for (let reelIndex = 0; reelIndex < result.reels.length; reelIndex++) {
        await stopReelAnimation(animation, {
          reelIndex,
          stopIndex: result.reelStops[reelIndex],
          targetSymbols: result.reels[reelIndex],
        })
      }
    } catch (error) {
      setDisplayedReels(reelsBeforeSpin)
      setSpinError(error instanceof Error ? error.message : 'The spin could not be completed.')
    } finally {
      finishSpinAnimation(animation)
      setIsSpinning(false)
    }
  }

  return (
    <div className="slots-page">
      <header className="slots-page__topbar">
        <div className="slots-page__brand">
          <span className="slots-page__brand-name">FortuneForge</span>
          <span className="slots-page__brand-mode">Slots preview</span>
        </div>

        <div className="slots-page__balance" aria-label="Preview balance: 10,000 points">
          <span className="slots-page__balance-label">Points</span>
          <span className="slots-page__balance-value">10,000</span>
        </div>
      </header>

      <main className="slots-page__main">
        <div className="slots-page__stage">
          <SlotMachine
            reelCount={displayedReels.length}
            renderReel={(reelIndex) => (
              <div className={`slot-reel__symbols slot-reel__symbols--${reelMotion[reelIndex]}`}>
                {displayedReels[reelIndex].map((symbol, rowIndex) => (
                  <SlotSymbol key={`${symbol}-${rowIndex}`} symbol={symbol} />
                ))}
              </div>
            )}
            isSpinning={isSpinning}
            onSpin={() => void handleSpin()}
          />
        </div>
      </main>

      <footer
        className={`slots-page__footer${spinError ? ' slots-page__footer--error' : ''}`}
        aria-live="polite"
      >
        {spinError
          ?? (isSpinning
            ? spinStage === 'requesting'
              ? 'Reels spinning — requesting result'
              : 'Result received — stopping reels'
            : 'Ready to spin')}
      </footer>
    </div>
  )
}
