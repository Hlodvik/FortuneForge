import neonJewelCloudsGold from './assets/art/background/neon-jewel-clouds-gold.png'
import wukongMedallion from './assets/art/symbols/wukong/wukong-medallion.png'
import wukongPowerSeal from './assets/art/symbols/wukong/wukong-power-seal.png'
import staff from './assets/art/symbols/wukong/celestial-staff.png'
import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'
import './wukongCabinet.css'

export const WUKONG_CABINET_THEME: SlotCabinetTheme = {
  id: 'wukong-celestial-arcade-v1',
  chrome: 'simple',
  accessibleName: "Wukong's Journey to the West slot machine",
  eyebrow: 'Fortune Forge presents',
  title: "Wukong's Journey",
  subtitle: 'To the West',
  emblemImage: wukongMedallion,
  accentImage: wukongPowerSeal,
  spinIconImage: staff,
  controlLayout: 'integrated',
  backdropImage: neonJewelCloudsGold,
  visualsBackdropImage: neonJewelCloudsGold,
  pageBackdropImage: neonJewelCloudsGold,
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
