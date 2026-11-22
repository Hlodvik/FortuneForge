// Edited: 2026-11-18T18:41:52-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:45:02-06:00 | Computer: Vinnabok
import { ResponsiveSlotImage } from './ResponsiveSlotImage'
import { useCallback, useId, useRef, useState, type CSSProperties, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { getSlotSymbolArtworks, getSlotSymbolDefinition, getSlotSymbolImageSrcSet, type SlotSymbolGuideEntry, type SlotSymbolSet } from '../config/symbolSets'
import { useSlotDialog, type SlotDialogTheme } from '../hooks/useSlotDialog'
import { formatRand } from '../slotPagePresentation'
import { SlotDialogCloseIcon } from './SlotDialogCloseIcon'

function readMultiplier(value: string | undefined) {
  const match = /^(\d+(?:\.\d+)?)[×x]$/i.exec(value ?? '')
  return match ? Number(match[1]) : undefined
}

function guideValueAtWager(value: string | undefined, wager: number | undefined, scale: number, pointValueInCents: number) {
  if (!value || wager === undefined) return value
  const multiplier = readMultiplier(value)
  return multiplier === undefined ? value : formatRand(guideAwardCents(wager, multiplier, scale, pointValueInCents) / 100)
}

function guideAwardCents(wager: number, multiplier: number, scale: number, pointValueInCents: number) {
  return Math.round(wager * multiplier * scale * 100 / pointValueInCents) * pointValueInCents
}

function guideTopAwardCents(entry: SlotSymbolGuideEntry, wager: number, defaultScale: number, pointValueInCents: number) {
  if (entry.companionSymbol) return -1
  const scale = entry.payoutScale ?? defaultScale
  const multipliers = [entry.firstValue, entry.secondValue].flatMap(value => {
    const multiplier = readMultiplier(value)
    return multiplier === undefined ? [] : [multiplier]
  })
  return multipliers.length === 0 ? -1 : Math.max(...multipliers.map(multiplier =>
    guideAwardCents(wager, multiplier, scale, pointValueInCents)))
}

function SymbolValueGuideItem({ entry, symbolSet, wager, pointValueInCents }: {
  entry: SlotSymbolGuideEntry
  symbolSet: SlotSymbolSet
  wager?: number
  pointValueInCents: number
}) {
  const { symbol, firstLabel, firstValue, secondLabel, secondValue, artworkValueLabel, compactInSidePanel,
    payoutScale, companionSymbol, details } = entry
  const definition = getSlotSymbolDefinition(symbolSet, symbol)
  const artworks = getSlotSymbolArtworks(definition)
  const multipleArtworks = artworks.length > 1
  const companion = companionSymbol ? getSlotSymbolDefinition(symbolSet, companionSymbol) : null
  const hasSecondValue = Boolean(secondLabel || secondValue)
  const firstIsAmount = wager !== undefined && readMultiplier(firstValue) !== undefined
  const secondIsAmount = wager !== undefined && readMultiplier(secondValue) !== undefined
  const awardScale = payoutScale ?? symbolSet.guidePayoutScale ?? 1
  const firstDisplayValue = guideValueAtWager(firstValue, wager, awardScale, pointValueInCents)
  const detailsId = useId()
  const [hoverPlacement, setHoverPlacement] = useState<'above' | 'below' | null>(null)
  const [pinned, setPinned] = useState(false)
  const detailsOpen = hoverPlacement !== null || pinned

  return (
    <div
      className={`symbol-value-guide__item symbol-value-guide__item--${symbol.toLowerCase()}${compactInSidePanel ? ' symbol-value-guide__item--compact' : ''}${hasSecondValue ? '' : ' symbol-value-guide__item--single-value'}${multipleArtworks ? ' symbol-value-guide__item--artwork-variants' : ''}`}
      style={multipleArtworks ? { '--symbol-guide-artwork-count': artworks.length } as CSSProperties : undefined}
      title={details ? undefined : definition.label}
      data-details-above={!pinned && hoverPlacement === 'above' || undefined}
      onPointerEnter={details ? event => {
        if (event.pointerType !== 'mouse') return
        const row = event.currentTarget.getBoundingClientRect()
        const panel = event.currentTarget.closest('aside')?.getBoundingClientRect()
        if (!panel) return
        const roomBelow = panel.bottom - row.bottom
        const roomAbove = row.top - panel.top
        setHoverPlacement(roomBelow < 112 && roomAbove > roomBelow ? 'above' : 'below')
      } : undefined}
      onPointerLeave={details ? () => setHoverPlacement(null) : undefined}
    >
      <div className={`symbol-value-guide__artwork${multipleArtworks ? ' symbol-value-guide__artwork--variants' : ''}`} data-value-label={artworkValueLabel ?? definition.valueLabel}>
        {artworks.map((artwork, index) => <ResponsiveSlotImage key={index}
          imageSrcSet={getSlotSymbolImageSrcSet(artwork.responsiveImages)} src={artwork.image}
          alt={multipleArtworks ? artwork.label ?? definition.label : ''} aria-hidden={multipleArtworks ? undefined : true} />)}
      </div>
      <span className="symbol-value-guide__name">{definition.label}</span>
      {companion ? (
        <span className="symbol-value-guide__match">
          <span aria-hidden="true">+</span>
          {getSlotSymbolArtworks(companion).map((artwork, index) => <ResponsiveSlotImage key={index}
            imageSrcSet={getSlotSymbolImageSrcSet(artwork.responsiveImages)} src={artwork.image} alt={artwork.label ?? companion.label} />)}
          <span aria-hidden="true">=</span>
          <strong>{firstDisplayValue}</strong>
          {firstIsAmount && <span className="symbol-value-guide__match-multiplier">({firstValue})</span>}
        </span>
      ) : details ? (
        <button className="symbol-value-guide__detail-toggle" type="button" aria-controls={detailsId}
          aria-expanded={detailsOpen} aria-label={`${firstValue} details for ${definition.label}`} onClick={() => {
            if (pinned) setHoverPlacement(null)
            setPinned(!pinned)
          }}>
          {firstValue}
          <svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="m4 6 4 4 4-4" />
          </svg>
        </button>
      ) : (
        <span className={firstIsAmount ? 'symbol-value-guide__award symbol-value-guide__award--three' : undefined}>
          {firstLabel && <><b>{firstLabel}</b> </>}{firstDisplayValue}
        </span>
      )}
      {hasSecondValue && <span className={secondIsAmount ? 'symbol-value-guide__award symbol-value-guide__award--five' : undefined}><b>{secondLabel}</b> {guideValueAtWager(secondValue, wager, awardScale, pointValueInCents)}</span>}
      {details && <div id={detailsId} className="symbol-value-guide__details" hidden={!detailsOpen}>
        {details.map(({ label, text }) => <p key={label}><b>{label}</b> {text}</p>)}
      </div>}
    </div>
  )
}

function SymbolValuePanel({
  className,
  headingId,
  onClose,
  onMinimize,
  sidePanelMode,
  focusMinimize = false,
  showValueTokens,
  symbolSet,
  panelRef,
  theme,
  wager,
  pointValueInCents = 1,
}: {
  className: string
  headingId?: string
  onClose?: () => void
  onMinimize?: () => void
  sidePanelMode?: 'on-demand'
  focusMinimize?: boolean
  showValueTokens: boolean
  symbolSet: SlotSymbolSet
  panelRef?: RefObject<HTMLElement | null>
  theme?: SlotDialogTheme
  wager?: number
  pointValueInCents?: number
}) {
  const guideEntries = (showValueTokens
    ? symbolSet.guideEntries
    : symbolSet.guideEntries.filter(({ symbol }) => !getSlotSymbolDefinition(symbolSet, symbol).wagerMultiplier)).slice()
  if (wager !== undefined) {
    guideEntries.sort((first, second) =>
      guideTopAwardCents(second, wager, symbolSet.guidePayoutScale ?? 1, pointValueInCents) -
      guideTopAwardCents(first, wager, symbolSet.guidePayoutScale ?? 1, pointValueInCents))
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
  const renderEntry = (entry: SlotSymbolGuideEntry) => <SymbolValueGuideItem key={entry.symbol} entry={entry}
    symbolSet={symbolSet} wager={wager} pointValueInCents={pointValueInCents} />
  return (
    <aside
      id={onMinimize ? 'symbol-value-guide-side-panel' : undefined}
      ref={panelRef}
      tabIndex={headingId ? -1 : undefined}
      data-slot-dialog-theme={headingId ? theme : undefined}
      data-side-panel-mode={sidePanelMode}
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
          autoFocus={focusMinimize}
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

type SidePanelMode = 'auto' | 'on-demand'

function SymbolValueSidePanel({
  className,
  mode,
  show,
  showValueTokens,
  symbolSet,
  wager,
  pointValueInCents = 1,
}: {
  className: string
  mode: SidePanelMode
  show: boolean
  showValueTokens: boolean
  symbolSet: SlotSymbolSet
  wager?: number
  pointValueInCents?: number
}) {
  const [minimized, setMinimized] = useState(mode === 'on-demand')
  const focusRestoreButton = useRef(false)
  if (!show) return null
  const onDemand = mode === 'on-demand' ? mode : undefined

  return createPortal(minimized ? (
    <button
      autoFocus={focusRestoreButton.current}
      className={`symbol-value-guide__restore ${className}`.trim()}
      data-side-panel-mode={onDemand}
      type="button"
      aria-label={onDemand ? 'Symbol values' : 'Restore symbol values'}
      aria-controls="symbol-value-guide-side-panel"
      aria-expanded="false"
      onClick={() => setMinimized(false)}
    >
      <span aria-hidden="true">◆</span> Symbol values
    </button>
  ) : (
    <SymbolValuePanel
      className={`symbol-value-guide--side ${className}`.trim()}
      sidePanelMode={onDemand}
      focusMinimize={focusRestoreButton.current || Boolean(onDemand)}
      onMinimize={() => {
        focusRestoreButton.current = true
        setMinimized(true)
      }}
      showValueTokens={showValueTokens}
      symbolSet={symbolSet}
      pointValueInCents={pointValueInCents}
      wager={wager}
    />
  ), document.body)
}

export function SymbolValueGuide({
  symbolSet,
  showSidePanel = false,
  sidePanelClassName = '',
  sidePanelMode = 'auto',
  showValueTokens = true,
  isOpen: controlledIsOpen,
  onOpenChange,
  theme,
  wager,
  pointValueInCents = 1,
  triggerContent,
}: {
  symbolSet: SlotSymbolSet
  showSidePanel?: boolean
  sidePanelClassName?: string
  sidePanelMode?: SidePanelMode
  showValueTokens?: boolean
  isOpen?: boolean
  onOpenChange?: (open: boolean) => void
  theme?: SlotDialogTheme
  wager?: number
  pointValueInCents?: number
  triggerContent?: ReactNode
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
      <SymbolValueSidePanel
        className={sidePanelClassName}
        mode="auto"
        show={showSidePanel && sidePanelMode === 'auto'}
        showValueTokens={showValueTokens}
        symbolSet={symbolSet}
        pointValueInCents={pointValueInCents}
        wager={wager}
      />
      {sidePanelMode === 'on-demand' && <SymbolValueSidePanel
        className={sidePanelClassName}
        mode={sidePanelMode}
        show={showSidePanel}
        showValueTokens={showValueTokens}
        symbolSet={symbolSet}
        pointValueInCents={pointValueInCents}
        wager={wager}
      />}

      <button
        ref={triggerRef}
        className="symbol-value-guide__trigger"
        data-slot-utility-cue
        data-side-panel-mode={showSidePanel && sidePanelMode === 'on-demand' ? sidePanelMode : undefined}
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
            pointValueInCents={pointValueInCents}
            wager={wager}
          />
        </div>,
        document.body,
      )}
    </>
  )
}
