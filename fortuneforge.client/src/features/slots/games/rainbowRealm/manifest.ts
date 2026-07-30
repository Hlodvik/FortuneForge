import cherrySymbol from '../../../../assets/slots/symbols/cherry.gif'
import { RAINBOW_REALM_MASCOT } from '../../../../components/WukongCompanion.config'
import { RAINBOW_REALM_CABINET_THEME } from '../../config/cabinetThemes'
import { DEFAULT_SLOT_SOUNDS } from '../../config/soundSets'
import { createSlotRulesSet, type SlotExperienceSet } from '../../config/slotExperienceSets'
import { RAINBOW_REALM_SYMBOLS } from '../../config/symbolSets'
import { defineSlotGame } from '../slotGameManifest'

export const RAINBOW_REALM_EXPERIENCE_SET: SlotExperienceSet = {
  id: 'rainbow-realm-fruits-v1',
  cabinet: RAINBOW_REALM_CABINET_THEME,
  features: {
    energy: {
      label: 'Energy',
      symbol: 'BOLT',
    },
  },
  help: {
    paylineCount: 23,
    freeGames: {
      requiredSymbols: 3,
      awardedSpins: 5,
    },
  },
  shellBackdrop: 'theme',
  symbols: RAINBOW_REALM_SYMBOLS,
  mascot: RAINBOW_REALM_MASCOT,
  sounds: DEFAULT_SLOT_SOUNDS,
  rules: createSlotRulesSet('rainbow-realm-fruits-v1'),
}

export const RAINBOW_REALM_SLOT_GAME = defineSlotGame({
  id: 'rainbow-realm',
  routes: {
    play: '/slots/rainbow-realm',
    demo: '/slots/rainbow-realm/demo',
  },
  catalog: {
    id: 'rainbow-realm',
    title: 'Rainbow Realm',
    shortTitle: 'Rainbow Realm',
    description: 'A bright return to the classic fruit symbols, led by the lucky cherry.',
    image: cherrySymbol,
    imagePresentation: 'contain',
  },
  experience: RAINBOW_REALM_EXPERIENCE_SET,
})
