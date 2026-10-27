import { useEffect, useId, useRef, useState } from 'react'

export type CrapsChipDenomination = Readonly<{ value: number; picture: string; artScale?: number }>
export type CrapsBetControlsProps = Readonly<{
  denominations: readonly CrapsChipDenomination[]
  selectedChip: number
  disabled: boolean
  canUndo: boolean
  canClear: boolean
  onSelectChip: (value: number) => void
  onUndo: () => void
  onClear: () => void
}>

const chipLabel = (value: number) => 'R' + Number(value.toFixed(2))

/** Visual draft controls only; the game owns valid denominations, wagers and click audio. */
export function CrapsBetControls({ denominations, selectedChip, disabled, canUndo, canClear, onSelectChip, onUndo, onClear }: CrapsBetControlsProps) {
  const [expanded, setExpanded] = useState(false)
  const picker = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const optionsId = useId()
  const index = denominations.findIndex(item => item.value === selectedChip)
  const selected = denominations[index] ?? denominations[0]
  const locked = disabled || !selected

  useEffect(() => { if (disabled) setExpanded(false) }, [disabled])
  useEffect(() => {
    if (!expanded) return
    const outside = (event: PointerEvent) => {
      if (event.target instanceof Node && !picker.current?.contains(event.target)) setExpanded(false)
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      setExpanded(false)
      trigger.current?.focus()
    }
    document.addEventListener('pointerdown', outside, true)
    document.addEventListener('keydown', escape)
    picker.current?.querySelector<HTMLButtonElement>('.ff-craps-chip-options [aria-pressed="true"]')?.focus()
    return () => {
      document.removeEventListener('pointerdown', outside, true)
      document.removeEventListener('keydown', escape)
    }
  }, [expanded])

  function select(value: number) {
    if (disabled || !denominations.some(item => item.value === value)) return
    onSelectChip(value)
    setExpanded(false)
    trigger.current?.focus()
  }
  function step(direction: -1 | 1) {
    const next = denominations[Math.max(0, index) + direction]
    if (!disabled && next) select(next.value)
  }

  return <div className="ff-craps-bet-controls">
    <div ref={picker} className="ff-craps-chip-picker" role="group" aria-label="Select chip value">
      <button className="ff-craps-chip-step ff-craps-chip-step--up" type="button" aria-label="Increase chip value" title="Higher chip value" disabled={locked || index >= denominations.length - 1} onClick={() => step(1)}>
        <svg viewBox="0 0 32 20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"><path d="M5 15 16 4 27 15"/></svg>
      </button>
      <button ref={trigger} className="ff-craps-selected-chip" type="button" aria-label={'Chip options, selected ' + chipLabel(selected?.value ?? selectedChip)} aria-expanded={expanded} aria-controls={optionsId} title="Choose chip value" disabled={locked}
        onClick={() => { if (!locked) setExpanded(value => !value) }}>
        {selected ? <ChipImage chip={selected}/> : <span>{chipLabel(selectedChip)}</span>}
      </button>
      <button className="ff-craps-chip-step ff-craps-chip-step--down" type="button" aria-label="Decrease chip value" title="Lower chip value" disabled={locked || index <= 0} onClick={() => step(-1)}>
        <svg viewBox="0 0 32 20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"><path d="m5 5 11 11L27 5"/></svg>
      </button>
      {expanded && !disabled && <div className="ff-craps-chip-options" id={optionsId} role="group" aria-label="Chip denominations">
        {denominations.map(item => <button type="button" key={item.value} aria-label={'Select ' + chipLabel(item.value) + ' chip'} aria-pressed={selected?.value === item.value} disabled={disabled}
          onClick={() => select(item.value)}><ChipImage chip={item}/></button>)}
      </div>}
    </div>
    <div className="ff-craps-control-actions">
      <button type="button" aria-label="Undo last chip" title="Undo last chip" disabled={disabled || !canUndo} onClick={() => { if (!disabled && canUndo) onUndo() }}>
        <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M8 4 3 9l5 5M3 9h10a7 7 0 0 1 0 14" transform="translate(0 -2)"/></svg>
      </button>
      <button type="button" aria-label="Clear bets" title="Clear bets" disabled={disabled || !canClear} onClick={() => { if (!disabled && canClear) onClear() }}>
        <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m6 6 12 12M18 6 6 18"/></svg>
      </button>
    </div>
  </div>
}

function ChipImage({ chip }: { chip: CrapsChipDenomination }) {
  return <img src={chip.picture} alt="" draggable={false} width="52" height="52" style={{ transform: `scale(${chip.artScale ?? 1})` }}/>
}
