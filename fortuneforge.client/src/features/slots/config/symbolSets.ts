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
import celestialStaffSymbol from '../../../assets/slots/symbols/wukong/celestial-staff.png'
import celestialStaffAnimatedSymbol from '../../../assets/slots/symbols/wukong/celestial-staff-animated.gif'
import goldenCircletSymbol from '../../../assets/slots/symbols/wukong/golden-circlet.png'
import goldenCircletAnimatedSymbol from '../../../assets/slots/symbols/wukong/golden-circlet-animated.gif'
import jadeDragonPearlSymbol from '../../../assets/slots/symbols/wukong/jade-dragon-pearl.png'
import jadeDragonPearlAnimatedSymbol from '../../../assets/slots/symbols/wukong/jade-dragon-pearl-animated.gif'
import nimbusCloudSymbol from '../../../assets/slots/symbols/wukong/nimbus-cloud.png'
import nimbusCloudAnimatedSymbol from '../../../assets/slots/symbols/wukong/nimbus-cloud-animated.gif'
import wukongMedallionSymbol from '../../../assets/slots/symbols/wukong/wukong-medallion.png'
import wukongMedallionAnimatedSymbol from '../../../assets/slots/symbols/wukong/wukong-medallion-animated.gif'
import wukongPowerSealSymbol from '../../../assets/slots/symbols/wukong/wukong-power-seal.png'
import type { SlotSymbolId } from '../types/slots'

export type SlotSymbolDefinition = {
  id: SlotSymbolId
  label: string
  image: string
  animatedImage: string
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
  definitions: Readonly<Record<SlotSymbolId, SlotSymbolDefinition>>
  guideEntries: readonly SlotSymbolGuideEntry[]
}

const svgDataUri = (source: string) =>
  `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(source)}`

const clearerPeachSymbol = svgDataUri(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs>
    <radialGradient id="skin" cx="36%" cy="28%" r="72%">
      <stop offset="0" stop-color="#fff3c5"/>
      <stop offset="0.34" stop-color="#ffac7a"/>
      <stop offset="0.72" stop-color="#ff6d7f"/>
      <stop offset="1" stop-color="#c73263"/>
    </radialGradient>
    <linearGradient id="leaf" x1="42" x2="148" y1="30" y2="92">
      <stop stop-color="#d7ff7a"/>
      <stop offset="1" stop-color="#3aaa42"/>
    </linearGradient>
  </defs>
  <path d="M139 44c28-28 64-23 86 4-34 2-57 15-79 42-10-11-13-29-7-46Z" fill="url(#leaf)" stroke="#145c2a" stroke-width="8" stroke-linejoin="round"/>
  <path d="M136 80c8-42-19-63-43-65 24 22 25 47 18 72" fill="none" stroke="#774313" stroke-width="10" stroke-linecap="round"/>
  <path d="M128 230c-44 0-90-39-88-95 1-39 25-79 62-82 16-1 24 5 32 16 8-11 19-17 35-16 37 3 60 43 59 82-2 56-47 95-100 95Z" fill="url(#skin)" stroke="#6e2138" stroke-width="8" stroke-linejoin="round"/>
  <path d="M132 72c15 34 10 96-18 137" fill="none" stroke="#fff3d0" stroke-width="8" stroke-linecap="round" opacity=".62"/>
  <path d="M70 128c8-31 29-52 55-54" fill="none" stroke="#fff7df" stroke-width="10" stroke-linecap="round" opacity=".42"/>
</svg>`)

const bananaSymbol = svgDataUri(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="banana" x1="48" x2="216" y1="66" y2="198">
      <stop stop-color="#fff6a2"/>
      <stop offset=".38" stop-color="#ffd942"/>
      <stop offset="1" stop-color="#e88a16"/>
    </linearGradient>
  </defs>
  <path d="M41 68c33 72 91 112 169 96-41 54-140 40-178-59-7-19-3-31 9-37Z" fill="url(#banana)" stroke="#6b3b05" stroke-width="10" stroke-linejoin="round"/>
  <path d="M47 70c10-8 20-10 31-7" fill="none" stroke="#35200a" stroke-width="14" stroke-linecap="round"/>
  <path d="M207 163c8-2 16 1 22 8" fill="none" stroke="#35200a" stroke-width="12" stroke-linecap="round"/>
  <path d="M63 92c38 54 83 82 139 77" fill="none" stroke="#fff8be" stroke-width="9" stroke-linecap="round" opacity=".72"/>
</svg>`)

