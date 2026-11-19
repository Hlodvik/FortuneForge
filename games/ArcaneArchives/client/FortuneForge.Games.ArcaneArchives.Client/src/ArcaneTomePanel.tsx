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
import { createPortal } from 'react-dom'
import { getSlotSymbolDefinition, SlotDialogCloseIcon, useSlotDialog } from '@fortuneforge/slot-client-core'
import type { SlotPotionState, SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { ArcaneIngredientFlights } from './ArcaneIngredientFlights'
import { ArcanePotionMotion } from './ArcanePotionMotion'
import { ArcanePotionShelfArtwork } from './ArcanePotionShelfArtwork'
import { ArcaneTomeBook } from './ArcaneTomeBook'
import { getPotionArtwork } from './potionArtwork'
import { ARCANE_ARCHIVES_SYMBOLS } from './symbols'
import cauldron from './assets/art/optimized/arcane-archives-cauldron-v1.webp'
import lectern from './assets/art/optimized/arcane-archives-lectern-blank-tome-layer-v1.webp'

type WorkbenchPage = 'tome' | 'cauldron' | 'shelf'
const workbenchLabels = { tome: 'Tome', cauldron: 'Cauldron', shelf: 'Potion shelf' } as const

function remainingSpins(count: number) {
  return `${count} ${count === 1 ? 'spin' : 'spins'} left`
}

function IngredientIcon({ id }: { id: string }) {
  const ingredient = getSlotSymbolDefinition(ARCANE_ARCHIVES_SYMBOLS, id)
  return <img src={ingredient.image} alt="" />
}

function PotionIcon({ ingredientId }: { ingredientId: string }) {
  const art = getPotionArtwork(ingredientId)
  return art ? <img className="arcane-potion-bottle" src={art.bottle} alt="" /> : <IngredientIcon id={ingredientId} />
}

function Cauldron({ state }: { state: SlotPotionState }) {
  return <section className="arcane-cauldron" aria-label="Cauldron contents">
    <img className="arcane-cauldron__art" src={cauldron} alt="" />
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

function PotionShelf({ state, disabled, onUse }: {
  state: SlotPotionState
  disabled: boolean
  onUse: (id: number) => void
}) {
  return <section className="arcane-potion-shelf" aria-label="Bottled potions">
    <ArcanePotionShelfArtwork bottles={state.bottles} />
    {state.active && <div className="arcane-potion-preview" role="status">
      <strong>{state.active.bottle.name} · {remainingSpins(state.active.spinsRemaining)}</strong>
      <span>{state.active.bottle.effect}</span>
    </div>}
    {state.bottles.length === 0 ? <p>No bottled potions on the shelf.</p> : <ul>
      {state.bottles.map(bottle => <li key={bottle.id}>
        <PotionIcon ingredientId={bottle.ingredientId} />
        <div><strong>{bottle.name}</strong><span>{bottle.effect}</span></div>
        <button type="button" aria-label={'Use ' + bottle.name} disabled={disabled || state.active !== null}
          onClick={() => onUse(bottle.id)}>Use</button>
      </li>)}
    </ul>}
  </section>
}

export function ArcaneTomePanel({
  state, potions, awarded, cabinetRef, settledSpin, reducedMotion, disabled, error, onOpen, onChangePage, onUsePotion,
  confirmedPotionUse, onPotionCue,
}: SlotTomePanelProps) {
  const { pages } = state
  const cauldronRef = useRef<HTMLButtonElement>(null)
  const shelfRef = useRef<HTMLButtonElement>(null)
  const pendingBottle = useRef<number | null>(null)
  const [section, setSection] = useState<WorkbenchPage | null>(null)
  const [stir, setStir] = useState<{ spinKey: number; staticOnly: boolean } | null>(null)
  const [stirredSpinKey, setStirredSpinKey] = useState<number | null>(null)
  const [lastArrivedUnits, setLastArrivedUnits] = useState(potions?.pot.length ?? 0)
  const headingId = useId()
  const dialogRef = useSlotDialog(section !== null, () => setSection(null))
  const activePage = pages.find(page => page.id === state.activePage)
  const count = Object.values(awarded).reduce((total, units) => total + units, 0)
  const wideStatus = activePage ? activePage.label + ' · ' + state.boostedSpinsRemaining + ' left'
    : state.grimoireReady ? 'Grimoire ready'
      : count > 0 ? '+' + count + (count === 1 ? ' ingredient' : ' ingredients') : ''
  const potionCount = potions?.bottles.length ?? 0
  const potUnits = potions?.pot.length ?? 0
  const collecting = settledSpin !== null && count > 0 &&
    settledSpin.key !== stir?.spinKey && settledSpin.key !== stirredSpinKey
  const mobilePotUnits = collecting ? lastArrivedUnits : potUnits
  const readyBottles = new Map<string, number>()
  for (const bottle of potions?.bottles ?? []) {
    readyBottles.set(bottle.name, (readyBottles.get(bottle.name) ?? 0) + 1)
  }
  const readyNames = [...readyBottles].map(([name, quantity]) => quantity === 1 ? name : `${name} ×${quantity}`).join(', ')
  const activeBottleId = potions?.active?.bottle.id ?? null
  const staticStir = reducedMotion || stir?.staticOnly === true
  const startStir = useCallback((spinKey: number) => setStir({ spinKey, staticOnly: reducedMotion }), [reducedMotion])

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
    if (activeBottleId !== null && activeBottleId === pendingBottle.current) {
      pendingBottle.current = null
      setSection(current => current === 'shelf' ? null : current)
    }
  }, [activeBottleId])

  const open = (page: WorkbenchPage) => {
    onOpen()
    setSection(page)
  }

  return <>
    <div className="arcane-tome-summary" data-workbench={potions !== null || undefined}>
      <div className="arcane-workbench" role="group" aria-label="Arcane workbench">
        <button type="button" className="arcane-workbench__tome" aria-expanded={section === 'tome'}
          aria-haspopup="dialog" onClick={() => open('tome')}>
          {potions && <img src={lectern} alt="" />}
          <span>{potions ? 'Tome' : 'Tome & ingredients'}</span>
        </button>
        {potions && <>
          <button ref={cauldronRef} type="button" className="arcane-workbench__pot" aria-expanded={section === 'cauldron'}
            aria-haspopup="dialog" onClick={() => open('cauldron')}>
            <img src={cauldron} alt="" />
            <span>Cauldron <b className="arcane-workbench__wide-units">{potUnits}/{potions.capacity}</b>
              <b className="arcane-workbench__mobile-units">{mobilePotUnits}/{potions.capacity}</b></span>
            <progress className="arcane-workbench__fill" value={mobilePotUnits} max={potions.capacity} aria-hidden="true" />
            {stir && <i
              key={stir.spinKey}
              className="arcane-workbench__stir"
              data-static={staticStir || undefined}
              aria-hidden="true"
              onAnimationEnd={event => {
                if (event.animationName !== 'arcane-cauldron-stir') return
                setStirredSpinKey(stir.spinKey)
                setStir(current => current === stir ? null : current)
              }}
            >
              <img className="arcane-workbench__stir-pot" src={cauldron} alt="" />
            </i>}
          </button>
          <button ref={shelfRef} type="button" className="arcane-workbench__shelf" aria-expanded={section === 'shelf'}
            aria-haspopup="dialog" onClick={() => open('shelf')}>
            <ArcanePotionShelfArtwork bottles={potions.bottles} />
            <span className="arcane-workbench__shelf-label">Potion shelf <b>{potionCount}</b></span>
          </button>
        </>}
      </div>
      <output aria-live="polite">
        {wideStatus && <span className="arcane-workbench__wide-status">{wideStatus}</span>}
        {potions?.active && <span className="arcane-workbench__wide-status arcane-workbench__active-potion">
          {potions.active.bottle.name} · {potions.active.spinsRemaining} left
        </span>}
        <span className="arcane-workbench__mobile-status">
          {activePage ? <span><b>Tome</b> · {activePage.label} · {remainingSpins(state.boostedSpinsRemaining)}</span>
            : <span><b>No active Tome page</b>{state.grimoireReady && ' · Grimoire ready'}</span>}
          {potions && (readyNames ? <span><b>{potions.active ? 'On the shelf' : 'Ready potions'}</b> · {readyNames}</span>
            : <span>No potions ready</span>)}
          {potions?.active && <span><b>Active potion</b> · {potions.active.bottle.name} · {remainingSpins(potions.active.spinsRemaining)}</span>}
        </span>
      </output>
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
      cabinetRef={cabinetRef} cauldronRef={cauldronRef} shelfRef={shelfRef} />}
    {section && createPortal(
      <div className="arcane-tome-backdrop" onMouseDown={event => {
        if (event.target === event.currentTarget) setSection(null)
      }}>
        <section className="arcane-tome-dialog" data-section={section} ref={dialogRef} tabIndex={-1}
          role="dialog" aria-modal="true" aria-labelledby={headingId}>
          <header>
            <h2 id={headingId}>{potions ? workbenchLabels[section] : 'Tome & ingredients'}</h2>
            <button type="button" className="arcane-tome-close" aria-label="Close workbench"
              onClick={() => setSection(null)}><SlotDialogCloseIcon /></button>
          </header>
          {section === 'tome' && <ArcaneTomeBook state={state} disabled={disabled} reducedMotion={reducedMotion}
            showIngredientTotals={potions === null} onChangePage={onChangePage} />}
          {section === 'cauldron' && potions && <Cauldron state={potions} />}
          {section === 'shelf' && potions && <PotionShelf state={potions} disabled={disabled} onUse={id => {
            pendingBottle.current = id
            onUsePotion(id)
          }} />}
          {error && <p role="alert">{error}</p>}
        </section>
      </div>,
      document.body,
    )}
  </>
}
