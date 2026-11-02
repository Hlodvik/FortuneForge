import chipQuarter from './assets/chip-025.png?no-inline'
import chipHalf from './assets/chip-050.png?no-inline'
import chipOne from './assets/chip-1.png?no-inline'
import chipTwo from './assets/chip-2.png?no-inline'
import chipFive from './assets/chip-5.png?no-inline'
import chipTen from './assets/chip-10.png?no-inline'
import chipTwentyFive from './assets/chip-25.png?no-inline'
import chipFifty from './assets/chip-50.png?no-inline'

const chipArt: Readonly<Record<number, string>> = {
  0.25: chipQuarter,
  0.5: chipHalf,
  1: chipOne,
  2: chipTwo,
  5: chipFive,
  10: chipTen,
  25: chipTwentyFive,
  50: chipFifty,
}

export type RouletteChipProps = Readonly<{ value: number; className?: string }>

/** Uses the same small, numbered chip art as the other Fortune Forge table games. */
export function RouletteChip({ value, className = '' }: RouletteChipProps) {
  const source = chipArt[value]
  // These three source files have a little extra transparent padding.
  const sizeClass = value === 50 ? ' ff-roulette-chip-art--size-50' : value === 0.25 ? ' ff-roulette-chip-art--size-quarter' : value === 0.5 ? ' ff-roulette-chip-art--size-half' : ''
  const classes = `ff-roulette-chip-art${sizeClass}${className ? ` ${className}` : ''}`

  if (source) return <span className={classes} aria-hidden="true"><img src={source} alt="" draggable={false} /></span>

  // The shared chip set ends at R50. A R100 wager is a pair of R50 chips.
  if (value === 100) return <span className={`${classes} ff-roulette-chip-art--pair`} aria-hidden="true"><img src={chipFifty} alt="" draggable={false} /><img src={chipFifty} alt="" draggable={false} /><b>100</b></span>

  // Some externally configured tables have nonstandard chip increments.
  // Keep those denominations legible without showing an incorrect numbered asset.
  return <span className={`${classes} ff-roulette-chip-art--custom`} aria-hidden="true"><b>{value}</b></span>
}
