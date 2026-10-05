import { europeanWheelOrder } from './roulettePresentation'

/** Wheel-order preview; its containing button opens the existing neighbours controls. */
export function RouletteRacetrack() {
  return <svg viewBox="0 0 600 108" aria-hidden="true" focusable="false">
    <rect x="3" y="3" width="594" height="102" rx="51" />
    <rect x="30" y="28" width="540" height="52" rx="26" />
    {europeanWheelOrder.map((pocket, index) => {
      const top = index < 19
      const x = 43 + (top ? index : 36 - index) * 28.5
      return <g key={pocket}><path d={top ? `M${x - 14},5 V28` : `M${x - 14},80 V103`} /><text x={x} y={top ? 22 : 97}>{pocket}</text></g>
    })}
    <text className="track-title" x="300" y="60">NEIGHBOURS</text>
  </svg>
}
