import type { SlotSymbolSet } from '../../../features/slots/config/symbolSets'

const symbolImages = import.meta.glob('../../../assets/slots/games/rainbow-realm/responsive/*.webp', {
  eager: true,
  query: '?url',
  import: 'default',
}) as Record<string, string>

type SymbolSize = 96 | 192 | 256 | 320

// These seven fruits are animated GIF masters at 256 px. A 320 px export
// would only upscale every frame and use more bandwidth without adding detail.
const animatedFruitStems = new Set([
  'cherry', 'orange', 'lemon', 'grape-bunch', 'mango', 'watermelon-slice', 'golden-apple',
])

function symbolImage(stem: string, size: SymbolSize): string {
  const path = `../../../assets/slots/games/rainbow-realm/responsive/${stem}-${size}.webp`
  const image = symbolImages[path]
  if (!image) throw new Error(`Missing Rainbow Realm symbol image: ${path}`)
  return image
}

function symbolArtwork(stem: string) {
  const baseImages = { 96: symbolImage(stem, 96), 192: symbolImage(stem, 192) }
  const responsiveImages = animatedFruitStems.has(stem)
    ? { ...baseImages, 256: symbolImage(stem, 256) }
    : { ...baseImages, 320: symbolImage(stem, 320) }
  return { image: responsiveImages[192], animatedImage: responsiveImages[192], responsiveImages }
}

const staticSymbol = (id: keyof SlotSymbolSet['definitions'], label: string, stem: string) => ({
  id,
  label,
  ...symbolArtwork(stem),
})

const fruitToken = (
  id: 'RAND_05' | 'RAND_1' | 'RAND_15' | 'RAND_2' | 'RAND_3' | 'RAND_4' | 'RAND_5',
  wagerMultiplier: number,
) => ({
  id,
  label: `${wagerMultiplier}× wager rainbow fruit token`,
  ...symbolArtwork('rainbow-fruit-token'),
  wagerMultiplier,
})

const ranks = [
  { symbol: 'J', label: 'Jack', stem: 'gold-rank-j', five: 2 },
  { symbol: 'Q', label: 'Queen', stem: 'gold-rank-q', five: 3 },
  { symbol: 'K', label: 'King', stem: 'gold-rank-k', five: 4 },
  { symbol: 'A', label: 'Ace', stem: 'gold-rank-a', five: 5 },
] as const

export const RAINBOW_REALM_BASKET_CHARMS = [
  { symbol: 'SEAL_SYNC', bonus: 'strawberry', multiplier: 1.5 },
  { symbol: 'SEAL_ROWS', bonus: 'blueberry', multiplier: 2 },
  { symbol: 'SEAL_PAW', bonus: 'basket', multiplier: 2.5 },
  { symbol: 'SEAL_RAND', bonus: 'kiwi', multiplier: 3 },
] as const

const activeSymbols = {
  ...Object.fromEntries(ranks.map(({ symbol, label, stem }) => [symbol, staticSymbol(symbol, label, stem)])),
  '2': staticSymbol('2', 'Cherry', 'cherry'),
  '3': staticSymbol('3', 'Orange', 'orange'),
  '4': staticSymbol('4', 'Lemon', 'lemon'),
  '5': staticSymbol('5', 'Grape bunch', 'grape-bunch'),
  '6': staticSymbol('6', 'Mango', 'mango'),
  '7': staticSymbol('7', 'Watermelon slice', 'watermelon-slice'),
  ACE: staticSymbol('ACE', 'Golden apple wild', 'golden-apple'),
  FREE: staticSymbol('FREE', 'Orchard Rush rainbow', 'orchard-rush-rainbow-v1'),
  POWER: staticSymbol('POWER', 'Crowned rainbow apple', 'rainbow-apple-medallion'),
  BANANA: staticSymbol('BANANA', 'Rainbow banana bunch', 'rainbow-banana-bunch'),
  PAW: staticSymbol('PAW', 'Wicker fruit basket harvest', 'wicker-fruit-basket'),
  SEAL_SYNC: staticSymbol('SEAL_SYNC', 'Strawberry sync charm', 'strawberry-sync-charm'),
  SEAL_ROWS: staticSymbol('SEAL_ROWS', 'Blueberry bounty charm', 'blueberry-rows-charm'),
  SEAL_PAW: staticSymbol('SEAL_PAW', 'Orange basket charm', 'orange-basket-charm'),
  SEAL_RAND: staticSymbol('SEAL_RAND', 'Kiwi wild-column charm', 'kiwi-column-charm'),
}

export const RAINBOW_REALM_SYMBOLS: SlotSymbolSet = {
  id: 'rainbow-realm-fruits-v2',
  serverSymbolSetId: 'rainbow-realm-symbols-v1',
  definitions: {
    ...activeSymbols,
    // Keep art available for historical results.
    BOLT: staticSymbol('BOLT', 'Bottled sunshine', 'bottled-sunshine'),
    RAND_05: fruitToken('RAND_05', 0.5),
    RAND_1: fruitToken('RAND_1', 1),
    RAND_15: fruitToken('RAND_15', 1.5),
    RAND_2: fruitToken('RAND_2', 2),
    RAND_3: fruitToken('RAND_3', 3),
    RAND_4: fruitToken('RAND_4', 4),
    RAND_5: fruitToken('RAND_5', 5),
  },
  animationSymbolIds: Object.values(activeSymbols).map(({ id }) => id),
  guideEntries: [
    ...ranks.map(({ symbol, five }) => ({ symbol, firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: `${five}×` })),
    { symbol: '2', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '4×' },
    { symbol: '3', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '2×' },
    { symbol: '4', firstLabel: '3–4', firstValue: '1×', secondLabel: '5', secondValue: '7×' },
    { symbol: '5', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '6×' },
    { symbol: '6', firstLabel: '3–4', firstValue: '2×', secondLabel: '5', secondValue: '8×' },
    { symbol: '7', firstLabel: '3–4', firstValue: '3×', secondLabel: '5', secondValue: '11×' },
    { symbol: 'ACE', firstLabel: '3–4', firstValue: '5×', secondLabel: '5', secondValue: '18×' },
    { symbol: 'FREE', firstLabel: '3+', firstValue: 'anywhere', secondLabel: 'Award', secondValue: '10 bonus spins' },
    { symbol: 'POWER', firstLabel: '3–4', firstValue: '2× +1 point', secondLabel: '5', secondValue: '4× +2 points' },
    { symbol: 'BANANA', firstLabel: '3', firstValue: 'row/column/diag', secondLabel: 'Pays', secondValue: '3×' },
    { symbol: 'PAW', firstLabel: 'Any', firstValue: 'harvests visible charms', secondLabel: '2+ baskets', secondValue: 'double' },
    ...RAINBOW_REALM_BASKET_CHARMS.map(({ symbol, bonus, multiplier }) => ({
      symbol,
      firstLabel: 'Basket',
      firstValue: `${multiplier}× wager`,
      secondLabel: 'Collect 60',
      secondValue: `10 ${bonus} bonus spins`,
    })),
  ],
}
