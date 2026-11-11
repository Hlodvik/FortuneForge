import highNoonEmblem from './assets/art/optimized/high-noon-cabinet-emblem-v2.webp'
import wordmark from './assets/art/optimized/high-noon-fortune-wordmark-v1.webp'
import turquoiseJewel from './assets/art/optimized/high-noon-gold-nugget-v2.webp'
import westernTown from './assets/art/optimized/high-noon-town-v2.webp'
import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'

export const HIGH_NOON_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'high-noon-fortune-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'High Noon Fortune western slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'High Noon Fortune',
  titleArtwork: { src: wordmark, width: 640, height: 257 },
  paylineTrace: { color: '#fff0ad', glowColor: '#ef861f' },
  subtitle: 'Round Up the Frontier Gold',
  celebrationEffect: 'frontier-dust',
  emblemImage: highNoonEmblem,
  accentImage: turquoiseJewel,
  backdropImage: westernTown,
  visualsBackdropImage: westernTown,
  pageBackdropImage: westernTown,
  palette: {
    shellTop: '#7a301c',
    shellBottom: '#2b1512',
    panel: '#3b2119',
    trim: '#dca13b',
    trimBright: '#ffe4a0',
    accent: '#3ed5c4',
    glow: '#ee6b2f',
    text: '#fff0cf',
  },
}
