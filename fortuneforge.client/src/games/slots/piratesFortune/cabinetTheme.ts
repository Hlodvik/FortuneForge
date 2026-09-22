import goldPirateCoin from '../../../assets/slots/games/pirates-fortune/optimized/gold-pirate-coin.png'
import pearl from '../../../assets/slots/games/pirates-fortune/optimized/pearl.png'
import piratesCove from '../../../assets/slots/games/pirates-fortune/optimized/pirates-cove.png'
import type { SlotCabinetTheme } from '../../../features/slots/config/cabinetThemes'

export const PIRATES_FORTUNE_CABINET_THEME: SlotCabinetTheme = {
  id: 'pirates-fortune-moonlit-cove-v1',
  chrome: 'ornate',
  accessibleName: "Pirates' Fortune slot machine",
  eyebrow: 'Fortune Forge presents',
  title: "Pirates' Fortune",
  subtitle: 'Plunder the Moonlit Cove',
  emblemImage: pearl,
  accentImage: goldPirateCoin,
  backdropImage: piratesCove,
  pageBackdropImage: piratesCove,
  topbar: {
    background: 'radial-gradient(circle at 9% 34%, rgb(255 229 150 / 24%) 0 0.06rem, transparent 0.12rem), radial-gradient(circle at 28% 66%, rgb(255 210 110 / 20%) 0 0.08rem, transparent 0.16rem), radial-gradient(circle at 74% 30%, rgb(255 235 176 / 22%) 0 0.05rem, transparent 0.12rem), radial-gradient(circle at 91% 69%, rgb(255 207 100 / 20%) 0 0.08rem, transparent 0.16rem), linear-gradient(100deg, rgb(110 61 29 / 30%), rgb(77 42 20 / 30%) 48%, rgb(133 79 38 / 30%))',
    borderColor: 'rgb(255 219 145 / 38%)',
    shadowColor: 'rgb(40 20 8 / 30%)',
    accentColor: 'rgb(255 207 102 / 18%)',
  },
  palette: {
    shellTop: '#6b3517',
    shellBottom: '#241005',
    panel: '#160b06',
    trim: '#d99b3c',
    trimBright: '#ffe0a1',
    accent: '#e1a84a',
    glow: '#b55a22',
    text: '#fff1c5',
  },
}
