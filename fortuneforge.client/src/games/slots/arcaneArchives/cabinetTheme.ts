import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_CABINET_THEME: SlotCabinetTheme = {
  id: 'arcane-archives-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Arcane Archives magic library slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Arcane Archives',
  subtitle: 'Unlock the Forbidden Stacks',
  emblemImage: art.emblem,
  accentImage: art.accent,
  backdropImage: art.backdrop,
  visualsBackdropImage: art.backdrop,
  pageBackdropImage: art.backdrop,
  palette: {
    shellTop: '#4d2474',
    shellBottom: '#120a28',
    panel: '#25113d',
    trim: '#e4bd58',
    trimBright: '#fff4b2',
    accent: '#72ead7',
    glow: '#c084fc',
    text: '#fff5d6',
  },
}
