import freeGameSymbol from '../../../assets/slots/symbols/free-game.png'
import freeGameAnimatedSymbol from '../../../assets/slots/symbols/free-game-animated.gif'
import celestialLightningBoltSymbol from '../../../assets/slots/symbols/celestial-lightning-bolt.png'
import cherrySymbol from '../../../assets/slots/symbols/cherry.gif'
import goldenAppleSymbol from '../../../assets/slots/symbols/golden-apple.gif'
import grapeSymbol from '../../../assets/slots/symbols/grape-bunch.gif'
import lemonSymbol from '../../../assets/slots/symbols/lemon.gif'
import mangoSymbol from '../../../assets/slots/symbols/mango.gif'
import orangeSymbol from '../../../assets/slots/symbols/orange.gif'
import rainbowPowerCoinSymbol from '../../../assets/slots/symbols/rainbow-realm-power-coin.png'
import watermelonSymbol from '../../../assets/slots/symbols/watermelon-slice.gif'
import celestialGourdSymbol from '../../../assets/slots/symbols/wukong/celestial-gourd.png'
import celestialGourdAnimatedSymbol from '../../../assets/slots/symbols/wukong/celestial-gourd-animated.gif'
import celestialHammerSymbol from '../../../assets/slots/symbols/wukong/celestial-hammer.png'
import celestialStaffSymbol from '../../../assets/slots/symbols/wukong/celestial-staff.png'
import celestialStaffAnimatedSymbol from '../../../assets/slots/symbols/wukong/celestial-staff-animated.gif'
import goldenCircletSymbol from '../../../assets/slots/symbols/wukong/golden-circlet.png'
import goldenCircletAnimatedSymbol from '../../../assets/slots/symbols/wukong/golden-circlet-animated.gif'
import immortalityPeachSymbol from '../../../assets/slots/symbols/wukong/immortality-peach.png'
import immortalityPeachAnimatedSymbol from '../../../assets/slots/symbols/wukong/immortality-peach-animated.gif'
import jadeDragonPearlSymbol from '../../../assets/slots/symbols/wukong/jade-dragon-pearl.png'
import jadeDragonPearlAnimatedSymbol from '../../../assets/slots/symbols/wukong/jade-dragon-pearl-animated.gif'
import nimbusCloudSymbol from '../../../assets/slots/symbols/wukong/nimbus-cloud.png'
import nimbusCloudAnimatedSymbol from '../../../assets/slots/symbols/wukong/nimbus-cloud-animated.gif'
import celestialBananaBunchSymbol from '../../../assets/slots/symbols/wukong/celestial-banana-bunch.png'
import randValueTokenSymbol from '../../../assets/slots/symbols/wukong/rand-value-token.png'
import wukongMonkeyPawSymbol from '../../../assets/slots/symbols/wukong/wukong-monkey-paw.png'
import wukongMedallionSymbol from '../../../assets/slots/symbols/wukong/wukong-medallion.png'
import wukongMedallionAnimatedSymbol from '../../../assets/slots/symbols/wukong/wukong-medallion-animated.gif'
import wukongPowerSealSymbol from '../../../assets/slots/symbols/wukong/wukong-power-seal.png'
import wukongPowerSealBlueSymbol from '../../../assets/slots/symbols/wukong/wukong-power-seal-blue.png'
import wukongPowerSealJadeSymbol from '../../../assets/slots/symbols/wukong/wukong-power-seal-jade.png'
import wukongPowerSealOrangeSymbol from '../../../assets/slots/symbols/wukong/wukong-power-seal-orange.png'
import type { SlotSymbolId } from '../types/slots'

export type SlotSymbolDefinition = {
  id: SlotSymbolId
  label: string
  image: string
  animatedImage?: string
  valueLabel?: string
  wagerMultiplier?: number
}

export type SlotSymbolGuideEntry = {
  symbol: SlotSymbolId
  firstLabel: string
  firstValue: string
  secondLabel?: string
  secondValue?: string
}

