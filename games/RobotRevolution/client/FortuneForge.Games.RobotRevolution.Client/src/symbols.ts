import type { SlotSymbolDefinition, SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { createClassicPaylineGuide, formatCalibratedMultiplier, getSlotEngineGuideContract } from '@fortuneforge/slot-game-runtime/engine-guide'
import { FIFTH_SUN_ECLIPSE_MATH as eclipseMath } from './eclipseMath.generated'
import flame from './assets/art/fifth-sun-glyphs/jade-flame-approved-v1.webp'
import rain from './assets/art/fifth-sun-glyphs/jade-rain-approved-v1.webp'
import wind from './assets/art/fifth-sun-glyphs/jade-wind-approved-v1.webp'
import earth from './assets/art/fifth-sun-glyphs/jade-earth-approved-v1.webp'
import reed from './assets/art/fifth-sun-glyphs/jade-reed-approved-v1.webp'
import wild from './assets/art/sunstone-eclipse/sunstone-sunray-glyph-v1.webp'
import feather from './assets/art/sunstone-eclipse/fifth-sun-quetzal-headdress-approved-v1.webp'
import serpent from './assets/art/sunstone-eclipse/fifth-sun-feathered-serpent-approved-v1.webp'
import eagle from './assets/art/sunstone-eclipse/fifth-sun-eagle-guardian-approved-v1.webp'
import jaguar from './assets/art/sunstone-eclipse/fifth-sun-jaguar-mask-approved-v1.webp'
import eclipse from './assets/art/sunstone-eclipse/sunstone-eclipse-scatter-v1.webp'
import lunarEclipse from './assets/art/sunstone-eclipse/fifth-sun-lunar-eclipse-approved-v1.webp'
import calendar from './assets/art/sunstone-eclipse/sunstone-wild-calendar-v1.webp'
import temple from './assets/art/sunstone-eclipse/sunstone-eclipse-temple-background-v1.webp'
import reelbed from './assets/art/sunstone-eclipse/fifth-sun-obsidian-reelbed-v2.webp'

export const FIFTH_SUN_PAYING_SYMBOLS = [
  'GLYPH_FLAME', 'GLYPH_RAIN', 'GLYPH_WIND', 'GLYPH_EARTH', 'GLYPH_REED',
  'FEATHER', 'SERPENT', 'EAGLE', 'JAGUAR', 'SUNSTONE', 'WILD',
] as const

export const FIFTH_SUN_SYMBOL_IMAGES = {
  GLYPH_FLAME: flame, GLYPH_RAIN: rain, GLYPH_WIND: wind, GLYPH_EARTH: earth, GLYPH_REED: reed,
  FEATHER: feather, SERPENT: serpent, EAGLE: eagle, JAGUAR: jaguar,
  SUNSTONE: calendar, WILD: wild, SOLAR_ECLIPSE: eclipse, LUNAR_ECLIPSE: lunarEclipse, ECLIPSE: eclipse,
  CABINET_EMBLEM: calendar, BACKDROP: temple, REELBED: reelbed,
  // Rendering aliases only: historical owed frames retain their wire identities.
  '2': flame, '3': feather, '4': rain, '5': serpent, '6': eagle, '7': jaguar,
  ACE: wild, POWER: calendar, FREE: eclipse, BANANA: reed, BOLT: flame, PAW: wild,
  SEAL_SYNC: jaguar, SEAL_ROWS: eagle, SEAL_PAW: serpent, SEAL_RAND: calendar, VALUE: rain,
} as const

const labels: Readonly<Record<string, string>> = {
  GLYPH_FLAME: 'Flame glyph', GLYPH_RAIN: 'Rain glyph', GLYPH_WIND: 'Wind glyph',
  GLYPH_EARTH: 'Earth glyph', GLYPH_REED: 'Reed glyph', FEATHER: 'Quetzal headdress',
  SERPENT: 'Feathered Serpent', EAGLE: 'Eagle guardian', JAGUAR: 'Jaguar mask',
  SUNSTONE: 'Sunstone', WILD: 'Golden Sun Wild', ECLIPSE: 'Eclipse Scatter',
  SOLAR_ECLIPSE: 'Solar Eclipse', LUNAR_ECLIPSE: 'Lunar Eclipse',
  '2': 'Saved offering', '3': 'Quetzal headdress', '4': 'Saved turquoise glyph',
  '5': 'Feathered Serpent', '6': 'Eagle guardian', '7': 'Jaguar mask',
  ACE: 'Saved Wild', POWER: 'Saved Sunstone', FREE: 'Saved Eclipse Scatter',
  BANANA: 'Saved triple-glyph pattern', BOLT: 'Saved sunlight', PAW: 'Saved temple seal',
  SEAL_SYNC: 'Saved Jaguar seal', SEAL_ROWS: 'Saved Eagle seal', SEAL_PAW: 'Saved Serpent seal',
  SEAL_RAND: 'Saved Sunstone seal', VALUE: 'Saved tribute',
}
const definitions = Object.fromEntries(Object.entries(labels).map(([id, label]) => [id, {
  id, label, image: FIFTH_SUN_SYMBOL_IMAGES[id as keyof typeof FIFTH_SUN_SYMBOL_IMAGES],
  ...(id === 'ECLIPSE' ? { artworkVariants: [
    { image: eclipse, label: 'Solar Eclipse Scatter' },
    { image: lunarEclipse, label: 'Lunar Eclipse Scatter' },
  ] } : {}),
}])) satisfies Record<string, SlotSymbolDefinition>
const nativePaylinePayouts = getSlotEngineGuideContract('robot-revolution-v1').paylinePayouts!

export const FIFTH_SUN_SYMBOL_SET: SlotSymbolSet = {
  id: 'fifth-sun-symbols-v2', serverSymbolSetId: 'fifth-sun-symbols-v2', definitions,
  compatibleServerSymbolSetIds: ['fifth-sun-symbols-v1', 'classic-symbols-v3'],
  animationSymbolIds: FIFTH_SUN_PAYING_SYMBOLS,
  guideEntries: [
    ...createClassicPaylineGuide('robot-revolution-v1', FIFTH_SUN_PAYING_SYMBOLS).map(entry => ({
      ...entry, firstLabel: entry.symbol === 'WILD' ? '3' : entry.firstLabel,
      details: [
        { label: `Eclipse · ${entry.symbol === 'WILD' ? '3' : '3–4'} matches`, text: formatCalibratedMultiplier(nativePaylinePayouts[entry.symbol].three, eclipseMath.eclipsePayoutMultiplier) },
        { label: 'Eclipse · 5 matches', text: formatCalibratedMultiplier(nativePaylinePayouts[entry.symbol].five, eclipseMath.eclipsePayoutMultiplier) },
      ],
    })),
    { symbol: 'SOLAR_ECLIPSE', companionSymbol: 'LUNAR_ECLIPSE', firstLabel: '1 on reel 3', firstValue: `${eclipseMath.eclipseRounds} paid Eclipse rounds`, secondLabel: 'Each round', secondValue: `${eclipseMath.alignmentsPerRound} Sunwheel alignments` },
  ],
  guideSymbolGroups: [FIFTH_SUN_PAYING_SYMBOLS.slice(0, 5), FIFTH_SUN_PAYING_SYMBOLS.slice(5, 10)],
  guideSubtitle: 'Temple treasures',
  guideNote: `Values shown are ordinary-play line awards; open a symbol for its Eclipse line values. One Solar or Lunar Eclipse on the centre reel activates ${eclipseMath.eclipseRounds} paid rounds at the saved wager. Each round includes ${eclipseMath.alignmentsPerRound} wheel alignments, with guardian awards ×${eclipseMath.wheelAwardScale} before linked multipliers. All three awakenings reveal Totality, doubled by a Sunstone at the heart. Wilds substitute in three- and five-symbol wins. Line wins pay once per board. Awards settle in wager points worth 25 cents. Saved older free bonuses retain their original terms.`,
}

export const SUNSTONE_ECLIPSE_SYMBOL_IMAGES = FIFTH_SUN_SYMBOL_IMAGES
export const ROBOT_REVOLUTION_SYMBOL_IMAGES = FIFTH_SUN_SYMBOL_IMAGES
