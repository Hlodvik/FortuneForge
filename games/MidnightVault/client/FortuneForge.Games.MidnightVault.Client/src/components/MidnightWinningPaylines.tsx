import { WinningPaylines, type WinningPaylinesProps, type ActivePaidPath } from '@fortuneforge/client-presentation/slots/paylines'
import '../midnightWinningPaylines.css'

export type { ActivePaidPath }
type MidnightWinningPaylinesProps = Omit<WinningPaylinesProps, 'appearance'> & {
  palette?: 'gold' | 'rainbow' | 'cabinet'
}

/** Compatibility appearance for existing games; drawing is a neutral primitive. */
export function MidnightWinningPaylines({ palette = 'gold', ...props }: MidnightWinningPaylinesProps) {
  const isRainbow = palette === 'rainbow'
  return <WinningPaylines {...props}
    boardSelector=".slots-page__cabinet-spotlight"
    reelsSelector=".slot-machine__reels"
    cellSelector=".slot-symbol[data-reel-index][data-row-index]"
    appearance={{
      classPrefix: 'midnight-winning-paylines',
      variant: palette,
      steadyTrail: isRainbow,
      steadyTrailClassName: isRainbow ? 'rainbow-realm__steady-payline' : undefined,
      reducedMotionTrail: palette === 'cabinet',
      labelInset: isRainbow ? 0 : 64,
      labelOffset: isRainbow ? 0 : 16,
      labelMinimumY: isRainbow ? 0 : 26,
      labelBaseline: isRainbow ? 'central' : undefined,
    }} />
}
