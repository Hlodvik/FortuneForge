import highNoonEmblem from '../../../assets/slots/games/high-noon-fortune/high-noon-emblem.svg'
import turquoiseJewel from '../../../assets/slots/games/high-noon-fortune/turquoise-jewel.svg'
import westernTown from '../../../assets/slots/games/high-noon-fortune/western-town.svg'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const HIGH_NOON_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'high-noon-fortune-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'High Noon Fortune western slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'High Noon Fortune',
  subtitle: 'Round Up the Frontier Gold',
  emblemImage: highNoonEmblem,
  accentImage: turquoiseJewel,
  backdropImage: westernTown,
  visualsBackdropImage: westernTown,
  pageBackdropImage: westernTown,
  palette: {
    shellTop: '#7a301c',
    shellBottom: '#2b1512',
    panel: '#3b2119',
    trim: '#dca13b',
    trimBright: '#ffe4a0',
    accent: '#3ed5c4',
    glow: '#ee6b2f',
    text: '#fff0cf',
  },
}
