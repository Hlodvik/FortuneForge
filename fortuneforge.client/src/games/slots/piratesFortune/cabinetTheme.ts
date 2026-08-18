import captainCrest from '../../../assets/slots/games/pirates-fortune/captain-crest.png'
import piratesCove from '../../../assets/slots/games/pirates-fortune/pirates-cove.png'
import rubyGem from '../../../assets/slots/games/pirates-fortune/ruby-gem.png'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const PIRATES_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'pirates-fortune-moonlit-cove-v1',
  chrome: 'simple',
  accessibleName: "Pirates' Fortune slot machine",
  eyebrow: 'Fortune Forge presents',
  title: "Pirates' Fortune",
  subtitle: 'Plunder the Moonlit Cove',
  emblemImage: captainCrest,
  accentImage: rubyGem,
  backdropImage: piratesCove,
  visualsBackdropImage: piratesCove,
  pageBackdropImage: piratesCove,
  palette: {
    shellTop: '#132a3b',
    shellBottom: '#071019',
    panel: '#101d27',
    trim: '#bd7b25',
    trimBright: '#ffe7a3',
    accent: '#2dd4c8',
    glow: '#e4392f',
    text: '#fff1c5',
  },
}
