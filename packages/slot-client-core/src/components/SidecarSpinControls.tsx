import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { formatRand, slotPointsToRand } from '../slotPagePresentation'
import { SpinButton } from './SpinButton'

type SidecarSpinControlsProps = {
  activeWager: number
  disabled: boolean
  freeSpinsRemaining: number
  isAutoSpinning: boolean
  isFreeSpinBadgePopping: boolean
  isSpinning: boolean
  isStopRequested: boolean
  allowStop?: boolean
  autoSpinReplacement?: ReactNode
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

export function SidecarSpinControls({
  activeWager,
  disabled,
  freeSpinsRemaining,
  isAutoSpinning,
  isFreeSpinBadgePopping,
  isSpinning,
  isStopRequested,
  allowStop = true,
  autoSpinReplacement,
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
}: SidecarSpinControlsProps) {
  const [isBetOpen, setIsBetOpen] = useState(false)
  const controlsRef = useRef<HTMLDivElement>(null)
  const betButtonRef = useRef<HTMLButtonElement>(null)
  const betMenuRef = useRef<HTMLDivElement>(null)
  const betMenuId = useId()
  const betLocked = isSpinning || freeSpinsRemaining > 0 || specialControlsActive
  const minWager = slotPointsToRand(wagerOptions[0] ?? 0, pointValueInCents)
  const maxWager = slotPointsToRand(wagerOptions[wagerOptions.length - 1] ?? 0, pointValueInCents)

  useEffect(() => {
    if (betLocked) setIsBetOpen(false)
  }, [betLocked])

  useLayoutEffect(() => {
    const controls = controlsRef.current
    const button = betButtonRef.current
    const menu = betMenuRef.current
    if (!isBetOpen || !controls || !button || !menu) return

    const viewport = window.visualViewport
    const navbar = document.querySelector('.site-navbar')
    const positionMenu = () => {
      const anchor = button.getBoundingClientRect()
      const viewportLeft = viewport?.offsetLeft ?? 0
      const viewportTop = viewport?.offsetTop ?? 0
      const viewportWidth = viewport?.width ?? window.innerWidth
      const viewportHeight = viewport?.height ?? window.innerHeight
      const margin = 12
      const gap = 8
      const minLeft = viewportLeft + margin
      const minTop = Math.max(viewportTop, navbar?.getBoundingClientRect().bottom ?? 0) + margin
      const maxRight = viewportLeft + viewportWidth - margin
      const maxBottom = viewportTop + viewportHeight - margin

      menu.style.setProperty('--sidecar-bet-viewport-width', `${viewportWidth}px`)
      menu.style.maxHeight = `${Math.max(0, maxBottom - minTop)}px`
      const { width, height } = menu.getBoundingClientRect()
      const leftOfButton = anchor.left - gap - width
      const opensBeside = getComputedStyle(controls).getPropertyValue('--sidecar-bet-placement').trim() === 'left' &&
        leftOfButton >= minLeft

      let left = leftOfButton
      let top = anchor.top + (anchor.height - height) / 2
      if (!opensBeside) {
        const above = Math.max(0, anchor.top - gap - minTop)
        const below = Math.max(0, maxBottom - anchor.bottom - gap)
        const opensAbove = above >= height || above >= below
        const available = opensAbove ? above : below
        menu.style.maxHeight = `${available}px`
        left = anchor.left + (anchor.width - width) / 2
        top = opensAbove ? anchor.top - gap - Math.min(height, available) : anchor.bottom + gap
      }

      menu.style.left = `${Math.max(minLeft, Math.min(left, maxRight - width))}px`
      menu.style.top = `${Math.max(minTop, Math.min(top, maxBottom - menu.getBoundingClientRect().height))}px`
    }

    const repositionOnScroll = (event: Event) => {
      if (event.target instanceof Node && menu.contains(event.target)) return
      positionMenu()
    }
    const closeOutside = (event: Event) => {
      if (event.target instanceof Node && !button.contains(event.target) && !menu.contains(event.target)) {
        setIsBetOpen(false)
      }
    }
    const closeEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      setIsBetOpen(false)
      button.focus({ preventScroll: true })
    }
    positionMenu()
    menu.querySelector<HTMLElement>('button:not(:disabled), input')?.focus({ preventScroll: true })
    window.addEventListener('resize', positionMenu)
    window.addEventListener('scroll', repositionOnScroll, true)
    viewport?.addEventListener('resize', positionMenu)
    viewport?.addEventListener('scroll', positionMenu)
    document.addEventListener('pointerdown', closeOutside, true)
    document.addEventListener('focusin', closeOutside)
    document.addEventListener('keydown', closeEscape)
    return () => {
      window.removeEventListener('resize', positionMenu)
      window.removeEventListener('scroll', repositionOnScroll, true)
      viewport?.removeEventListener('resize', positionMenu)
      viewport?.removeEventListener('scroll', positionMenu)
      document.removeEventListener('pointerdown', closeOutside, true)
      document.removeEventListener('focusin', closeOutside)
      document.removeEventListener('keydown', closeEscape)
    }
  }, [isBetOpen])

  const autoSpinControl = autoSpinReplacement ?? (
    <button
      className={`sidecar-spin-controls__small${isAutoSpinning ? ' sidecar-spin-controls__small--active' : ''}`}
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
    <div className="sidecar-spin-controls" ref={controlsRef}
      aria-label={specialControlsActive ? 'Spin controls' : autoSpinReplacement != null ? 'Bet and spin controls' : 'Autospin, bet, and spin controls'}
      data-wager-first={wagerFirst || undefined}>
      {specialControlsActive ? autoSpinReplacement : (
        <>
          {!wagerFirst && autoSpinControl}
          <div className="sidecar-spin-controls__bet-wrap">
            <button
              ref={betButtonRef}
              className={`sidecar-spin-controls__small${isBetOpen ? ' sidecar-spin-controls__small--active' : ''}`}
              type="button"
              aria-label={`Change bet, currently ${formatRand(activeWager)}`}
              aria-haspopup="dialog"
              aria-expanded={isBetOpen}
              aria-controls={isBetOpen ? betMenuId : undefined}
              disabled={betLocked}
              onClick={() => {
                onAutoSpinChange(false)
                setIsBetOpen((open) => !open)
              }}
            >
              <svg viewBox="0 0 48 48" aria-hidden="true" className="sidecar-spin-controls__coins">
                <ellipse cx="27" cy="16" rx="12" ry="4.5" />
                {/* Draw only the parts of the right stack visible behind the left stack. */}
                <path d="M15 16v8M39 16v11c0 2.5-5.4 4.5-12 4.5M39 22c0 2.5-5.4 4.5-12 4.5h-.7" />
                <ellipse cx="16" cy="28" rx="11" ry="4" />
                <path d="M5 28v9c0 2.2 4.9 4 11 4s11-1.8 11-4v-9M5 33c0 2.2 4.9 4 11 4s11-1.8 11-4" />
              </svg>
            </button>
            {isBetOpen && !betLocked && createPortal(
              <div
                className="sidecar-spin-controls__bet-menu"
                id={betMenuId}
                ref={betMenuRef}
                role="dialog"
                aria-label="Choose bet"
                onKeyDown={(event) => {
                  if (event.key !== 'Tab') return
                  const fields = event.currentTarget.querySelectorAll('button:not(:disabled), input')
                  const boundary = event.shiftKey ? fields[0] : fields[fields.length - 1]
                  if (event.target !== boundary) return
                  if (event.shiftKey) event.preventDefault()
                  // Resume the page's tab order from the trigger outside the portal.
                  betButtonRef.current?.focus({ preventScroll: true })
                  setIsBetOpen(false)
                }}
              >
                <span className="sidecar-spin-controls__bet-heading">BET</span>
                <div className="sidecar-spin-controls__bet-stepper">
                  <button type="button" aria-label="Decrease bet" disabled={wagerIndex === 0}
                    onClick={() => onSelectWager(wagerIndex - 1)}>
                    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true">
                      <path d="M4 10h12" />
                    </svg>
                  </button>
                  <output aria-live="polite">{formatRand(activeWager)}</output>
                  <button type="button" aria-label="Increase bet" disabled={wagerIndex === wagerOptions.length - 1}
                    onClick={() => onSelectWager(wagerIndex + 1)}>
                    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true">
                      <path d="M4 10h12M10 4v12" />
                    </svg>
                  </button>
                </div>
                <input
                  type="range"
                  aria-label="Bet amount"
                  min={0}
                  max={Math.max(0, wagerOptions.length - 1)}
                  value={wagerIndex}
                  onChange={(event) => onSelectWager(Number(event.currentTarget.value))}
                />
                <div className="sidecar-spin-controls__bet-limits"><span>{formatRand(minWager)}</span><span>{formatRand(maxWager)}</span></div>
              </div>,
              document.body,
            )}
          </div>
          {wagerFirst && autoSpinControl}
        </>
      )}

      <div className="sidecar-spin-controls__spin">
        <div className="slots-page__spin-button-shell">
          <SpinButton
            disabled={disabled}
            isSpinning={isSpinning}
            isStopRequested={isStopRequested}
            allowStop={allowStop}
            onSpin={() => {
              setIsBetOpen(false)
              onSpin()
            }}
            variant="sidecar-loop"
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
      {!specialControlsActive && <div className="sidecar-spin-controls__bet-readout">BET <strong>{formatRand(activeWager)}</strong></div>}
    </div>
  )
}
