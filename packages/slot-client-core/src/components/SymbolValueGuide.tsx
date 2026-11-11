import { ResponsiveSlotImage } from './ResponsiveSlotImage'
import { useCallback, useRef, useState, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { getSlotSymbolDefinition, getSlotSymbolImageSrcSet, type SlotSymbolGuideEntry, type SlotSymbolSet } from '../config/symbolSets'
import { useSlotDialog, type SlotDialogTheme } from '../hooks/useSlotDialog'
import { formatRand } from '../slotPagePresentation'
import { SlotDialogCloseIcon } from './SlotDialogCloseIcon'

function guideValueAtWager(value: string | undefined, wager: number | undefined, scale: number) {
  if (!value || wager === undefined) return value
  const multiplier = /^(\d+(?:\.\d+)?)[×x]$/i.exec(value)?.[1]
  return multiplier === undefined ? value : formatRand(Math.round(wager * Number(multiplier) * scale * 100) / 100)
}

function guideTopAwardCents(entry: SlotSymbolGuideEntry, wager: number, defaultScale: number) {
  const scale = entry.payoutScale ?? defaultScale
  const multipliers = [entry.firstValue, entry.secondValue].flatMap(value => {
    const multiplier = /^(\d+(?:\.\d+)?)[×x]$/i.exec(value ?? '')?.[1]
    return multiplier === undefined ? [] : [Number(multiplier)]
  })
  return multipliers.length === 0 ? -1 : Math.max(...multipliers.map(multiplier =>
    Math.round(wager * multiplier * scale * 100)))
}

function SymbolValuePanel({
  className,
  headingId,
  onClose,
  onMinimize,
  showValueTokens,
  symbolSet,
  panelRef,
  theme,
  wager,
}: {
  className: string
  headingId?: string
  onClose?: () => void
  onMinimize?: () => void
  showValueTokens: boolean
  symbolSet: SlotSymbolSet
  panelRef?: RefObject<HTMLElement | null>
  theme?: SlotDialogTheme
  wager?: number
}) {
  const guideEntries = (showValueTokens
    ? symbolSet.guideEntries
    : symbolSet.guideEntries.filter(({ symbol }) => !getSlotSymbolDefinition(symbolSet, symbol).wagerMultiplier)).slice()
  if (wager !== undefined) {
    guideEntries.sort((first, second) =>
      guideTopAwardCents(second, wager, symbolSet.guidePayoutScale ?? 1) -
      guideTopAwardCents(first, wager, symbolSet.guidePayoutScale ?? 1))
  }
  const guideRows: SlotSymbolGuideEntry[][] = []
  const displayedSymbols = new Set<SlotSymbolGuideEntry['symbol']>()
  for (const entry of guideEntries) {
    if (displayedSymbols.has(entry.symbol)) continue
    const rowSymbols = symbolSet.guideSymbolGroups?.find(row => row.includes(entry.symbol))
    const rowEntries = rowSymbols
      ? rowSymbols.flatMap(symbol => guideEntries.find(candidate => candidate.symbol === symbol) ?? [])
      : [entry]
    guideRows.push(rowEntries)
    rowEntries.forEach(({ symbol }) => displayedSymbols.add(symbol))
  }
  const renderEntry = ({
    symbol,
    firstLabel,
    firstValue,
    secondLabel,
    secondValue,
    artworkValueLabel,
    compactInSidePanel,
    payoutScale,
  }: SlotSymbolGuideEntry) => {
    const definition = getSlotSymbolDefinition(symbolSet, symbol)
    const hasSecondValue = Boolean(secondLabel || secondValue)
    const firstIsAmount = wager !== undefined && /^\d+(?:\.\d+)?[×x]$/i.test(firstValue)
    const secondIsAmount = wager !== undefined && /^\d+(?:\.\d+)?[×x]$/i.test(secondValue ?? '')
    const awardScale = payoutScale ?? symbolSet.guidePayoutScale ?? 1
    return (
      <div
        className={`symbol-value-guide__item symbol-value-guide__item--${symbol.toLowerCase()}${compactInSidePanel ? ' symbol-value-guide__item--compact' : ''}${hasSecondValue ? '' : ' symbol-value-guide__item--single-value'}`}
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
        <span className={firstIsAmount ? 'symbol-value-guide__award symbol-value-guide__award--three' : undefined}><b>{firstLabel}</b> {guideValueAtWager(firstValue, wager, awardScale)}</span>
        {hasSecondValue && <span className={secondIsAmount ? 'symbol-value-guide__award symbol-value-guide__award--five' : undefined}><b>{secondLabel}</b> {guideValueAtWager(secondValue, wager, awardScale)}</span>}
      </div>
    )
  }
  return (
    <aside
      id={onMinimize ? 'symbol-value-guide-side-panel' : undefined}
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
        {wager === undefined || symbolSet.guideSubtitle
          ? <span>{symbolSet.guideSubtitle ?? '3 in a row · 5 in a row'}</span>
          : <span className="symbol-value-guide__heading-key">
              <span className="symbol-value-guide__heading-three">3 in a row</span>
              <span aria-hidden="true"> · </span>
              <span className="symbol-value-guide__heading-five">5 in a row</span>
            </span>}
        {onMinimize && <button
          className="symbol-value-guide__minimize"
          type="button"
          aria-label="Minimize symbol values"
          onClick={onMinimize}
        >
          <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <path d="M5 12h14" />
          </svg>
        </button>}
      </div>
      <div className="symbol-value-guide__grid">
        {guideRows.map(entries => entries.length > 1
          ? <div className="symbol-value-guide__group" key={`row-${entries[0].symbol}`}>
              {entries.map(renderEntry)}
            </div>
          : renderEntry(entries[0]))}
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
  wager,
  triggerContent,
}: {
  symbolSet: SlotSymbolSet
  showSidePanel?: boolean
  sidePanelClassName?: string
  showValueTokens?: boolean
  isOpen?: boolean
  onOpenChange?: (open: boolean) => void
  theme?: SlotDialogTheme
  wager?: number
  triggerContent?: ReactNode
}) {
  const [internalIsOpen, setInternalIsOpen] = useState(false)
  const [sidePanelMinimized, setSidePanelMinimized] = useState(false)
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
      {showSidePanel && createPortal(sidePanelMinimized ? (
        <button
          autoFocus
          className={`symbol-value-guide__restore ${sidePanelClassName}`.trim()}
          type="button"
          aria-label="Restore symbol values"
          aria-controls="symbol-value-guide-side-panel"
          aria-expanded="false"
          onClick={() => setSidePanelMinimized(false)}
        >
          <span aria-hidden="true">◆</span> Symbol values
        </button>
      ) : (
        <SymbolValuePanel
          className={`symbol-value-guide--side ${sidePanelClassName}`.trim()}
          onMinimize={() => setSidePanelMinimized(true)}
          showValueTokens={showValueTokens}
          symbolSet={symbolSet}
          wager={wager}
        />
      ),
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
        {triggerContent ?? <><span aria-hidden="true">◆</span>Symbol values</>}
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
            wager={wager}
          />
        </div>,
        document.body,
      )}
    </>
  )
}
