// Edited: 2026-11-18T18:34:08-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:20:26-06:00 | Computer: VINNABOK
// Edited: 2026-11-18T18:11:03-06:00 | Computer: VINNABOK
import { useLayoutEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import type { SlotTomePanelProps } from '@fortuneforge/slot-game-runtime/experience'
import './ingredientFlights.css'

type IngredientFlightsProps = Pick<SlotTomePanelProps, 'cabinetRef' | 'settledSpin' | 'awarded' | 'reducedMotion'> & {
  cauldronRef: RefObject<HTMLButtonElement | null>
  onCollectionArrived: (spinKey: number) => void
}

type IngredientFlight = {
  key: string
  spinKey: number
  reel: number
  row: number
  image: string
  boosted: boolean
  last: boolean
  style: CSSProperties
}

export function ArcaneIngredientFlights({
  cabinetRef, cauldronRef, settledSpin, awarded, reducedMotion, onCollectionArrived,
}: IngredientFlightsProps) {
  const shownSpinKey = useRef<number | null>(null)
  const arrivedSpinKey = useRef<number | null>(null)
  const [flights, setFlights] = useState<IngredientFlight[]>([])
  const spinKey = settledSpin?.key
  const reels = settledSpin?.reels

  useLayoutEffect(() => {
    if (reducedMotion) setFlights(current => current.length ? [] : current)
    if (spinKey === undefined || !reels || !cauldronRef.current || arrivedSpinKey.current === spinKey) return
    const hasIngredients = Object.values(awarded).some(units => units > 0)
    if (reducedMotion) {
      shownSpinKey.current = spinKey
      if (hasIngredients) {
        arrivedSpinKey.current = spinKey
        onCollectionArrived(spinKey)
      }
      return
    }
    if (!hasIngredients || shownSpinKey.current === spinKey) return
    // Wait for the revealed reel DOM and its final layout to paint.
    let frame = requestAnimationFrame(() => {
      frame = requestAnimationFrame(() => {
        shownSpinKey.current = spinKey
        const newFlights = createIngredientFlights(spinKey, reels, awarded, cabinetRef.current, cauldronRef.current)
        if (newFlights.length) {
          setFlights(current => [...current, ...newFlights])
        } else {
          arrivedSpinKey.current = spinKey
          onCollectionArrived(spinKey)
        }
      })
    })
    return () => cancelAnimationFrame(frame)
  }, [awarded, cabinetRef, cauldronRef, onCollectionArrived, reducedMotion, reels, spinKey])

  if (reducedMotion || !flights.length) return null

  return createPortal(<div className="arcane-ingredient-flights" aria-hidden="true">
    {flights.map(flight => <div
      key={flight.key}
      className="arcane-ingredient-flight"
      data-reel-index={flight.reel}
      data-row-index={flight.row}
      style={flight.style}
      onAnimationEnd={() => {
        setFlights(current => current.filter(item => item.key !== flight.key))
        if (flight.last) {
          arrivedSpinKey.current = flight.spinKey
          onCollectionArrived(flight.spinKey)
        }
      }}
    >
      <img src={flight.image} alt="" />
      {flight.boosted && <span>×2</span>}
    </div>)}
  </div>, document.body)
}

export function cauldronOpening(cauldron: HTMLButtonElement) {
  const art = cauldron.querySelector('img')?.getBoundingClientRect()
  const hasVisibleArt = art && art.width > 0 && art.height > 0
  const target = hasVisibleArt ? art : cauldron.getBoundingClientRect()
  // The opening sits above the center of the full transparent art canvas.
  const opening = hasVisibleArt ? parseFloat(getComputedStyle(cauldron).getPropertyValue('--arcane-cauldron-opening')) / 100 : 0.5
  return { x: target.left + target.width / 2, y: target.top + target.height * opening }
}

function createIngredientFlights(
  spinKey: number,
  reels: NonNullable<SlotTomePanelProps['settledSpin']>['reels'],
  awarded: SlotTomePanelProps['awarded'],
  cabinet: HTMLDivElement | null,
  cauldron: HTMLButtonElement | null,
): IngredientFlight[] {
  if (!cabinet || !cauldron) return []

  const opening = cauldronOpening(cauldron)

  const landings = reels.flatMap((symbols, reel) => symbols.flatMap((symbol, row) =>
    Object.hasOwn(awarded, symbol) && awarded[symbol] > 0 ? [{ symbol, reel, row }] : [],
  ))
  const landingCounts: Record<string, number> = {}
  for (const { symbol } of landings) landingCounts[symbol] = (landingCounts[symbol] ?? 0) + 1

  const measured = landings.flatMap(({ symbol, reel, row }) => {
    const image = cabinet.querySelector<HTMLImageElement>(
      `.slot-symbol[data-reel-index="${reel}"][data-row-index="${row}"] .slot-symbol__image`,
    )
    if (!image || image.closest('.slot-symbol')?.getAttribute('data-symbol') !== symbol) return []
    const bounds = image.getBoundingClientRect()
    const source = image.getAttribute('src') || image.currentSrc
    if (!source || bounds.width <= 0 || bounds.height <= 0) return []
    const x = bounds.left + bounds.width / 2
    const y = bounds.top + bounds.height / 2
    const travelX = opening.x - x
    const travelY = opening.y - y
    return [{ symbol, reel, row, source, bounds, x, y, travelX, travelY }]
  })

  return measured.map(({ symbol, reel, row, source, bounds, x, y, travelX, travelY }, index): IngredientFlight => ({
    key: `${spinKey}:${reel}:${row}`,
    spinKey,
    reel,
    row,
    image: source,
    boosted: awarded[symbol] === 2 * landingCounts[symbol],
    last: index === measured.length - 1,
    style: {
      left: x,
      top: y,
      width: bounds.width,
      height: bounds.height,
      animationDelay: `${70 + index * 75}ms`,
      '--ingredient-flight-x': `${travelX}px`,
      '--ingredient-flight-y': `${travelY}px`,
      '--ingredient-flight-lift': `${Math.min(78, 20 + Math.hypot(travelX, travelY) * 0.13)}px`,
    } as CSSProperties,
  }))
}
