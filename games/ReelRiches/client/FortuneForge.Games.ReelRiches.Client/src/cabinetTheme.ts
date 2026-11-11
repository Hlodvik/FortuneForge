import dawnLake from './assets/art/optimized/reel-riches-lake-v2.webp'
import pearlJewel from './assets/art/optimized/reel-riches-pearl-token-v2.webp'
import reelRichesEmblem from './assets/art/optimized/reel-riches-cabinet-emblem-v2.webp'
import reelRichesWordmark from './assets/art/optimized/reel-riches-wordmark-v1.webp'
import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'

export const REEL_RICHES_CABINET_THEME: SlotCabinetTheme = {
  id: 'reel-riches-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Reel Riches fishing slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Reel Riches',
  titleArtwork: { src: reelRichesWordmark, width: 640, height: 164 },
  paylineTrace: { color: '#43e0d5', glowColor: '#fff1a6' },
  subtitle: 'Land the Legendary Catch',
  celebrationEffect: 'reel-ripple',
  emblemImage: reelRichesEmblem,
  accentImage: pearlJewel,
  backdropImage: dawnLake,
  visualsBackdropImage: dawnLake,
  pageBackdropImage: dawnLake,
  palette: {
    shellTop: '#075a73',
    shellBottom: '#04283d',
    panel: '#07344a',
    trim: '#e2a93e',
    trimBright: '#fff1a6',
    accent: '#43e0d5',
    glow: '#ff8161',
    text: '#f5fff7',
  },
}
