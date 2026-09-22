import olympusEmblem from '../../../assets/slots/games/gods-of-olympus/optimized/olympus-emblem.png'
import olympusTerrace from '../../../assets/slots/games/gods-of-olympus/optimized/olympus-terrace.png'
import sapphireLaurelJewel from '../../../assets/slots/games/gods-of-olympus/optimized/sapphire-laurel-jewel.png'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const GODS_OF_OLYMPUS_CABINET_THEME: SlotCabinetTheme = {
  id: 'gods-of-olympus-cabinet-v1',
  chrome: 'ornate',
  accessibleName: 'Gods of Olympus slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Gods of Olympus',
  subtitle: 'Claim the Divine Tribute',
  celebrationEffect: 'olympus-storm',
  emblemImage: olympusEmblem,
  accentImage: sapphireLaurelJewel,
  backdropImage: olympusTerrace,
  pageBackdropImage: olympusTerrace,
  topbar: {
    background: 'radial-gradient(circle at 11% 31%, rgb(255 237 177 / 24%) 0 0.06rem, transparent 0.14rem), radial-gradient(circle at 68% 65%, rgb(255 224 136 / 20%) 0 0.07rem, transparent 0.15rem), linear-gradient(104deg, rgb(37 55 126 / 32%), rgb(21 25 70 / 30%) 50%, rgb(101 61 126 / 30%))',
    borderColor: 'rgb(255 231 159 / 42%)',
    shadowColor: 'rgb(5 9 39 / 34%)',
    accentColor: 'rgb(255 218 124 / 18%)',
  },
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
