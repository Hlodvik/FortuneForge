import blueberryRowsCharm from '../../../assets/slots/games/rainbow-realm/blueberry-rows-charm.png'
import bottledSunshine from '../../../assets/slots/games/rainbow-realm/bottled-sunshine.png'
import kiwiColumnCharm from '../../../assets/slots/games/rainbow-realm/kiwi-column-charm.png'
import orangeBasketCharm from '../../../assets/slots/games/rainbow-realm/orange-basket-charm.png'
import rainbowAppleMedallion from '../../../assets/slots/games/rainbow-realm/rainbow-apple-medallion.png'
import rainbowBananaBunch from '../../../assets/slots/games/rainbow-realm/rainbow-banana-bunch.png'
import rainbowFruitToken from '../../../assets/slots/games/rainbow-realm/rainbow-fruit-token.png'
import rainbowOrchardGate from '../../../assets/slots/games/rainbow-realm/rainbow-orchard-gate.png'
import strawberrySyncCharm from '../../../assets/slots/games/rainbow-realm/strawberry-sync-charm.png'
import wickerFruitBasket from '../../../assets/slots/games/rainbow-realm/wicker-fruit-basket.png'
import cherrySymbol from '../../../assets/slots/symbols/cherry.gif'
import goldenAppleSymbol from '../../../assets/slots/symbols/golden-apple.gif'
import grapeSymbol from '../../../assets/slots/symbols/grape-bunch.gif'
import lemonSymbol from '../../../assets/slots/symbols/lemon.gif'
import mangoSymbol from '../../../assets/slots/symbols/mango.gif'
import orangeSymbol from '../../../assets/slots/symbols/orange.gif'
import watermelonSymbol from '../../../assets/slots/symbols/watermelon-slice.gif'
import type { SlotSymbolSet } from '../../../features/slots/config/symbolSets'

const staticSymbol = (id: keyof SlotSymbolSet['definitions'], label: string, image: string) => ({
  id,
  label,
  image,
  animatedImage: image,
})

const fruitToken = (
  id: 'RAND_05' | 'RAND_1' | 'RAND_15' | 'RAND_2' | 'RAND_3' | 'RAND_4' | 'RAND_5',
  wagerMultiplier: number,
) => ({
  id,
  label: `${wagerMultiplier}× wager rainbow fruit token`,
  image: rainbowFruitToken,
  animatedImage: rainbowFruitToken,
  wagerMultiplier,
})

export const RAINBOW_REALM_SYMBOLS: SlotSymbolSet = {
  id: 'rainbow-realm-fruits-v2',
  serverSymbolSetId: 'rainbow-realm-fruits-v1-symbols',
  definitions: {
    '2': staticSymbol('2', 'Cherry', cherrySymbol),
    '3': staticSymbol('3', 'Orange', orangeSymbol),
    '4': staticSymbol('4', 'Lemon', lemonSymbol),
    '5': staticSymbol('5', 'Grape bunch', grapeSymbol),
    '6': staticSymbol('6', 'Mango', mangoSymbol),
    '7': staticSymbol('7', 'Watermelon slice', watermelonSymbol),
    ACE: staticSymbol('ACE', 'Golden apple wild', goldenAppleSymbol),
    FREE: staticSymbol('FREE', 'Rainbow orchard free game', rainbowOrchardGate),
    POWER: staticSymbol('POWER', 'Crowned rainbow apple', rainbowAppleMedallion),
    BOLT: staticSymbol('BOLT', 'Bottled sunshine', bottledSunshine),
    BANANA: staticSymbol('BANANA', 'Rainbow banana bunch', rainbowBananaBunch),
    PAW: staticSymbol('PAW', 'Wicker fruit basket harvest', wickerFruitBasket),
    RAND_05: fruitToken('RAND_05', 0.5),
    RAND_1: fruitToken('RAND_1', 1),
    RAND_15: fruitToken('RAND_15', 1.5),
    RAND_2: fruitToken('RAND_2', 2),
    RAND_3: fruitToken('RAND_3', 3),
    RAND_4: fruitToken('RAND_4', 4),
    RAND_5: fruitToken('RAND_5', 5),
    SEAL_SYNC: staticSymbol('SEAL_SYNC', 'Strawberry sync charm', strawberrySyncCharm),
    SEAL_ROWS: staticSymbol('SEAL_ROWS', 'Blueberry bounty charm', blueberryRowsCharm),
    SEAL_PAW: staticSymbol('SEAL_PAW', 'Orange basket charm', orangeBasketCharm),
    SEAL_RAND: staticSymbol('SEAL_RAND', 'Kiwi token-column charm', kiwiColumnCharm),
  },
  guideEntries: [
    { symbol: '2', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '4×' },
    { symbol: '3', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '2×' },
    { symbol: '4', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '7×' },
    { symbol: '5', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '6×' },
    { symbol: '6', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '8×' },
    { symbol: '7', firstLabel: '3–4', firstValue: '3×', secondLabel: '5', secondValue: '11×' },
    { symbol: 'ACE', firstLabel: '3–4', firstValue: '5×', secondLabel: '5', secondValue: '18×' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '5 free games' },
    { symbol: 'POWER', firstLabel: '3–4', firstValue: '2× +1 point', secondLabel: '5', secondValue: '4× +2 points' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 sunshine' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'gathers fruit tokens', secondLabel: '2 baskets', secondValue: 'double' },
    { symbol: 'SEAL_SYNC', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 strawberry spins' },
    { symbol: 'SEAL_ROWS', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 blueberry spins' },
    { symbol: 'SEAL_PAW', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 basket spins' },
    { symbol: 'SEAL_RAND', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 kiwi spins' },
  ],
}
