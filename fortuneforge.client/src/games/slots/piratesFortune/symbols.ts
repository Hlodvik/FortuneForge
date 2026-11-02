import type { SlotSymbolSet } from '../../../features/slots/config/symbolSets'
import { pirateSymbolImages } from './responsiveSymbols'

type PirateSymbolImage = (typeof pirateSymbolImages)[keyof typeof pirateSymbolImages]

const staticSymbol = (id: keyof SlotSymbolSet['definitions'], label: string, images: PirateSymbolImage) => ({
  id,
  label,
  image: images[192],
  animatedImage: images[192],
  responsiveImages: images,
})

const doubloonValueSymbol = (
  id: 'RAND_05' | 'RAND_1' | 'RAND_15' | 'RAND_2' | 'RAND_3' | 'RAND_4' | 'RAND_5',
  wagerMultiplier: number,
) => ({
  id,
  label: `${wagerMultiplier}× wager doubloon`,
  image: pirateSymbolImages.goldDoubloon[192],
  animatedImage: pirateSymbolImages.goldDoubloon[192],
  responsiveImages: pirateSymbolImages.goldDoubloon,
  wagerMultiplier,
})

export const PIRATES_FORTUNE_SYMBOLS: SlotSymbolSet = {
  id: 'pirates-fortune-treasures-v1',
  serverSymbolSetId: 'wukong-treasures-v3',
  definitions: {
    '2': staticSymbol('2', 'Rum bottle', pirateSymbolImages.rumBottleV2),
    '3': staticSymbol('3', 'Brass compass', pirateSymbolImages.compass),
    '4': staticSymbol('4', 'Naval cutlass', pirateSymbolImages.cutlass),
    '5': staticSymbol('5', 'Flintlock pistol', pirateSymbolImages.flintlockPistol),
    '6': staticSymbol('6', 'Charted island map', pirateSymbolImages.treasureMap),
    '7': staticSymbol('7', 'Gold pirate coin', pirateSymbolImages.goldPirateCoin),
    ACE: staticSymbol('ACE', 'Moonlit pearl wild', pirateSymbolImages.pearl),
    FREE: staticSymbol('FREE', 'Treasure Map bonus', pirateSymbolImages.treasureMapBonus),
    POWER: staticSymbol('POWER', 'Jolly Roger power flag', pirateSymbolImages.jollyRoger),
    BANANA: staticSymbol('BANANA', 'Powder keg volley', pirateSymbolImages.powderKegs),
    PAW: staticSymbol('PAW', 'Doubloon Purse collector', pirateSymbolImages.doubloonPurse),
    RAND_05: doubloonValueSymbol('RAND_05', 0.5),
    RAND_1: doubloonValueSymbol('RAND_1', 1),
    RAND_15: doubloonValueSymbol('RAND_15', 1.5),
    RAND_2: doubloonValueSymbol('RAND_2', 2),
    RAND_3: doubloonValueSymbol('RAND_3', 3),
    RAND_4: doubloonValueSymbol('RAND_4', 4),
    RAND_5: doubloonValueSymbol('RAND_5', 5),
    SEAL_SYNC: staticSymbol('SEAL_SYNC', 'Pear-cut ruby collection gem', pirateSymbolImages.rubyGem),
    SEAL_ROWS: staticSymbol('SEAL_ROWS', 'Lapis lozenge high-tide gem', pirateSymbolImages.sapphireGem),
    SEAL_PAW: staticSymbol('SEAL_PAW', 'Step-cut orange skull-storm gem', pirateSymbolImages.amberGem),
    SEAL_RAND: staticSymbol('SEAL_RAND', 'Marquise emerald doubloon gem', pirateSymbolImages.emeraldGem),
  },
  guideEntries: [
    { symbol: '2', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '4×' },
    { symbol: '3', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '2×' },
    { symbol: '4', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '7×' },
    { symbol: '5', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '6×' },
    { symbol: '6', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '8×' },
    { symbol: '7', firstLabel: '3–4', firstValue: '3×', secondLabel: '5', secondValue: '11×' },
    { symbol: 'ACE', firstLabel: '3–4', firstValue: '5×', secondLabel: '5', secondValue: '18×' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '7 Island Search rounds' },
    { symbol: 'POWER', firstLabel: '3–4', firstValue: '2× +1 point', secondLabel: '5', secondValue: '4× +2 points' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'collects doubloons', secondLabel: '2 purses', secondValue: 'double' },
    {
      symbol: 'RAND_05',
      firstLabel: 'Values',
      firstValue: '0.5×–5× wager',
      secondLabel: 'Purse',
      secondValue: 'collects every coin',
      artworkValueLabel: '',
    },
    { symbol: 'SEAL_SYNC', firstLabel: 'Any', firstValue: 'builds chance', secondLabel: 'Feature', secondValue: '10 free games' },
    { symbol: 'SEAL_ROWS', firstLabel: 'Any', firstValue: 'builds chance', secondLabel: 'Feature', secondValue: '10 free games' },
    { symbol: 'SEAL_PAW', firstLabel: 'Any', firstValue: 'builds chance', secondLabel: 'Feature', secondValue: '10 free games' },
    { symbol: 'SEAL_RAND', firstLabel: 'Any', firstValue: 'builds chance', secondLabel: 'Feature', secondValue: '10 free games' },
  ],
}
