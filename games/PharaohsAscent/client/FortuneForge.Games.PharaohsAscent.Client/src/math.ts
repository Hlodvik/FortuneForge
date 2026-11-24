import mathDocument from './assets/contracts/math-v3.json?raw'
import { formatCalibratedMultiplier } from '@fortuneforge/slot-game-runtime/engine-guide'

export type PharaohMath = Readonly<{
  engineVersion: string
  paytableId: string
  payoutMultiplier: number
  tombMinimumScarabs: number
  payouts: readonly Readonly<{ symbolId: string; matchLength: number; multiplier: number }>[]
}>

// Current paid math and its visible paytable share this game-owned document.
export const PHARAOH_MATH: PharaohMath = Object.freeze(JSON.parse(mathDocument) as PharaohMath)
export const PHARAOH_PAYLINE_GUIDE = [...new Set(PHARAOH_MATH.payouts.map(entry => entry.symbolId))].map(symbol => {
  const three = PHARAOH_MATH.payouts.find(entry => entry.symbolId === symbol && entry.matchLength === 3)
  const five = PHARAOH_MATH.payouts.find(entry => entry.symbolId === symbol && entry.matchLength === 5)
  if (!three || !five) throw new Error(`The Pharaoh paytable is incomplete for ${symbol}.`)
  return {
    symbol,
    firstLabel: symbol === 'ACE' ? '3' : '3–4',
    firstValue: formatCalibratedMultiplier(three.multiplier, PHARAOH_MATH.payoutMultiplier),
    secondLabel: '5',
    secondValue: formatCalibratedMultiplier(five.multiplier, PHARAOH_MATH.payoutMultiplier),
  }
})
