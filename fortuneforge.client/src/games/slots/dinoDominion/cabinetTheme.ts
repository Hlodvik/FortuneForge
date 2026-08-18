import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'
import { DINO_DOMINION_VISUALS as art } from './visuals'

export const DINO_DOMINION_CABINET_THEME: SlotCabinetTheme = {
  id: 'dino-dominion-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Dino Dominion prehistoric slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Dino Dominion',
  subtitle: 'Unearth a Prehistoric Fortune',
  emblemImage: art.emblem,
  accentImage: art.accent,
  backdropImage: art.backdrop,
  visualsBackdropImage: art.backdrop,
  pageBackdropImage: art.backdrop,
  palette: {
    shellTop: '#43622b',
    shellBottom: '#171d14',
    panel: '#2b2b1b',
    trim: '#e0ad4c',
    trimBright: '#fff0a3',
    accent: '#6ed6c3',
    glow: '#f1793e',
    text: '#fff1ce',
  },
}
