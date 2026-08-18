import neonJewelCloudsGold from '../../../assets/slots/backgrounds/neon-jewel-clouds-gold.png'
import wukongMedallion from '../../../assets/slots/symbols/wukong/wukong-medallion.png'
import wukongPowerSeal from '../../../assets/slots/symbols/wukong/wukong-power-seal.png'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

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
