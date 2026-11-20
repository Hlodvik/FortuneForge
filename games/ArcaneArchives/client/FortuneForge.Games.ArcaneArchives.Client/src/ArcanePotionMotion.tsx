// Edited: 2026-11-18T19:10:04-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:43:19-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:19:50-06:00 | Computer: VINNABOK
import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import type { SlotPotionSoundEvent } from '@fortuneforge/slot-client-core'
import type { SlotPotionBottle, SlotPotionState, SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import { cauldronOpening } from './ArcaneIngredientFlights'
import { ArcanePotionPour } from './ArcanePotionPour'
import { getPotionArtwork, type PotionArtwork } from './potionArtwork'
import './potionMotion.css'

type Point = { x: number; y: number }

type Action = {
  bottle: SlotPotionBottle
  kind: SlotPotionSoundEvent
  bottlePosition: Point | null
}

type WorkbenchRefs = {
  cabinetRef: RefObject<HTMLElement | null>
  cauldronRef: RefObject<HTMLButtonElement | null>
  bottlesRef: RefObject<HTMLUListElement | null>
}

type PotionMotionProps = WorkbenchRefs & Pick<SlotTomePanelProps, 'confirmedPotionUse' | 'onPotionCue'> & {
  state: SlotPotionState
  arrival: number | null
  settledSpinKey: number | null
  reducedMotion: boolean
  activationOriginRef: RefObject<(Point & { id: number }) | null>
}

function centerOf(element?: Element | null): Point | null {
  const bounds = element?.getBoundingClientRect()
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) return null
  return { x: bounds.left + bounds.width / 2, y: bounds.top + bounds.height / 2 }
}

function bottleCenter(row: HTMLUListElement | null, id: SlotPotionBottle['id']) {
  return centerOf(row?.querySelector(`button[data-bottle-id="${id}"]`))
}

export function ArcanePotionMotion({
  state, arrival, settledSpinKey, confirmedPotionUse, onPotionCue, reducedMotion,
  cabinetRef, cauldronRef, bottlesRef, activationOriginRef,
}: PotionMotionProps) {
  const previous = useRef({ state, arrival, settledSpinKey, confirmedPotionUse })
  const awaitingArrival = useRef<Action[]>([])
  const playedAction = useRef<Action | null>(null)
  const [queue, setQueue] = useState<Action[]>([])
  const [reels, setReels] = useState<Element | null>(null)
  const [rimBottle, setRimBottle] = useState<number | null>(null)
  const action = queue[0]

  useLayoutEffect(() => {
    setReels(cabinetRef.current?.querySelector('.slot-machine__reels') ?? null)
  }, [cabinetRef])

  useLayoutEffect(() => {
    const before = previous.current
    previous.current = { state, arrival, settledSpinKey, confirmedPotionUse }
    if (settledSpinKey !== null && settledSpinKey !== before.settledSpinKey) {
      const known = new Set(before.state.bottles.map(bottle => bottle.id))
      for (const bottle of state.bottles) {
        if (!known.has(bottle.id)) {
          awaitingArrival.current.push({ bottle, kind: 'bottling', bottlePosition: bottleCenter(bottlesRef.current, bottle.id) })
        }
      }
    }

    const ready = arrival !== null && arrival !== before.arrival ? awaitingArrival.current.splice(0) : []
    const activeBottle = state.active?.bottle
    if (activeBottle && activeBottle.id === confirmedPotionUse && confirmedPotionUse !== before.confirmedPotionUse) {
      const origin = activationOriginRef.current
      const bottlePosition = origin?.id === activeBottle.id ? origin : bottleCenter(bottlesRef.current, activeBottle.id)
      ready.push({ bottle: activeBottle, kind: 'activation', bottlePosition })
    }
    if (ready.length) setQueue(current => [...current, ...ready])
  }, [activationOriginRef, arrival, bottlesRef, confirmedPotionUse, settledSpinKey, state])

  useEffect(() => {
    if (!action) return
    if (action.kind === 'activation' && action.bottle.id !== state.active?.bottle.id) {
      setQueue(current => current[0] === action ? current.slice(1) : current)
      return
    }
    if (playedAction.current !== action) {
      playedAction.current = action
      onPotionCue?.(action.kind)
    }
    if (reducedMotion) setQueue(current => current[0] === action ? current.slice(1) : current)
  }, [action, onPotionCue, reducedMotion, state.active?.bottle.id])

  const active = state.active?.bottle
  const activeArt = active && getPotionArtwork(active)
  const opening = !!active && !reducedMotion &&
    queue.some(item => item.kind === 'activation' && item.bottle.id === active.id)
  const complete = () => setQueue(current => current[0] === action ? current.slice(1) : current)
  return <>
    {active && activeArt && reels?.parentElement && (!opening || rimBottle === active.id) && createPortal(
      <PotionFrame key={active.id} art={activeArt} reducedMotion={reducedMotion} opening={opening} />,
      reels.parentElement,
    )}
    {action?.kind === 'activation' && active?.id === action.bottle.id && activeArt && !reducedMotion &&
      <ArcanePotionPour key={action.bottle.id} art={activeArt} reels={reels} origin={action.bottlePosition}
        onImpact={() => setRimBottle(action.bottle.id)} onComplete={complete} />}
    {action?.kind === 'bottling' && !reducedMotion &&
      <BottlingClip key={action.bottle.id} action={action} cauldronRef={cauldronRef} bottlesRef={bottlesRef}
        onComplete={complete} />}
  </>
}

