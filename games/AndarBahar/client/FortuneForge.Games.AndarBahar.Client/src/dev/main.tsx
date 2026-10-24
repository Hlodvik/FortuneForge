import { createRoot } from 'react-dom/client'
import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/app/routes/AndarBaharRoute.css'
import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'
import type { CardRank } from '../../../../../../fortuneforge.client/src/games/cards/shared/cards'
import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'
import { AndarBaharGame } from '../AndarBaharGame'
import type { AndarBaharCard } from '../contracts'
import './preview.css'

const cardRanks: Readonly<Record<AndarBaharCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: AndarBaharCard) => <PlayingCard card={{
  id: `andar-bahar-preview-${card.rank}-${card.suit}`, rank: cardRanks[card.rank], suit: card.suit,
}} />

// Monorepo-only preview: reuse the actual host chrome rather than a second navbar.
// No account is fabricated. The sample API remains development-practice only.
createRoot(document.getElementById('root')!).render(
  <InGameShell theme="casino" className="andar-bahar-page" bodyClassName="andar-bahar-route__body">
    <AndarBaharGame playerId="standalone-practice" renderCardFace={renderCardFace}/>
  </InGameShell>,
)
