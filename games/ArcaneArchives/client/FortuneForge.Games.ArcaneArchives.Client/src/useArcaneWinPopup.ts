import { useEffect, useRef, useState, type RefObject } from 'react'
import { getPaylineWinTimings } from '@fortuneforge/slot-client-core/slotWinPresentation'
import { getArcaneWinPresentationTier } from './arcaneWinPresentation'

type ArcaneWinPopup = Readonly<{
  id: string
  amount: number
  tier: 'win' | 'big' | 'jackpot'
  flying: boolean
  toTotal: boolean
  left: number
  top: number
  travelX: number
  travelY: number
  endScale: number
  duration: number
}>

/** Presentation of an already-settled award; this never changes the wallet. */
export function useArcaneWinPopup({
  amount, wager, resultKey, visible, interrupted, reducedMotion, fast, collection, multipleAwards,
  cabinetRef, totalRef, amountRef,
}: {
  amount: number
  wager: number
  resultKey: number | string
  visible: boolean
  interrupted: boolean
  reducedMotion: boolean
  fast: boolean
  collection: boolean
  multipleAwards: boolean
  cabinetRef: RefObject<HTMLDivElement | null>
  totalRef: RefObject<HTMLDivElement | null>
  amountRef: RefObject<HTMLElement | null>
}) {
  const [popup, setPopup] = useState<ArcaneWinPopup | null>(null)
  const cancelledResult = useRef<number | string | null>(null)
  const fastRef = useRef(fast)
  fastRef.current = fast

  useEffect(() => {
    if (visible && interrupted) cancelledResult.current = resultKey
    if (!visible || interrupted || cancelledResult.current === resultKey || amount <= 0 || reducedMotion) {
      setPopup(null)
      return
    }
    const fast = fastRef.current
    const tier = getArcaneWinPresentationTier(amount, wager)
    const timing = getPaylineWinTimings(tier, fast)
    let frame = 0
    let flightTimer = 0
    let clearTimer = 0
    const startTimer = window.setTimeout(() => {
      const rect = cabinetRef.current?.getBoundingClientRect()
      const left = rect ? rect.left + rect.width / 2 : window.innerWidth / 2
      const top = rect ? rect.top + rect.height * 0.38 : window.innerHeight * 0.38
      const initial: ArcaneWinPopup = { id: `${resultKey}:count`, amount: 0, tier, flying: false,
        toTotal: false, left, top, travelX: 0, travelY: 0, endScale: 0.18, duration: timing.hold }
      setPopup(initial)
      const started = performance.now()
      const count = (now: number) => {
        const progress = Math.min(1, (now - started) / timing.count)
        const value = Math.round(amount * (1 - Math.pow(1 - progress, 3)) * 100) / 100
        setPopup(current => current?.id === initial.id ? { ...current, amount: value } : current)
        if (progress < 1) frame = requestAnimationFrame(count)
      }
      frame = requestAnimationFrame(count)
      flightTimer = window.setTimeout(() => {
        cancelAnimationFrame(frame)
        const total = multipleAwards ? totalRef.current : null
        const target = total ?? document.querySelector<HTMLElement>('[data-game-navbar] .site-header-balance')
        const destination = target?.getBoundingClientRect()
        const source = amountRef.current?.getBoundingClientRect()
        const flightLeft = source ? source.left + source.width / 2 : left
        const flightTop = source ? source.top + source.height / 2 : top
        const totalSize = total ? parseFloat(getComputedStyle(total).fontSize) : 0
        const amountSize = amountRef.current ? parseFloat(getComputedStyle(amountRef.current).fontSize) : 0
        setPopup({ ...initial, id: `${resultKey}:flight`, amount, flying: true, toTotal: total !== null,
          left: flightLeft, top: flightTop,
          travelX: destination ? destination.left + destination.width / 2 - flightLeft : 0,
          travelY: destination ? destination.top + destination.height / 2 - flightTop : -24,
          endScale: totalSize > 0 && amountSize > 0 ? totalSize / amountSize : 0.18,
          duration: timing.flight })
        clearTimer = window.setTimeout(() => setPopup(null), timing.flight)
      }, timing.hold)
    }, (fast ? 40 : 110) + (collection ? 500 : 0))
    return () => {
      window.clearTimeout(startTimer)
      window.clearTimeout(flightTimer)
      window.clearTimeout(clearTimer)
      cancelAnimationFrame(frame)
      setPopup(null)
    }
  }, [amount, amountRef, cabinetRef, collection, interrupted, multipleAwards, reducedMotion, resultKey, totalRef, visible, wager])

  return popup
}