const monkeyPawSymbol = svgDataUri(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs>
    <radialGradient id="paw" cx="38%" cy="25%" r="76%">
      <stop stop-color="#ffe2b0"/>
      <stop offset=".48" stop-color="#be6633"/>
      <stop offset="1" stop-color="#552414"/>
    </radialGradient>
  </defs>
  <circle cx="68" cy="82" r="27" fill="url(#paw)" stroke="#2a110b" stroke-width="8"/>
  <circle cx="116" cy="54" r="29" fill="url(#paw)" stroke="#2a110b" stroke-width="8"/>
  <circle cx="166" cy="70" r="27" fill="url(#paw)" stroke="#2a110b" stroke-width="8"/>
  <circle cx="198" cy="116" r="25" fill="url(#paw)" stroke="#2a110b" stroke-width="8"/>
  <path d="M69 161c-7-52 29-83 66-78 42 6 74 46 67 88-5 33-31 58-66 60-37 2-63-24-67-70Z" fill="url(#paw)" stroke="#2a110b" stroke-width="9" stroke-linejoin="round"/>
  <path d="M96 164c22 16 52 17 76-2" fill="none" stroke="#ffd68f" stroke-width="8" stroke-linecap="round" opacity=".7"/>
</svg>`)

const randSymbol = (label: string) => svgDataUri(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="coin" x1="42" x2="214" y1="36" y2="220">
      <stop stop-color="#fff7b3"/>
      <stop offset=".42" stop-color="#ffd547"/>
      <stop offset="1" stop-color="#a95c08"/>
    </linearGradient>
  </defs>
  <circle cx="128" cy="128" r="104" fill="url(#coin)" stroke="#5c2f03" stroke-width="10"/>
  <circle cx="128" cy="128" r="78" fill="#2f0610" stroke="#fff1a2" stroke-width="6"/>
  <text x="128" y="112" text-anchor="middle" font-family="Arial Black, Impact, sans-serif" font-size="56" fill="#fff7bf">R</text>
  <text x="128" y="166" text-anchor="middle" font-family="Arial Black, Impact, sans-serif" font-size="48" fill="#fff">${label}</text>
</svg>`)

