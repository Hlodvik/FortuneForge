import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'
import cosmicFortuneWordmark from './assets/art/optimized/cosmic-fortune-wordmark-v1.webp'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'

export const COSMIC_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'cosmic-fortune-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Cosmic Fortune space slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Cosmic Fortune',
  titleArtwork: { src: cosmicFortuneWordmark, width: 640, height: 360 },
  paylineTrace: { color: '#b5f2ff', glowColor: '#7a62ff' },
  subtitle: 'Launch Beyond the Lucky Stars',
  celebrationEffect: 'cosmic-streak',
  emblemImage: art.emblem,
  accentImage: art.accent,
  backdropImage: art.backdrop,
  visualsBackdropImage: art.backdrop,
  pageBackdropImage: art.backdrop,
  palette: {
    shellTop: '#172d85',
    shellBottom: '#05081e',
    panel: '#0b1648',
    trim: '#f2ca58',
    trimBright: '#fff2a0',
    accent: '#54d9ff',
    glow: '#d85cff',
    text: '#f5f8ff',
  },
}
