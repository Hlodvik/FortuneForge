// Edited: 2026-11-18T19:10:04-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:43:19-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:19:50-06:00 | Computer: VINNABOK
import { useEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import type { SlotPotionSoundEvent } from '@fortuneforge/slot-client-core'
import type { SlotPotionBottle, SlotPotionState, SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { cauldronOpening } from './ArcaneIngredientFlights'
import { getPotionArtwork } from './potionArtwork'
import './potionMotion.css'

type Action = {
  bottle: SlotPotionBottle
  kind: SlotPotionSoundEvent
}

type WorkbenchRefs = {
  cabinetRef: RefObject<HTMLElement | null>
  cauldronRef: RefObject<HTMLButtonElement | null>
  shelfRef: RefObject<HTMLButtonElement | null>
}

type PotionMotionProps = WorkbenchRefs & Pick<SlotTomePanelProps, 'confirmedPotionUse' | 'onPotionCue'> & {
  state: SlotPotionState
  arrival: number | null
  settledSpinKey: number | null
  reducedMotion: boolean
}

const clipDuration = { bottling: 2200, activation: 1550 }

export function ArcanePotionMotion({
  state, arrival, settledSpinKey, confirmedPotionUse, onPotionCue, reducedMotion, cabinetRef, cauldronRef, shelfRef,
}: PotionMotionProps) {
  const previous = useRef({ state, arrival, settledSpinKey, confirmedPotionUse })
  const awaitingArrival = useRef<Action[]>([])
  const playedAction = useRef<Action | null>(null)
  const [queue, setQueue] = useState<Action[]>([])
  const action = queue[0]

  useEffect(() => {
    const before = previous.current
    previous.current = { state, arrival, settledSpinKey, confirmedPotionUse }
    if (settledSpinKey !== null && settledSpinKey !== before.settledSpinKey) {
      const known = new Set(before.state.bottles.map(bottle => bottle.id))
      for (const bottle of state.bottles) {
        if (!known.has(bottle.id)) awaitingArrival.current.push({ bottle, kind: 'bottling' })
      }
    }

    const ready = arrival !== null && arrival !== before.arrival ? awaitingArrival.current.splice(0) : []
    const activeBottle = state.active?.bottle
    if (activeBottle && activeBottle.id === confirmedPotionUse && confirmedPotionUse !== before.confirmedPotionUse) {
      ready.push({ bottle: activeBottle, kind: 'activation' })
    }
    if (ready.length) setQueue(current => [...current, ...ready])
  }, [arrival, confirmedPotionUse, settledSpinKey, state])

  useEffect(() => {
    if (!action) return
    if (playedAction.current !== action) {
      playedAction.current = action
      onPotionCue?.(action.kind)
    }
    if (reducedMotion) setQueue(current => current[0] === action ? current.slice(1) : current)
  }, [action, onPotionCue, reducedMotion])

  const reelSurface = cabinetRef.current?.querySelector('.slot-machine__reels')
  const active = state.active
  const activeArt = active && getPotionArtwork(active.bottle.ingredientId)
  return <>
    {activeArt && reelSurface && createPortal(
      <div className="arcane-potion-aura" style={{ '--potion-glow': activeArt.glow } as CSSProperties} aria-hidden="true">
        {!reducedMotion && <img src={activeArt.effect} alt="" />}
      </div>,
      reelSurface,
    )}
    {action && !reducedMotion && <PotionClip key={action.kind + action.bottle.id} action={action}
      cabinetRef={cabinetRef} cauldronRef={cauldronRef} shelfRef={shelfRef}
      onComplete={() => setQueue(current => current[0] === action ? current.slice(1) : current)} />}
  </>
}

function PotionClip({ action, cabinetRef, cauldronRef, shelfRef, onComplete }: WorkbenchRefs & {
  action: Action
  onComplete: () => void
}) {
  const [transfer, setTransfer] = useState(false)
  const timer = useRef<number | undefined>(undefined)
  const completed = useRef(onComplete)
  completed.current = onComplete
  const bottling = action.kind === 'bottling'
  const art = getPotionArtwork(action.bottle.ingredientId)

  useEffect(() => {
    // Move on if an animation is unavailable or slow to download.
    timer.current = window.setTimeout(() => completed.current(), 8000)
    return () => window.clearTimeout(timer.current)
  }, [])

  const shelfButton = shelfRef.current
  const ledge = shelfButton?.querySelector('[data-arcane-shelf-ledge]')?.getBoundingClientRect()
  const shelf = ledge && ledge.width > 0 ? ledge : shelfButton?.getBoundingClientRect()
  const cabinet = cabinetRef.current?.getBoundingClientRect()
  const cauldron = cauldronRef.current && cauldronOpening(cauldronRef.current)
  const shelfX = shelf ? shelf.left + shelf.width / 2 : 0
  const shelfY = shelf ? shelf.top + shelf.height / 2 : 0
  const cabinetX = cabinet ? cabinet.left + cabinet.width / 2 : shelfX
  const origin = bottling ? cauldron : { x: shelfX, y: shelfY }
  if (!art || !origin || !shelf?.width) return null

  const style = {
    left: origin.x,
    top: origin.y,
    '--potion-glow': art.glow,
    '--potion-transfer-x': `${shelfX - origin.x}px`,
    '--potion-transfer-y': `${shelfY - origin.y}px`,
  } as CSSProperties

  return createPortal(
    <div className="arcane-potion-motion" aria-hidden="true">
      {transfer ? <img className="arcane-potion-motion__transfer" style={style} src={art.bottle} alt=""
        onAnimationEnd={onComplete} onError={onComplete} />
        : <div className="arcane-potion-motion__clip" style={style} data-action={action.kind}
          data-face-right={!bottling && cabinetX >= shelfX || undefined}>
          <img src={art[action.kind] + '#' + action.bottle.id} alt="" onLoad={() => {
            window.clearTimeout(timer.current)
            timer.current = window.setTimeout(() => {
              if (bottling) {
                setTransfer(true)
                timer.current = window.setTimeout(() => completed.current(), 1000)
              } else {
                completed.current()
              }
            }, clipDuration[action.kind])
          }} onError={onComplete} />
        </div>}
    </div>,
    document.body,
  )
}
