// Edited: 2026-11-18T14:51:01-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:27:28-06:00 | Computer: Vinnabok
import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_CABINET_THEME: SlotCabinetTheme = {
  id: 'arcane-archives-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Arcane Archives magic library slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Arcane Archives',
  subtitle: 'Unlock the Forbidden Stacks',
  celebrationEffect: 'arcane-orbit',
  paylineTrace: { color: '#f2cd79', glowColor: '#dfb3ff' },
  emblemImage: art.emblem,
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
