import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'
import { COSMIC_FORTUNE_VISUALS as art } from './visuals'

export const COSMIC_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'pharaohs-ascent-cabinet-v1', chrome: 'simple',
  accessibleName: 'Pharaoh’s Ascent Egyptian slot machine',
  eyebrow: 'Fortune Forge presents', title: 'Pharaoh’s Ascent',
  paylineTrace: { color: '#ffe1a0', glowColor: '#e1a84b' },
  subtitle: 'Four guardians. One ancient ascent.', celebrationEffect: 'pharaoh-gold',
  emblemImage: art.pharaoh, accentImage: art.scarab,
  backdropImage: art.backdrop, visualsBackdropImage: art.backdrop, pageBackdropImage: art.backdrop,
  palette: {
    shellTop: '#153e3c', shellBottom: '#071c21', panel: '#071e23',
    trim: '#b99552', trimBright: '#f9dea0', accent: '#70d8c6', glow: '#d7a24e', text: '#fff0ce',
  },
}
