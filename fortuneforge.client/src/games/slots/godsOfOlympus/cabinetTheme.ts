import olympusEmblem from '../../../assets/slots/games/gods-of-olympus/olympus-emblem.png'
import olympusTerrace from '../../../assets/slots/games/gods-of-olympus/olympus-terrace.png'
import sapphireLaurelJewel from '../../../assets/slots/games/gods-of-olympus/sapphire-laurel-jewel.png'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const GODS_OF_OLYMPUS_CABINET_THEME: SlotCabinetTheme = {
  id: 'gods-of-olympus-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Gods of Olympus slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Gods of Olympus',
  subtitle: 'Claim the Divine Tribute',
  emblemImage: olympusEmblem,
  accentImage: sapphireLaurelJewel,
  backdropImage: olympusTerrace,
  visualsBackdropImage: olympusTerrace,
  pageBackdropImage: olympusTerrace,
  palette: {
    shellTop: '#2b377d',
    shellBottom: '#160c36',
    panel: '#1b1742',
    trim: '#e6b642',
    trimBright: '#fff1a3',
    accent: '#56b8ff',
    glow: '#b17cff',
    text: '#fff9df',
  },
}
