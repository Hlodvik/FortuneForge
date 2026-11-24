import wageringDocument from './assets/contracts/wagering.json?raw'

export type PharaohWagering = Readonly<{
  pointValueInCents: number
  minimumWagerPoints: number
  defaultWagerPoints: number
  maximumWagerPoints: number
  wagerIncrementPoints: number
}>

// The same game-owned document is embedded by the paid server admission policy.
export const PHARAOH_WAGERING: PharaohWagering = Object.freeze(JSON.parse(wageringDocument) as PharaohWagering)
export const PHARAOH_WAGER_OPTIONS: readonly number[] = Object.freeze(Array.from({
  length: (PHARAOH_WAGERING.maximumWagerPoints - PHARAOH_WAGERING.minimumWagerPoints) / PHARAOH_WAGERING.wagerIncrementPoints + 1,
}, (_, index) => PHARAOH_WAGERING.minimumWagerPoints + index * PHARAOH_WAGERING.wagerIncrementPoints))
