import { createRoot } from 'react-dom/client'
import { DragonTigerGame } from '../DragonTigerGame'
import type { DragonTigerCard } from '../contracts'
import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'
import type { CardRank } from '../../../../../../fortuneforge.client/src/games/cards/shared/cards'
import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'
import './preview.css'

const cardRanks: Readonly<Record<DragonTigerCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: DragonTigerCard) => <PlayingCard card={{
  id: `dragon-tiger-${card.rank}-${card.suit}`,
  rank: cardRanks[card.rank],
  suit: card.suit,
}}/>

createRoot(document.getElementById('root')!).render(<main><DragonTigerGame playerId="standalone-practice" renderCardFace={renderCardFace}/></main>)
