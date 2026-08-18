import pokerRoom from '../../../assets/slots/games/royal-draw/poker-room.svg'
import royalDrawEmblem from '../../../assets/slots/games/royal-draw/royal-draw-emblem.svg'
import rubyCardJewel from '../../../assets/slots/games/royal-draw/ruby-card-jewel.svg'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const ROYAL_DRAW_CABINET_THEME: SlotCabinetTheme = {
  id: 'royal-draw-cabinet-v1',
  chrome: 'simple',
  accessibleName: 'Royal Draw poker slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Royal Draw',
  subtitle: 'Sweep the High-Stakes Table',
  emblemImage: royalDrawEmblem,
  accentImage: rubyCardJewel,
  backdropImage: pokerRoom,
  visualsBackdropImage: pokerRoom,
  pageBackdropImage: pokerRoom,
  palette: {
    shellTop: '#53132b',
    shellBottom: '#130b18',
    panel: '#241025',
    trim: '#d6a53c',
    trimBright: '#fff0a3',
    accent: '#32d28a',
    glow: '#e93e69',
    text: '#fff8dc',
  },
}
