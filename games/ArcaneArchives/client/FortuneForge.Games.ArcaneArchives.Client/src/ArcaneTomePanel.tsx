// Edited: 2026-11-18T19:10:04-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T19:05:53-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:56:23-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:43:19-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:34:08-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:20:26-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:11:03-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T17:59:23-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:29:57-06:00 | Computer: Vinnabok
import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent, ReactNode, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { formatRand, getSlotSymbolDefinition, SlotDialogCloseIcon, slotPointsToRand, useSlotDialog } from '@fortuneforge/slot-client-core'
import type { SlotPotionBottle, SlotPotionState, SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { ArcaneIngredientFlights } from './ArcaneIngredientFlights'
import { ArcanePotionMotion } from './ArcanePotionMotion'
import { ArcaneTomeBook } from './ArcaneTomeBook'
import { getPotionArtwork } from './potionArtwork'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols'
import cauldron from './assets/art/optimized/arcane-archives-cauldron-v1.webp'
import lectern from './assets/art/optimized/arcane-archives-lectern-blank-tome-layer-v1.webp'

type WorkbenchPage = 'tome' | 'cauldron'
const workbenchLabels = { tome: 'Tome', cauldron: 'Cauldron' } as const

function remainingSpins(count: number) {
  return `${count} ${count === 1 ? 'spin' : 'spins'} left`
}

function IngredientIcon({ id }: { id: string }) {
  const ingredient = getSlotSymbolDefinition(ARCANE_ARCHIVES_SYMBOLS, id)
  return <img src={ingredient.image} alt="" />
}

function PotionIcon({ bottle }: { bottle: SlotPotionBottle }) {
  const art = getPotionArtwork(bottle)
  return art ? <img className="arcane-potion-bottle" src={art.bottle} alt="" /> : <IngredientIcon id={bottle.ingredientId} />
}

function Cauldron({ state, stir }: { state: SlotPotionState; stir: ReactNode }) {
  return <section className="arcane-cauldron" aria-label="Cauldron contents">
    <div className="arcane-cauldron__artwork">
      <img className="arcane-cauldron__art" src={cauldron} alt="" />
      {stir}
    </div>
    <p><strong>{state.pot.length} / {state.capacity} ingredient units</strong></p>
    <p>A potion bottles automatically when the cauldron is full or holds {state.majority} units of one ingredient.</p>
    {state.pot.length > 0 && <ol aria-label="Ingredients in the pot">
      {state.pot.map((id, index) => {
        const name = getSlotSymbolDefinition(ARCANE_ARCHIVES_SYMBOLS, id).label
        return <li key={index} title={name}><span className="slots-page__accessible-title">{name}</span><IngredientIcon id={id} /></li>
      })}
    </ol>}
    {state.preview && <p className="arcane-potion-preview" aria-live="polite">
      <strong>Current mix · {state.preview.name}</strong>
      <span>{state.preview.effect}</span>
    </p>}
  </section>
}

function ReadyPotions({ bottles, bottlesRef, disabled, onUse, pointValueInCents }: {
  bottles: readonly SlotPotionBottle[]
  bottlesRef: RefObject<HTMLUListElement | null>
  disabled: boolean
  onUse: (id: number, button: HTMLButtonElement) => void
  pointValueInCents: number
}) {
  const descriptionPrefix = useId()
  const [hoveredId, setHoveredId] = useState<number | null>(null)
  const [focusedId, setFocusedId] = useState<number | null>(null)
  const [touchPreviewId, setTouchPreviewId] = useState<number | null>(null)
  const touchPress = useRef<{
    bottleId: number
    pointerId: number
    x: number
    y: number
    timer: number
    suppressClick: boolean
  } | null>(null)
  const inspected = bottles.find(bottle => bottle.id === touchPreviewId)
    ?? bottles.find(bottle => bottle.id === hoveredId)
    ?? bottles.find(bottle => bottle.id === focusedId)

  useEffect(() => () => window.clearTimeout(touchPress.current?.timer), [])

  useEffect(() => {
    if (touchPreviewId === null) return
    const dismiss = () => setTouchPreviewId(null)
    document.addEventListener('pointerdown', dismiss, true)
    return () => document.removeEventListener('pointerdown', dismiss, true)
  }, [touchPreviewId])

  function startHold(bottleId: number, event: ReactPointerEvent<HTMLButtonElement>) {
    window.clearTimeout(touchPress.current?.timer)
    touchPress.current = null
    if (event.pointerType !== 'touch' || !event.isPrimary) return

    const { pointerId, clientX: x, clientY: y } = event
    const timer = window.setTimeout(() => {
      if (touchPress.current?.pointerId !== pointerId) return
      touchPress.current.suppressClick = true
      setTouchPreviewId(bottleId)
    }, 450)
    touchPress.current = { bottleId, pointerId, x, y, timer, suppressClick: false }
  }

  function endHold(event: ReactPointerEvent<HTMLButtonElement>) {
    if (touchPress.current?.pointerId === event.pointerId) window.clearTimeout(touchPress.current.timer)
  }

  function cancelDraggedHold(event: ReactPointerEvent<HTMLButtonElement>) {
    const press = touchPress.current
    if (press?.pointerId !== event.pointerId || Math.hypot(event.clientX - press.x, event.clientY - press.y) < 10) return
    window.clearTimeout(press.timer)
    press.suppressClick = true
    setTouchPreviewId(null)
  }

  if (bottles.length === 0) return null

  const wagerLabel = (bottle: SlotPotionBottle) => bottle.lockedWagerPoints === undefined ? null
    : `Potion wager ${formatRand(slotPointsToRand(bottle.lockedWagerPoints, pointValueInCents))}`

  return <div className="arcane-ready-potions">
    <ul ref={bottlesRef} className="arcane-ready-potions__bottles" aria-label="Bottled potions">
      {bottles.map(bottle => {
        const descriptionId = descriptionPrefix + '-' + bottle.id
        return <li key={bottle.id}>
          <button type="button" className="arcane-ready-potions__bottle" data-bottle-id={bottle.id}
            aria-label={'Use ' + bottle.name} aria-describedby={descriptionId} aria-disabled={disabled || undefined}
            onPointerEnter={event => { if (event.pointerType !== 'touch') setHoveredId(bottle.id) }}
            onPointerLeave={() => setHoveredId(null)}
            onPointerDown={event => startHold(bottle.id, event)}
            onPointerMove={cancelDraggedHold}
            onPointerUp={endHold}
            onPointerCancel={endHold}
            onContextMenu={event => { if (touchPress.current?.bottleId === bottle.id) event.preventDefault() }}
            onFocus={() => setFocusedId(bottle.id)}
            onBlur={() => setFocusedId(null)}
            onClick={event => {
              if (event.detail > 0 && touchPress.current?.bottleId === bottle.id && touchPress.current.suppressClick) return
              if (!disabled) onUse(bottle.id, event.currentTarget)
            }}>
            <PotionIcon bottle={bottle} />
          </button>
          <span id={descriptionId} hidden>{bottle.effect}{wagerLabel(bottle) && `. ${wagerLabel(bottle)}`}</span>
        </li>
      })}
    </ul>
    {inspected && <div className="arcane-ready-potions__tooltip" role="tooltip">
      <strong>{inspected.name}</strong>
      <span>{inspected.effect}</span>
      {wagerLabel(inspected) && <span>{wagerLabel(inspected)}</span>}
    </div>}
  </div>
}

export function ArcaneTomePanel({
  state, potions, awarded, cabinetRef, settledSpin, reducedMotion, disabled, error, onOpen, onChangePage, onUsePotion,
  confirmedPotionUse, onPotionCue, pointValueInCents,
}: SlotTomePanelProps) {
  const { pages } = state
  const cauldronRef = useRef<HTMLButtonElement>(null)
  const bottlesRef = useRef<HTMLUListElement>(null)
  const activationOriginRef = useRef<{ id: number; x: number; y: number } | null>(null)
  const [section, setSection] = useState<WorkbenchPage | null>(null)
  const [stir, setStir] = useState<{ spinKey: number; staticOnly: boolean } | null>(null)
  const [stirredSpinKey, setStirredSpinKey] = useState<number | null>(null)
  const [lastArrivedUnits, setLastArrivedUnits] = useState(potions?.pot.length ?? 0)
  const headingId = useId()
  const dialogRef = useSlotDialog(section !== null, () => setSection(null))
  const activePage = pages.find(page => page.id === state.activePage)
  const count = Object.values(awarded).reduce((total, units) => total + units, 0)
  const readyStatus = 'Activate a Tome page to spin'
  const wideStatus = activePage ? activePage.label + ' · ' + state.boostedSpinsRemaining + ' left'
    : state.grimoireReady ? readyStatus
      : count > 0 ? '+' + count + (count === 1 ? ' ingredient' : ' ingredients') : ''
  const potUnits = potions?.pot.length ?? 0
  const collecting = settledSpin !== null && count > 0 &&
    settledSpin.key !== stir?.spinKey && settledSpin.key !== stirredSpinKey
  const mobilePotUnits = collecting ? lastArrivedUnits : potUnits
  const activeBottleId = potions?.active?.bottle.id ?? null
  const tomeLabel = potions ? workbenchLabels.tome : 'Tome & ingredients'
  const staticStir = reducedMotion || stir?.staticOnly === true
  const startStir = useCallback((spinKey: number) => setStir({ spinKey, staticOnly: reducedMotion }), [reducedMotion])
  const stirArtwork = stir && <i key={stir.spinKey} className="arcane-workbench__stir"
    data-static={staticStir || undefined} aria-hidden="true">
    <img src={cauldron} alt="" />
  </i>

  useEffect(() => {
    if (!collecting) setLastArrivedUnits(potUnits)
  }, [collecting, potUnits])

  useEffect(() => {
    if (!stir || !staticStir) return
    const timer = window.setTimeout(() => {
      setStirredSpinKey(stir.spinKey)
      setStir(current => current === stir ? null : current)
    }, 550)
    return () => window.clearTimeout(timer)
  }, [stir, staticStir])

  useEffect(() => {
    if (activeBottleId === activationOriginRef.current?.id && document.activeElement === document.body) {
      cauldronRef.current?.focus({ preventScroll: true })
    }
  }, [activeBottleId])

  const open = (page: WorkbenchPage) => {
    onOpen()
    setSection(page)
  }

  function usePotion(id: number, button: HTMLButtonElement) {
    const { left, top, width, height } = button.getBoundingClientRect()
    activationOriginRef.current = { id, x: left + width / 2, y: top + height / 2 }
    onUsePotion(id)
  }

  return <>
    <div className="arcane-tome-summary" data-workbench={potions !== null || undefined}>
      <div className="arcane-workbench" role="group" aria-label="Arcane workbench">
        <button type="button" className="arcane-workbench__tome" data-grimoire-ready={state.grimoireReady || undefined}
          aria-label={tomeLabel} aria-expanded={section === 'tome'}
          aria-haspopup="dialog" onClick={() => open('tome')}>
          {potions && <img src={lectern} alt="" />}
          <span className="arcane-workbench__tome-label">{tomeLabel}</span>
        </button>
        {potions && <div className="arcane-workbench__cauldron">
          <button ref={cauldronRef} type="button" className="arcane-workbench__pot" aria-expanded={section === 'cauldron'}
            aria-haspopup="dialog" onClick={() => open('cauldron')} onAnimationEnd={event => {
              if (!stir || event.animationName !== 'arcane-cauldron-stir') return
              setStirredSpinKey(stir.spinKey)
              setStir(current => current === stir ? null : current)
            }}>
            <img src={cauldron} alt="" />
            <span>Cauldron <b className="arcane-workbench__wide-units">{potUnits}/{potions.capacity}</b>
              <b className="arcane-workbench__mobile-units">{mobilePotUnits}/{potions.capacity}</b></span>
            <progress className="arcane-workbench__fill" value={mobilePotUnits} max={potions.capacity} aria-hidden="true" />
            {stirArtwork}
          </button>
          <ReadyPotions bottles={potions.bottles} bottlesRef={bottlesRef}
            disabled={disabled || potions.active !== null} onUse={usePotion} pointValueInCents={pointValueInCents} />
        </div>}
      </div>
      <output aria-live="polite">
        {wideStatus && <span className="arcane-workbench__wide-status">{wideStatus}</span>}
        {potions?.active && <span className="arcane-workbench__wide-status arcane-workbench__active-potion">
          {potions.active.bottle.name} · {potions.active.spinsRemaining} left
        </span>}
        <span className="arcane-workbench__mobile-status">
          {activePage ? <span><b>Tome</b> · {activePage.label} · {remainingSpins(state.boostedSpinsRemaining)}</span>
            : <span><b>{state.grimoireReady ? readyStatus : 'No active Tome page'}</b></span>}
          {potions?.active && <span><b>Active potion</b> · {potions.active.bottle.name} · {remainingSpins(potions.active.spinsRemaining)}</span>}
        </span>
      </output>
      {!section && error && <p className="arcane-workbench__error" role="alert">{error}</p>}
    </div>
    <ArcaneIngredientFlights
      cabinetRef={cabinetRef}
      cauldronRef={cauldronRef}
      settledSpin={settledSpin}
      awarded={awarded}
      reducedMotion={reducedMotion}
      onCollectionArrived={startStir}
    />
    {potions && <ArcanePotionMotion state={potions} arrival={stirredSpinKey} reducedMotion={reducedMotion}
      settledSpinKey={settledSpin?.key ?? null} confirmedPotionUse={confirmedPotionUse} onPotionCue={onPotionCue}
      cabinetRef={cabinetRef} cauldronRef={cauldronRef} bottlesRef={bottlesRef}
      activationOriginRef={activationOriginRef} />}
    {section && createPortal(
      <div className="arcane-tome-backdrop" onMouseDown={event => {
        if (event.target === event.currentTarget) setSection(null)
      }}>
        <section className="arcane-tome-dialog" data-section={section} ref={dialogRef} tabIndex={-1}
          role="dialog" aria-modal="true" aria-labelledby={headingId}>
          <header>
            <h2 id={headingId}>{section === 'tome' ? tomeLabel : workbenchLabels[section]}</h2>
            <button type="button" className="arcane-tome-close" aria-label="Close workbench"
              onClick={() => setSection(null)}><SlotDialogCloseIcon /></button>
          </header>
          {section === 'tome' && <ArcaneTomeBook state={state} disabled={disabled} reducedMotion={reducedMotion}
            showIngredientTotals={potions === null} onChangePage={onChangePage} />}
          {section === 'cauldron' && potions && <Cauldron state={potions} stir={stirArtwork} />}
          {error && <p role="alert">{error}</p>}
        </section>
      </div>,
      document.body,
    )}
  </>
}
