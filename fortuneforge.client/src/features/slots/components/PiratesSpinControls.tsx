import { useEffect, useRef, useState } from 'react'
import { formatRand, slotPointsToRand } from '../slotPagePresentation'
import { SpinButton } from './SpinButton'

type PiratesSpinControlsProps = {
  activeWager: number
  disabled: boolean
  freeSpinsRemaining: number
  isAutoSpinning: boolean
  isFreeSpinBadgePopping: boolean
  isSpinning: boolean
  isStopRequested: boolean
  onAutoSpinChange: (active: boolean) => void
  onSelectWager: (index: number) => void
  onSpin: () => void
  pointValueInCents: number
  showFreeSpinBadge: boolean
  specialControlsActive: boolean
  specialRoundLabel: string | null
  wagerFirst?: boolean
  wagerIndex: number
  wagerOptions: readonly number[]
}

export function PiratesSpinControls({
  activeWager,
  disabled,
  freeSpinsRemaining,
  isAutoSpinning,
  isFreeSpinBadgePopping,
  isSpinning,
  isStopRequested,
  onAutoSpinChange,
  onSelectWager,
  onSpin,
  pointValueInCents,
  showFreeSpinBadge,
  specialControlsActive,
  specialRoundLabel,
  wagerFirst = false,
  wagerIndex,
  wagerOptions,
}: PiratesSpinControlsProps) {
  const [isBetOpen, setIsBetOpen] = useState(false)
  const betMenuRef = useRef<HTMLDivElement>(null)
  const betLocked = isSpinning || freeSpinsRemaining > 0 || specialControlsActive
  const minWager = slotPointsToRand(wagerOptions[0] ?? 0, pointValueInCents)
  const maxWager = slotPointsToRand(wagerOptions[wagerOptions.length - 1] ?? 0, pointValueInCents)

  useEffect(() => {
    if (betLocked) setIsBetOpen(false)
  }, [betLocked])

  useEffect(() => {
    if (!isBetOpen) return undefined

    const closeOutside = (event: PointerEvent) => {
      if (!betMenuRef.current?.contains(event.target as Node)) setIsBetOpen(false)
    }
    const closeEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setIsBetOpen(false)
    }
    document.addEventListener('pointerdown', closeOutside)
    document.addEventListener('keydown', closeEscape)
    return () => {
      document.removeEventListener('pointerdown', closeOutside)
      document.removeEventListener('keydown', closeEscape)
    }
  }, [isBetOpen])

  const autoSpinButton = (
    <button
      className={`pirates-spin-controls__small${isAutoSpinning ? ' pirates-spin-controls__small--active' : ''}`}
      type="button"
      disabled={disabled}
      aria-label={isAutoSpinning ? 'Stop autospin' : 'Start autospin'}
      aria-pressed={isAutoSpinning}
      onClick={() => {
        setIsBetOpen(false)
        onAutoSpinChange(!isAutoSpinning)
      }}
    >
      <svg viewBox="0 0 48 48" aria-hidden="true">
        <path d="M37 19a14 14 0 0 0-23-6l-3 3M11 11v6h6M11 29a14 14 0 0 0 23 6l3-3M37 37v-6h-6" />
        <path d="M21 18v12l9-6Z" />
      </svg>
    </button>
  )

  return (
    <div className="pirates-spin-controls" aria-label="Autospin, bet, and spin controls" data-wager-first={wagerFirst || undefined}>
      {!specialControlsActive && (
        <>
          {!wagerFirst && autoSpinButton}
          <div className="pirates-spin-controls__bet-wrap" ref={betMenuRef}>
            <button
              className={`pirates-spin-controls__small${isBetOpen ? ' pirates-spin-controls__small--active' : ''}`}
              type="button"
              aria-label={`Change bet, currently ${formatRand(activeWager)}`}
              aria-haspopup="dialog"
              aria-expanded={isBetOpen}
              disabled={betLocked}
              onClick={() => {
                onAutoSpinChange(false)
                setIsBetOpen((open) => !open)
              }}
            >
              <svg viewBox="0 0 48 48" aria-hidden="true" className="pirates-spin-controls__coins">
                <ellipse cx="27" cy="16" rx="12" ry="4.5" />
                {/* Draw only the parts of the right stack visible behind the left stack. */}
                <path d="M15 16v8M39 16v11c0 2.5-5.4 4.5-12 4.5M39 22c0 2.5-5.4 4.5-12 4.5h-.7" />
                <ellipse cx="16" cy="28" rx="11" ry="4" />
                <path d="M5 28v9c0 2.2 4.9 4 11 4s11-1.8 11-4v-9M5 33c0 2.2 4.9 4 11 4s11-1.8 11-4" />
              </svg>
            </button>
            {isBetOpen && (
              <div className="pirates-spin-controls__bet-menu" role="dialog" aria-label="Choose bet">
                <span className="pirates-spin-controls__bet-heading">BET</span>
                <div className="pirates-spin-controls__bet-stepper">
                  <button type="button" aria-label="Decrease bet" disabled={wagerIndex === 0}
                    onClick={() => onSelectWager(wagerIndex - 1)}>−</button>
                  <output aria-live="polite">{formatRand(activeWager)}</output>
                  <button type="button" aria-label="Increase bet" disabled={wagerIndex === wagerOptions.length - 1}
                    onClick={() => onSelectWager(wagerIndex + 1)}>+</button>
                </div>
                <input
                  type="range"
                  aria-label="Bet amount"
                  min={0}
                  max={Math.max(0, wagerOptions.length - 1)}
                  value={wagerIndex}
                  onChange={(event) => onSelectWager(Number(event.currentTarget.value))}
                />
                <div className="pirates-spin-controls__bet-limits"><span>{formatRand(minWager)}</span><span>{formatRand(maxWager)}</span></div>
              </div>
            )}
          </div>
          {wagerFirst && autoSpinButton}
        </>
      )}

      <div className="pirates-spin-controls__spin">
        <div className="slots-page__spin-button-shell">
          <SpinButton
            disabled={disabled}
            isSpinning={isSpinning}
            isStopRequested={isStopRequested}
            onSpin={() => {
              setIsBetOpen(false)
              onSpin()
            }}
            variant="pirate-loop"
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
      </div>
      {!specialControlsActive && <div className="pirates-spin-controls__bet-readout">BET <strong>{formatRand(activeWager)}</strong></div>}
    </div>
  )
}