export type SlotSymbolSet = {
  id: string
  serverSymbolSetId?: string
  definitions: Readonly<Partial<Record<SlotSymbolId, SlotSymbolDefinition>>>
  guideEntries: readonly SlotSymbolGuideEntry[]
}

export function getSlotSymbolDefinition(
  symbolSet: SlotSymbolSet,
  symbol: SlotSymbolId,
): SlotSymbolDefinition {
  const definition = symbolSet.definitions[symbol]
  if (!definition) {
    throw new Error(`Slot symbol '${symbol}' is not defined by '${symbolSet.id}'.`)
  }
  return definition
}

export const WUKONG_FEATURE_SYMBOL_IDS = [
  'BANANA',
  'PAW',
  'RAND_05',
  'RAND_1',
  'RAND_15',
  'RAND_2',
  'RAND_3',
  'RAND_4',
  'RAND_5',
  'SEAL_SYNC',
  'SEAL_ROWS',
  'SEAL_PAW',
  'SEAL_RAND',
] as const satisfies readonly SlotSymbolId[]

const WUKONG_FEATURE_SYMBOL_DEFINITIONS: Readonly<Record<
  Exclude<SlotSymbolId, '2' | '3' | '4' | '5' | '6' | '7' | 'ACE' | 'FREE' | 'POWER' | 'BOLT'>,
  SlotSymbolDefinition
