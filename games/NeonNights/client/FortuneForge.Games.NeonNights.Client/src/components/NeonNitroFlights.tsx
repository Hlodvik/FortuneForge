import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { nitroWildFlightDelay, nitroWildFlightMs } from '../nitroPresentation.js'

type Landing = { x: number; y: number; width: number; height: number }

export function NeonNitroFlights({ reel, image }: { reel: number; image: string }) {
  const layer = useRef<HTMLDivElement>(null)
  const [landings, setLandings] = useState<Landing[]>([])

  useLayoutEffect(() => {
    const overlay = layer.current
    const cabinet = overlay?.parentElement
    if (!overlay || !cabinet) return

    const measure = () => {
      const origin = overlay.getBoundingClientRect()
      const cells = cabinet.querySelectorAll<HTMLElement>(`.slot-symbol[data-reel-index="${reel}"]`)
      setLandings(Array.from(cells, cell => {
        const tile = cell.getBoundingClientRect()
        return { x: tile.left + tile.width / 2 - origin.left, y: tile.top + tile.height / 2 - origin.top,
          width: tile.width * .78, height: tile.height * .81 }
      }))
    }
    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [reel])

  return <div className="neon-nitro-flights" ref={layer} aria-hidden="true">
    {landings.map(({ x, y, width, height }, row) => <img key={row} src={image} alt=""
      className="neon-nitro-flight" style={{ left: x, top: y, width, height,
        '--nitro-flight-delay': `${nitroWildFlightDelay(row)}ms`, '--nitro-flight-duration': `${nitroWildFlightMs}ms`,
        '--nitro-flight-origin': `${-y - height * .6}px`,
      } as CSSProperties} />)}
  </div>
}
