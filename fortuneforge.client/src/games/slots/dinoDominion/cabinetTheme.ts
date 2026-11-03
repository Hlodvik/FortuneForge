import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'
import wordmark from '../../../assets/slots/games/gold-foundry/optimized/gold-foundry-wordmark-v1.webp'
import { GOLD_FOUNDRY_VISUALS as art } from './visuals'
import './goldFoundryCabinet.css'

export const DINO_DOMINION_CABINET_THEME: SlotCabinetTheme = {
  id: 'gold-foundry-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Gold Foundry slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Gold Foundry',
  titleArtwork: { src: wordmark, width: 960, height: 126 },
  subtitle: 'Cast molten gold',
  celebrationEffect: 'foundry-sparks',
  emblemImage: art.smithMask,
  accentImage: art.goldIngots,
  backdropImage: art.backdrop,
  visualsBackdropImage: art.reelBacking,
  pageBackdropImage: art.backdrop,
  palette: {
    shellTop: '#3b2c25',
    shellBottom: '#171314',
    panel: '#242a30',
    trim: '#d39142',
    trimBright: '#ffe0a3',
    accent: '#e5a161',
    glow: '#f1793e',
    text: '#fff1ce',
  },
}