>> = {
  BANANA: { id: 'BANANA', label: 'Celestial banana bunch', image: celestialBananaBunchSymbol, animatedImage: celestialBananaBunchSymbol },
  PAW: { id: 'PAW', label: 'Wukong grab', image: wukongMonkeyPawSymbol, animatedImage: wukongMonkeyPawSymbol },
  RAND_05: { id: 'RAND_05', label: 'Rand 0.5× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 0.5 },
  RAND_1: { id: 'RAND_1', label: 'Rand 1× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 1 },
  RAND_15: { id: 'RAND_15', label: 'Rand 1.5× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 1.5 },
  RAND_2: { id: 'RAND_2', label: 'Rand 2× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 2 },
  RAND_3: { id: 'RAND_3', label: 'Rand 3× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 3 },
  RAND_4: { id: 'RAND_4', label: 'Rand 4× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 4 },
  RAND_5: { id: 'RAND_5', label: 'Rand 5× wager value', image: randValueTokenSymbol, animatedImage: randValueTokenSymbol, wagerMultiplier: 5 },
  SEAL_SYNC: { id: 'SEAL_SYNC', label: 'Sync power seal', image: wukongPowerSealSymbol, animatedImage: wukongPowerSealSymbol },
  SEAL_ROWS: { id: 'SEAL_ROWS', label: 'Rows power seal', image: wukongPowerSealBlueSymbol, animatedImage: wukongPowerSealBlueSymbol },
  SEAL_PAW: { id: 'SEAL_PAW', label: 'Paw power seal', image: wukongPowerSealOrangeSymbol, animatedImage: wukongPowerSealOrangeSymbol },
  SEAL_RAND: { id: 'SEAL_RAND', label: 'Rand power seal', image: wukongPowerSealJadeSymbol, animatedImage: wukongPowerSealJadeSymbol },
}

// This is the current production symbol collection. A future theme can supply
// another SlotSymbolSet without changing the reel or guide components.
export const WUKONG_SYMBOLS: SlotSymbolSet = {
  id: 'wukong-treasures-v3',
  definitions: {
    '2': { id: '2', label: 'Nimbus cloud', image: nimbusCloudSymbol, animatedImage: nimbusCloudAnimatedSymbol },
    '3': { id: '3', label: 'Immortality peach', image: immortalityPeachSymbol, animatedImage: immortalityPeachAnimatedSymbol },
    '4': { id: '4', label: 'Celestial gourd', image: celestialGourdSymbol, animatedImage: celestialGourdAnimatedSymbol },
    '5': { id: '5', label: 'Jade dragon pearl', image: jadeDragonPearlSymbol, animatedImage: jadeDragonPearlAnimatedSymbol },
    '6': { id: '6', label: 'Golden circlet', image: goldenCircletSymbol, animatedImage: goldenCircletAnimatedSymbol },
    '7': { id: '7', label: 'Celestial staff', image: celestialStaffSymbol, animatedImage: celestialStaffAnimatedSymbol },
    ACE: { id: 'ACE', label: 'Wukong medallion', image: wukongMedallionSymbol, animatedImage: wukongMedallionAnimatedSymbol },
    FREE: { id: 'FREE', label: 'Free game', image: freeGameSymbol, animatedImage: freeGameAnimatedSymbol },
    POWER: { id: 'POWER', label: 'Celestial power hammer', image: celestialHammerSymbol, animatedImage: celestialHammerSymbol },
    BOLT: { id: 'BOLT', label: 'Energy bolt', image: celestialLightningBoltSymbol, animatedImage: celestialLightningBoltSymbol },
    ...WUKONG_FEATURE_SYMBOL_DEFINITIONS,
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
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 energy' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'grabs R coins', secondLabel: '2 paws', secondValue: 'double' },
    { symbol: 'SEAL_SYNC', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 sync spins' },
    { symbol: 'SEAL_ROWS', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 +2 row spins' },
    { symbol: 'SEAL_PAW', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 paw spins' },
    { symbol: 'SEAL_RAND', firstLabel: 'Any', firstValue: 'collect 40', secondLabel: 'Award', secondValue: '10 rand spins' },
  ],
}

export const RAINBOW_REALM_SYMBOLS: SlotSymbolSet = {
  id: 'rainbow-realm-fruits-v1',
  serverSymbolSetId: WUKONG_SYMBOLS.id,
  definitions: {
    '2': { id: '2', label: 'Cherry', image: cherrySymbol, animatedImage: cherrySymbol },
    '3': { id: '3', label: 'Orange', image: orangeSymbol, animatedImage: orangeSymbol },
    '4': { id: '4', label: 'Lemon', image: lemonSymbol, animatedImage: lemonSymbol },
    '5': { id: '5', label: 'Grape bunch', image: grapeSymbol, animatedImage: grapeSymbol },
    '6': { id: '6', label: 'Mango', image: mangoSymbol, animatedImage: mangoSymbol },
    '7': { id: '7', label: 'Watermelon slice', image: watermelonSymbol, animatedImage: watermelonSymbol },
    ACE: { id: 'ACE', label: 'Golden apple', image: goldenAppleSymbol, animatedImage: goldenAppleSymbol },
    FREE: { id: 'FREE', label: 'Free game', image: freeGameSymbol, animatedImage: freeGameAnimatedSymbol },
    POWER: { id: 'POWER', label: 'Rainbow power coin', image: rainbowPowerCoinSymbol, animatedImage: rainbowPowerCoinSymbol },
    BOLT: { id: 'BOLT', label: 'Energy bolt', image: celestialLightningBoltSymbol, animatedImage: celestialLightningBoltSymbol },
  },
  guideEntries: [
    { symbol: '2', firstLabel: '3–4', firstValue: '1x', secondLabel: '5', secondValue: '4-6x' },
    { symbol: '3', firstLabel: '3–4', firstValue: '1x', secondLabel: '5', secondValue: '2-4x' },
    { symbol: '4', firstLabel: '3–4', firstValue: '1x', secondLabel: '5', secondValue: '7-9x' },
    { symbol: '5', firstLabel: '3–4', firstValue: '2x', secondLabel: '5', secondValue: '6-8x' },
    { symbol: '6', firstLabel: '3–4', firstValue: '2x', secondLabel: '5', secondValue: '8-10x' },
    { symbol: '7', firstLabel: '3–4', firstValue: '3x', secondLabel: '5', secondValue: '11-13x' },
    { symbol: 'ACE', firstLabel: '3–4', firstValue: '5x', secondLabel: '5', secondValue: '18-20x' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '5 free games' },
    { symbol: 'POWER', firstLabel: '3–4', firstValue: '2x +1 point', secondLabel: '5', secondValue: '4-6x +2 points' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 energy' },
  ],
}
