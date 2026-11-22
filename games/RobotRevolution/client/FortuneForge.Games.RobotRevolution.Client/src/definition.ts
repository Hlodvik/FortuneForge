import type { SlotCabinetTheme } from '@fortuneforge/slot-client-core'
import { FIFTH_SUN_SYMBOL_IMAGES as artwork } from './symbols'

export const FIFTH_SUN_TITLE = 'Fifth Sun'
export const FIFTH_SUN_GAME_ID = 'robot-revolution-v1'
export const FIFTH_SUN_DESCRIPTION = 'Turn the ancient sunwheel, align three guardians, and reveal the hidden Eclipse.'

export const FIFTH_SUN_CABINET: SlotCabinetTheme = {
  id: 'robot-revolution-cabinet-v1', chrome: 'simple',
  accessibleName: 'Fifth Sun temple slot machine', eyebrow: 'Fortune Forge presents',
  title: FIFTH_SUN_TITLE, subtitle: 'Awaken the hidden sun', celebrationEffect: 'sunstone-corona',
  emblemImage: artwork.SUNSTONE, accentImage: artwork.ECLIPSE,
  backdropImage: artwork.REELBED, visualsBackdropImage: artwork.REELBED, pageBackdropImage: artwork.BACKDROP,
  paylineTrace: { color: '#efc66e', glowColor: '#c77d35' },
  palette: {
    shellTop: '#34271e', shellBottom: '#0e0d0d', panel: '#17120f', trim: '#ad8449',
    trimBright: '#f3d390', accent: '#d7ac64', glow: '#c78b41', text: '#fff1d4',
  },
}

export const FIFTH_SUN_DEFINITION = {
  id: 'robot-revolution', title: FIFTH_SUN_TITLE, subtitle: FIFTH_SUN_CABINET.subtitle,
  description: FIFTH_SUN_DESCRIPTION, serverGameId: FIFTH_SUN_GAME_ID,
  cabinet: FIFTH_SUN_CABINET,
}
export const SUNSTONE_ECLIPSE_DEFINITION = FIFTH_SUN_DEFINITION
export const ROBOT_REVOLUTION_DEFINITION = FIFTH_SUN_DEFINITION