const sealSymbol = (glyph: string, label: string, color: string) => svgDataUri(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="rim" x1="34" x2="220" y1="30" y2="228">
      <stop stop-color="#fff8bd"/>
      <stop offset=".5" stop-color="#ffc941"/>
      <stop offset="1" stop-color="#7a3903"/>
    </linearGradient>
  </defs>
  <circle cx="128" cy="128" r="108" fill="url(#rim)" stroke="#3c1800" stroke-width="9"/>
  <circle cx="128" cy="128" r="82" fill="${color}" stroke="#fff4ac" stroke-width="7"/>
  <text x="128" y="128" text-anchor="middle" dominant-baseline="middle" font-family="Arial Black, Impact, sans-serif" font-size="70" fill="#fff" stroke="#22030a" stroke-width="5" paint-order="stroke">${glyph}</text>
  <text x="128" y="201" text-anchor="middle" font-family="Arial Black, Impact, sans-serif" font-size="25" fill="#fff8c7" stroke="#22030a" stroke-width="3" paint-order="stroke">${label}</text>
</svg>`)

const rand05Symbol = randSymbol('0.5×')
const rand1Symbol = randSymbol('1×')
const rand15Symbol = randSymbol('1.5×')
const rand2Symbol = randSymbol('2×')
const rand3Symbol = randSymbol('3×')
const rand4Symbol = randSymbol('4×')
const rand5Symbol = randSymbol('5×')
const syncSealSymbol = sealSymbol('↔', 'SYNC', '#7d1fd1')
const rowsSealSymbol = sealSymbol('+2', 'ROWS', '#1167d8')
const pawSealSymbol = sealSymbol('🐾', 'PAW', '#a74820')
const randSealSymbol = sealSymbol('R', 'RAND', '#0f8a4c')

const FEATURE_SYMBOL_DEFINITIONS: Readonly<Record<
  Exclude<SlotSymbolId, '2' | '3' | '4' | '5' | '6' | '7' | 'ACE' | 'FREE' | 'POWER' | 'BOLT'>,
  SlotSymbolDefinition
>> = {
  BANANA: { id: 'BANANA', label: 'Banana bunch', image: bananaSymbol, animatedImage: bananaSymbol },
  PAW: { id: 'PAW', label: 'Monkey paw', image: monkeyPawSymbol, animatedImage: monkeyPawSymbol },
  RAND_05: { id: 'RAND_05', label: 'Rand 0.5×', image: rand05Symbol, animatedImage: rand05Symbol },
  RAND_1: { id: 'RAND_1', label: 'Rand 1×', image: rand1Symbol, animatedImage: rand1Symbol },
  RAND_15: { id: 'RAND_15', label: 'Rand 1.5×', image: rand15Symbol, animatedImage: rand15Symbol },
  RAND_2: { id: 'RAND_2', label: 'Rand 2×', image: rand2Symbol, animatedImage: rand2Symbol },
  RAND_3: { id: 'RAND_3', label: 'Rand 3×', image: rand3Symbol, animatedImage: rand3Symbol },
  RAND_4: { id: 'RAND_4', label: 'Rand 4×', image: rand4Symbol, animatedImage: rand4Symbol },
  RAND_5: { id: 'RAND_5', label: 'Rand 5×', image: rand5Symbol, animatedImage: rand5Symbol },
  SEAL_SYNC: { id: 'SEAL_SYNC', label: 'Sync seal', image: syncSealSymbol, animatedImage: syncSealSymbol },
  SEAL_ROWS: { id: 'SEAL_ROWS', label: 'Rows seal', image: rowsSealSymbol, animatedImage: rowsSealSymbol },
  SEAL_PAW: { id: 'SEAL_PAW', label: 'Paw seal', image: pawSealSymbol, animatedImage: pawSealSymbol },
  SEAL_RAND: { id: 'SEAL_RAND', label: 'Rand seal', image: randSealSymbol, animatedImage: randSealSymbol },
}

// This is the current production symbol collection. A future theme can supply
// another SlotSymbolSet without changing the reel or guide components.
export const WUKONG_SYMBOLS: SlotSymbolSet = {
  id: 'wukong-treasures-v3',
  definitions: {
    '2': { id: '2', label: 'Nimbus cloud', image: nimbusCloudSymbol, animatedImage: nimbusCloudAnimatedSymbol },
    '3': { id: '3', label: 'Immortality peach', image: clearerPeachSymbol, animatedImage: clearerPeachSymbol },
    '4': { id: '4', label: 'Celestial gourd', image: celestialGourdSymbol, animatedImage: celestialGourdAnimatedSymbol },
    '5': { id: '5', label: 'Jade dragon pearl', image: jadeDragonPearlSymbol, animatedImage: jadeDragonPearlAnimatedSymbol },
    '6': { id: '6', label: 'Golden circlet', image: goldenCircletSymbol, animatedImage: goldenCircletAnimatedSymbol },
    '7': { id: '7', label: 'Celestial staff', image: celestialStaffSymbol, animatedImage: celestialStaffAnimatedSymbol },
    ACE: { id: 'ACE', label: 'Wukong medallion', image: wukongMedallionSymbol, animatedImage: wukongMedallionAnimatedSymbol },
    FREE: { id: 'FREE', label: 'Free game', image: freeGameSymbol, animatedImage: freeGameAnimatedSymbol },
    POWER: { id: 'POWER', label: 'Wukong power seal', image: wukongPowerSealSymbol, animatedImage: wukongPowerSealSymbol },
    BOLT: { id: 'BOLT', label: 'Energy bolt', image: celestialLightningBoltSymbol, animatedImage: celestialLightningBoltSymbol },
    ...FEATURE_SYMBOL_DEFINITIONS,
  },
  guideEntries: [
    { symbol: '2', firstLabel: '3', firstValue: '1×', secondLabel: '5', secondValue: '4–6×' },
    { symbol: '3', firstLabel: '3', firstValue: '1×', secondLabel: '5', secondValue: '2–4×' },
    { symbol: '4', firstLabel: '3', firstValue: '1×', secondLabel: '5', secondValue: '7–9×' },
    { symbol: '5', firstLabel: '3', firstValue: '2×', secondLabel: '5', secondValue: '6–8×' },
    { symbol: '6', firstLabel: '3', firstValue: '2×', secondLabel: '5', secondValue: '8–10×' },
    { symbol: '7', firstLabel: '3', firstValue: '3×', secondLabel: '5', secondValue: '11–13×' },
    { symbol: 'ACE', firstLabel: '3', firstValue: '5×', secondLabel: '5', secondValue: '18–20×' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '5 free games' },
    { symbol: 'POWER', firstLabel: '3', firstValue: '2× +1 point', secondLabel: '5', secondValue: '4–6× +2 points' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 energy' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'grabs R coins', secondLabel: '2 paws', secondValue: 'double' },
    { symbol: 'SEAL_SYNC', firstLabel: 'Any', firstValue: 'collect 44', secondLabel: 'Award', secondValue: '10 sync spins' },
    { symbol: 'SEAL_ROWS', firstLabel: 'Any', firstValue: 'collect 44', secondLabel: 'Award', secondValue: '10 +2 row spins' },
    { symbol: 'SEAL_PAW', firstLabel: 'Any', firstValue: 'collect 44', secondLabel: 'Award', secondValue: '10 paw spins' },
    { symbol: 'SEAL_RAND', firstLabel: 'Any', firstValue: 'collect 44', secondLabel: 'Award', secondValue: '10 rand spins' },
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
    ...FEATURE_SYMBOL_DEFINITIONS,
  },
  guideEntries: [
    { symbol: '2', firstLabel: '3', firstValue: '1x', secondLabel: '5', secondValue: '4-6x' },
    { symbol: '3', firstLabel: '3', firstValue: '1x', secondLabel: '5', secondValue: '2-4x' },
    { symbol: '4', firstLabel: '3', firstValue: '1x', secondLabel: '5', secondValue: '7-9x' },
    { symbol: '5', firstLabel: '3', firstValue: '2x', secondLabel: '5', secondValue: '6-8x' },
    { symbol: '6', firstLabel: '3', firstValue: '2x', secondLabel: '5', secondValue: '8-10x' },
    { symbol: '7', firstLabel: '3', firstValue: '3x', secondLabel: '5', secondValue: '11-13x' },
    { symbol: 'ACE', firstLabel: '3', firstValue: '5x', secondLabel: '5', secondValue: '18-20x' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '5 free games' },
    { symbol: 'POWER', firstLabel: '3', firstValue: '2x +1 point', secondLabel: '5', secondValue: '4-6x +2 points' },
    { symbol: 'BOLT', firstLabel: 'Any', firstValue: 'visible', secondLabel: 'Earn', secondValue: '+1 energy' },
  ],
}
