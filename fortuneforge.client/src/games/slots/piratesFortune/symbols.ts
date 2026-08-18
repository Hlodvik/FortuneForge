import amberGem from '../../../assets/slots/games/pirates-fortune/amber-gem.png'
import anchor from '../../../assets/slots/games/pirates-fortune/anchor.png'
import captainCrest from '../../../assets/slots/games/pirates-fortune/captain-crest.png'
import compass from '../../../assets/slots/games/pirates-fortune/compass.png'
import deckCannon from '../../../assets/slots/games/pirates-fortune/deck-cannon.png'
import emeraldGem from '../../../assets/slots/games/pirates-fortune/emerald-gem.png'
import goldDoubloon from '../../../assets/slots/games/pirates-fortune/gold-doubloon.png'
import messageBottle from '../../../assets/slots/games/pirates-fortune/message-bottle.png'
import pirateShip from '../../../assets/slots/games/pirates-fortune/pirate-ship.png'
import powderKegs from '../../../assets/slots/games/pirates-fortune/powder-kegs.png'
import rubyGem from '../../../assets/slots/games/pirates-fortune/ruby-gem.png'
import rumBottle from '../../../assets/slots/games/pirates-fortune/rum-bottle.png'
import sapphireGem from '../../../assets/slots/games/pirates-fortune/sapphire-gem.png'
import shipWheel from '../../../assets/slots/games/pirates-fortune/ship-wheel.png'
import skullCrossbones from '../../../assets/slots/games/pirates-fortune/skull-crossbones.png'
import stormBottle from '../../../assets/slots/games/pirates-fortune/storm-bottle.png'
import treasureMap from '../../../assets/slots/games/pirates-fortune/treasure-map.png'
import type { SlotSymbolSet } from '../../../features/slots/config/symbolSets'

const staticSymbol = (id: keyof SlotSymbolSet['definitions'], label: string, image: string) => ({
  id,
  label,
  image,
  animatedImage: image,
})

const doubloonValueSymbol = (
  id: 'RAND_05' | 'RAND_1' | 'RAND_15' | 'RAND_2' | 'RAND_3' | 'RAND_4' | 'RAND_5',
  wagerMultiplier: number,
) => ({
  id,
  label: `${wagerMultiplier}× wager doubloon`,
  image: goldDoubloon,
  animatedImage: goldDoubloon,
  wagerMultiplier,
})

export const PIRATES_FORTUNE_SYMBOLS: SlotSymbolSet = {
  id: 'pirates-fortune-treasures-v1',
  serverSymbolSetId: 'pirates-fortune-v1-symbols',
  definitions: {
    '2': staticSymbol('2', 'Rum bottle', rumBottle),
    '3': staticSymbol('3', 'Brass compass', compass),
    '4': staticSymbol('4', 'Iron anchor', anchor),
    '5': staticSymbol('5', 'Deck cannon', deckCannon),
    '6': staticSymbol('6', 'Treasure map', treasureMap),
    '7': staticSymbol('7', 'Black-sailed pirate ship', pirateShip),
    ACE: staticSymbol('ACE', 'Captain wild crest', captainCrest),
    FREE: staticSymbol('FREE', 'Message in a bottle free game', messageBottle),
    POWER: staticSymbol('POWER', 'Captain\'s wheel', shipWheel),
    BOLT: staticSymbol('BOLT', 'Bottled storm charge', stormBottle),
    BANANA: staticSymbol('BANANA', 'Powder keg volley', powderKegs),
    PAW: staticSymbol('PAW', 'Skull-and-crossbones plunder', skullCrossbones),
    RAND_05: doubloonValueSymbol('RAND_05', 0.5),
    RAND_1: doubloonValueSymbol('RAND_1', 1),
    RAND_15: doubloonValueSymbol('RAND_15', 1.5),
    RAND_2: doubloonValueSymbol('RAND_2', 2),
    RAND_3: doubloonValueSymbol('RAND_3', 3),
    RAND_4: doubloonValueSymbol('RAND_4', 4),
    RAND_5: doubloonValueSymbol('RAND_5', 5),
    SEAL_SYNC: staticSymbol('SEAL_SYNC', 'Crimson broadside gem', rubyGem),
    SEAL_ROWS: staticSymbol('SEAL_ROWS', 'Sapphire high-tide gem', sapphireGem),
    SEAL_PAW: staticSymbol('SEAL_PAW', 'Amber skull-storm gem', amberGem),
    SEAL_RAND: staticSymbol('SEAL_RAND', 'Emerald doubloon gem', emeraldGem),
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
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 storm charge' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'grabs doubloons', secondLabel: '2 skulls', secondValue: 'double' },
    { symbol: 'SEAL_SYNC', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 broadside spins' },
    { symbol: 'SEAL_ROWS', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 high-tide spins' },
    { symbol: 'SEAL_PAW', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 skull-storm spins' },
    { symbol: 'SEAL_RAND', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 doubloon spins' },
  ],
}
