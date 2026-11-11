import './styles.css'
import { createShowcaseSlotGame } from '@fortuneforge/slot-game-runtime/showcase'
import { CANDY_CARNIVAL_DEFINITION } from './definition'
import './candyCarnivalCabinet.css'

export const CANDY_CARNIVAL_SLOT_GAME = createShowcaseSlotGame(CANDY_CARNIVAL_DEFINITION)
