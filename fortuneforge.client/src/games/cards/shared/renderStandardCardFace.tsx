import { PlayingCard } from './PlayingCard'
import type { CardRank, CardSuit } from './cards'

const ranks = { ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13 } as const

/** Host-owned adapter for package card contracts; never supplies hidden cards. */
export function renderStandardCardFace(card: { rank: keyof typeof ranks; suit: CardSuit }) {
  return <PlayingCard card={{ id: `${card.rank}-${card.suit}`, rank: ranks[card.rank] as CardRank, suit: card.suit }} />
}
