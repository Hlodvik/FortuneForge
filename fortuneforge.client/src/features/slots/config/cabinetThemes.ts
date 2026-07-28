import neonJewelCloudsGold from '../../../assets/slots/backgrounds/neon-jewel-clouds-gold.png'
import rainbowOrchard from '../../../assets/slots/backgrounds/rainbow-realm-prismatic-orchard-v3-base.png'
import cherrySymbol from '../../../assets/slots/symbols/cherry.gif'
import rainbowPowerCoin from '../../../assets/slots/symbols/rainbow-realm-power-coin.png'
import wukongMedallion from '../../../assets/slots/symbols/wukong/wukong-medallion.png'
import wukongPowerSeal from '../../../assets/slots/symbols/wukong/wukong-power-seal.png'

export type SlotCabinetPalette = {
  shellTop: string
  shellBottom: string
  panel: string
  trim: string
  trimBright: string
  accent: string
  glow: string
  text: string
}

export type SlotCabinetTheme = {
  id: string
  chrome: 'ornate' | 'simple'
  accessibleName: string
  eyebrow: string
  title: string
  subtitle: string
  emblemImage: string
  accentImage?: string
  backdropImage?: string
  visualsBackdropImage?: string
  pageBackdropImage?: string
  palette: SlotCabinetPalette
}

// Cabinet presentation lives beside the rest of the experience configuration.
// A future slot can provide its own copy, art, and palette without changing the
// shared SlotGameFrame or SlotMachine components.
export const WUKONG_CABINET_THEME: SlotCabinetTheme = {
  id: 'wukong-celestial-arcade-v1',
  chrome: 'simple',
  accessibleName: "Wukong's Journey to the West slot machine",
  eyebrow: 'Fortune Forge presents',
  title: "Wukong's Journey",
  subtitle: 'To the West',
  emblemImage: wukongMedallion,
  accentImage: wukongPowerSeal,
  backdropImage: neonJewelCloudsGold,
  palette: {
    shellTop: '#8f1830',
    shellBottom: '#240716',
    panel: '#4b0713',
    trim: '#f6b92f',
    trimBright: '#fff0a6',
    accent: '#20d9cf',
    glow: '#ff6a2f',
    text: '#fff8d8',
  },
}

export const RAINBOW_REALM_CABINET_THEME: SlotCabinetTheme = {
  id: 'rainbow-realm-fruit-arcade-v1',
  chrome: 'simple',
  accessibleName: 'Rainbow Realm fruit slot machine',
  eyebrow: 'Fortune Forge presents',
  title: 'Rainbow Realm',
  subtitle: 'Fruit Frenzy',
  emblemImage: cherrySymbol,
  accentImage: rainbowPowerCoin,
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
