import { ResponsiveSlotImage } from './ResponsiveSlotImage'
import { useCallback, useRef, useState, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { getSlotSymbolDefinition, getSlotSymbolImageSrcSet, type SlotSymbolSet } from '../config/symbolSets'
import { useSlotDialog, type SlotDialogTheme } from '../hooks/useSlotDialog'
import { SlotDialogCloseIcon } from './SlotDialogCloseIcon'

function SymbolValuePanel({
  className,
  headingId,
  onClose,
  showValueTokens,
  symbolSet,
  panelRef,
  theme,
}: {
  className: string
  headingId?: string
  onClose?: () => void
  showValueTokens: boolean
  symbolSet: SlotSymbolSet
  panelRef?: RefObject<HTMLElement | null>
  theme?: SlotDialogTheme
}) {
  const guideEntries = showValueTokens
    ? symbolSet.guideEntries
    : symbolSet.guideEntries.filter(({ symbol }) => !getSlotSymbolDefinition(symbolSet, symbol).wagerMultiplier)
  return (
    <aside
      ref={panelRef}
      tabIndex={headingId ? -1 : undefined}
      data-slot-dialog-theme={headingId ? theme : undefined}
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
          onClick={onClose}
        >
          <SlotDialogCloseIcon />
        </button>
      )}
      <div className="symbol-value-guide__heading">
        <strong id={headingId}>Symbol values</strong>
        <span>{symbolSet.guideSubtitle ?? '3 in a row · 5 in a row'}</span>
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
                <ResponsiveSlotImage imageSrcSet={getSlotSymbolImageSrcSet(definition.responsiveImages)} src={definition.image} alt="" aria-hidden="true" />
              </div>
              <span className="symbol-value-guide__name">{definition.label}</span>
              <span><b>{firstLabel}</b> {firstValue}</span>
              <span><b>{secondLabel}</b> {secondValue}</span>
            </div>
          )
        })}
      </div>
      {symbolSet.guideNote && <small>{symbolSet.guideNote}</small>}
      {showValueTokens && <small>Rand values multiply the selected wager.</small>}
    </aside>
  )
}

export function SymbolValueGuide({
  symbolSet,
  showSidePanel = false,
  sidePanelClassName = '',
  showValueTokens = true,
  isOpen: controlledIsOpen,
  onOpenChange,
  theme,
}: {
  symbolSet: SlotSymbolSet
  showSidePanel?: boolean
  sidePanelClassName?: string
  showValueTokens?: boolean
  isOpen?: boolean
  onOpenChange?: (open: boolean) => void
  theme?: SlotDialogTheme
}) {
  const [internalIsOpen, setInternalIsOpen] = useState(false)
  const isOpen = controlledIsOpen ?? internalIsOpen
  const triggerRef = useRef<HTMLButtonElement>(null)
  const setOpen = useCallback((open: boolean) => {
    if (controlledIsOpen === undefined) setInternalIsOpen(open)
    onOpenChange?.(open)
  }, [controlledIsOpen, onOpenChange])
  const closeGuide = () => setOpen(false)
  const dialogRef = useSlotDialog(isOpen, closeGuide)

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
        aria-label="Symbol values"
        title="Symbol values"
        aria-expanded={isOpen}
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        {theme === 'midnight-vault' ? (
          <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" width="24" height="24"
            fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
            <circle cx="12" cy="12" r="9" />
            <path d="M12 11v6M12 7h.01" />
          </svg>
        ) : <><span aria-hidden="true">◆</span>Symbol values</>}
      </button>

      {isOpen && createPortal(
        <div
          data-slot-dialog-theme={theme}
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
            panelRef={dialogRef}
            theme={theme}
          />
        </div>,
        document.body,
      )}
    </>
  )
}
