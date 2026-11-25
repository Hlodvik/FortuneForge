import document from './assets/contracts/guide.json?raw'
/** Presentation snapshot of this game's frozen server definition; never authorizes an award. */
export const guide = JSON.parse(document) as {
  gameId: string; payoutMultiplier: number; pointValueInCents: number; paylinePatternIds: number[]
  scatterRequiredSymbols: number; scatterAwardedSpins: number; collectionTarget: number; collectionAwardedSpins: number
  paylinePayouts: Record<string, { three: number; five: number }>
}
export const formatCalibratedMultiplier = (base: number, calibration = guide.payoutMultiplier) => `${Number((base * calibration).toFixed(6))}×`
export const lineGuide = Object.entries(guide.paylinePayouts).map(([symbol, payout]) => ({ symbol,
  firstLabel: symbol === 'ACE' ? '3' : '3–4', firstValue: formatCalibratedMultiplier(payout.three),
  secondLabel: '5', secondValue: formatCalibratedMultiplier(payout.five),
}))
