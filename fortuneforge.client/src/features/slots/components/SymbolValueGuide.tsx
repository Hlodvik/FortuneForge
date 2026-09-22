import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { getSlotSymbolDefinition, type SlotSymbolSet } from '../config/symbolSets'

function SymbolValuePanel({
  className,
  headingId,
  onClose,
  showValueTokens,
  symbolSet,
}: {
  className: string
  headingId?: string
  onClose?: () => void
  showValueTokens: boolean
  symbolSet: SlotSymbolSet
}) {
  const guideEntries = showValueTokens
    ? symbolSet.guideEntries
    : symbolSet.guideEntries.filter(({ symbol }) => !getSlotSymbolDefinition(symbolSet, symbol).wagerMultiplier)
  return (
    <aside
      className={`symbol-value-guide ${className}`}
      aria-label={headingId ? undefined : 'Symbol values'}
      aria-labelledby={headingId}
      aria-modal={headingId ? true : undefined}
      role={headingId ? 'dialog' : undefined}
    >
      {onClose && (
        <button
          className="symbol-value-guide__close"
          type="button"
          aria-label="Close symbol values"
          autoFocus
          onClick={onClose}
        >
          ×
        </button>
      )}
      <div className="symbol-value-guide__heading">
        <strong id={headingId}>Symbol values</strong>
        <span>3 in a row · 5 in a row</span>
      </div>
      <div className="symbol-value-guide__grid">
        {guideEntries.map(({
          symbol,
          firstLabel,
          firstValue,
          secondLabel,
          secondValue,
          artworkValueLabel,
        }) => {
          const definition = getSlotSymbolDefinition(symbolSet, symbol)
          return (
            <div
              className={`symbol-value-guide__item symbol-value-guide__item--${symbol.toLowerCase()}`}
              key={symbol}
              title={definition.label}
            >
              <div
                className="symbol-value-guide__artwork"
                data-value-label={artworkValueLabel ?? definition.valueLabel}
              >
                <img src={definition.image} alt="" aria-hidden="true" />
              </div>
              <span className="symbol-value-guide__name">{definition.label}</span>
              <span><b>{firstLabel}</b> {firstValue}</span>
              <span><b>{secondLabel}</b> {secondValue}</span>
            </div>
          )
        })}
      </div>
      {showValueTokens && <small>Rand values multiply the selected wager.</small>}
    </aside>
  )
}

export function SymbolValueGuide({
  symbolSet,
  showSidePanel = false,
  sidePanelClassName = '',
  showValueTokens = true,
}: {
  symbolSet: SlotSymbolSet
  showSidePanel?: boolean
  sidePanelClassName?: string
  showValueTokens?: boolean
}) {
  const [isOpen, setIsOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)

  const closeGuide = () => {
    setIsOpen(false)
    window.requestAnimationFrame(() => triggerRef.current?.focus())
  }

  useEffect(() => {
    if (!isOpen) {
      return
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeGuide()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [isOpen])

  return (
    <>
      {showSidePanel && createPortal(
        <SymbolValuePanel
          className={`symbol-value-guide--side ${sidePanelClassName}`.trim()}
          showValueTokens={showValueTokens}
          symbolSet={symbolSet}
        />,
        document.body,
      )}

      <button
        ref={triggerRef}
        className="symbol-value-guide__trigger"
        type="button"
        aria-expanded={isOpen}
        aria-haspopup="dialog"
        onClick={() => setIsOpen(true)}
      >
        <span aria-hidden="true">◆</span>
        Symbol values
      </button>

      {isOpen && (
        <div
          className="symbol-value-guide__backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) {
              closeGuide()
            }
          }}
        >
          <SymbolValuePanel
            className="symbol-value-guide--modal"
            headingId="symbol-value-guide-title"
            onClose={closeGuide}
            showValueTokens={showValueTokens}
            symbolSet={symbolSet}
          />
        </div>
      )}
    </>
  )
}
