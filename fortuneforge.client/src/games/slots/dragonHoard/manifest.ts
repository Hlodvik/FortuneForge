import { createShowcaseSlotGame } from '../shared/createShowcaseSlotGame'
import { DRAGON_HOARD_DEFINITION } from './definition'
import './midnightVaultCelebration.css'
import './midnightVaultLayout.css'

const game = createShowcaseSlotGame(DRAGON_HOARD_DEFINITION)

export const DRAGON_HOARD_SLOT_GAME = {
  ...game,
  experience: {
    ...game.experience,
    symbols: {
      ...game.experience.symbols,
      animationSymbolIds: game.experience.symbols.guideEntries.map(entry => entry.symbol),
    },
  },
}
