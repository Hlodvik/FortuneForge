import rainbowOrchard from '../../../assets/slots/backgrounds/rainbow-realm-prismatic-orchard-v3-base.png'
import rainbowAppleMedallion from '../../../assets/slots/games/rainbow-realm/rainbow-apple-medallion.png'
import cherrySymbol from '../../../assets/slots/symbols/cherry.gif'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const RAINBOW_REALM_CABINET_THEME: SlotCabinetTheme = {
  id: 'rainbow-realm-fruit-arcade-v1',
  chrome: 'simple',
  accessibleName: 'Rainbow Realm fruit slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Rainbow Realm',
  subtitle: 'Fruit Frenzy',
  emblemImage: cherrySymbol,
  accentImage: rainbowAppleMedallion,
  backdropImage: rainbowOrchard,
  visualsBackdropImage: rainbowOrchard,
  pageBackdropImage: rainbowOrchard,
  palette: {
    shellTop: '#ff5d46',
    shellBottom: '#4f1a62',
    panel: '#24124d',
    trim: '#ffd95b',
    trimBright: '#fff8b8',
    accent: '#40cfff',
    glow: '#ff85b7',
    text: '#fff8dc',
  },
}