function PotionFrame({ art, reducedMotion, opening }: { art: PotionArtwork; reducedMotion: boolean; opening: boolean }) {
  const anchor = useRef<HTMLDivElement>(null)
  const [moving, setMoving] = useState(false)
  const [reveal, setReveal] = useState(opening)

  useLayoutEffect(() => {
    const element = anchor.current
    if (!element) return
    function fit() {
      if (!element) return
      const reel = element.getBoundingClientRect()
      const side = reel.width * .145
      const left = Math.min(side, Math.max(0, reel.left))
      const right = Math.min(side, Math.max(0, document.documentElement.clientWidth - reel.right))
      element.style.setProperty('--arcane-potion-rim-left', `${-left}px`)
      element.style.setProperty('--arcane-potion-rim-right', `${-right}px`)
      element.style.setProperty('--arcane-potion-rim-image-left', `${left - side}px`)
      element.style.setProperty('--arcane-potion-rim-image-width', `${reel.width + 2 * side}px`)
    }
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(fit)
    observer?.observe(element)
    window.addEventListener('resize', fit)
    fit()
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', fit)
    }
  }, [])

  return <div ref={anchor} className="arcane-potion-frame" data-opening={reveal && !reducedMotion || undefined}
    data-ambient={!art.rim && !reducedMotion || undefined} style={{ '--potion-glow': art.glow } as CSSProperties}
    data-moving={moving && !reducedMotion || undefined} aria-hidden="true"
    onAnimationEnd={event => { if (event.target === event.currentTarget) setReveal(false) }}>
    <div className="arcane-potion-frame__crop">
      <img className="arcane-potion-frame__still" src={art.rimStill} alt="" />
      {!reducedMotion && art.rim && <img className="arcane-potion-frame__moving" src={art.rim} alt=""
        onLoad={() => setMoving(true)} onError={() => setMoving(false)} />}
    </div>
  </div>
}

function BottlingClip({ action, cauldronRef, bottlesRef, onComplete }: Pick<WorkbenchRefs, 'cauldronRef' | 'bottlesRef'> & {
  action: Action
  onComplete: () => void
}) {
  const [transfer, setTransfer] = useState(false)
  const timer = useRef<number | undefined>(undefined)
  const completed = useRef(onComplete)
  completed.current = onComplete
  const art = getPotionArtwork(action.bottle)

  useEffect(() => {
    // Move on if an animation is unavailable or slow to download.
    timer.current = window.setTimeout(() => completed.current(), 8000)
    return () => window.clearTimeout(timer.current)
  }, [])

  const row = bottlesRef.current
  const liveBottle = bottleCenter(row, action.bottle.id)
  const cauldron = cauldronRef.current && cauldronOpening(cauldronRef.current)
  const fallback = centerOf(row) ?? cauldron
  const destination = liveBottle ?? action.bottlePosition ?? fallback
  const origin = cauldron
  if (!art || !origin || !destination) return null

  const style = {
    left: origin.x,
    top: origin.y,
    '--potion-glow': art.glow,
    '--potion-transfer-x': `${destination.x - origin.x}px`,
    '--potion-transfer-y': `${destination.y - origin.y}px`,
  } as CSSProperties

  return createPortal(
    <div className="arcane-potion-motion" aria-hidden="true">
      {transfer || !art.bottling ? <img className="arcane-potion-motion__transfer" style={style} src={art.bottle} alt=""
        onAnimationEnd={onComplete} onError={onComplete} />
        : <div className="arcane-potion-motion__clip" style={style}>
          <img src={art.bottling + '#' + action.bottle.id} alt="" onLoad={() => {
            window.clearTimeout(timer.current)
            timer.current = window.setTimeout(() => {
              setTransfer(true)
              timer.current = window.setTimeout(() => completed.current(), 1000)
            }, 2200)
          }} onError={onComplete} />
        </div>}
    </div>,
    document.body,
  )
}
