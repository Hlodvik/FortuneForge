import dawnLake from '../../../assets/slots/games/reel-riches/optimized/reel-riches-lake-v2.webp'
import pearlJewel from '../../../assets/slots/games/reel-riches/optimized/reel-riches-pearl-token-v2.webp'
import reelRichesEmblem from '../../../assets/slots/games/reel-riches/optimized/reel-riches-cabinet-emblem-v2.webp'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const REEL_RICHES_CABINET_THEME: SlotCabinetTheme = {
  id: 'reel-riches-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Reel Riches fishing slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Reel Riches',
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
