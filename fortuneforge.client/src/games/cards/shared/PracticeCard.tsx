import { PlayingCard } from './PlayingCard'
import type { CardRank, CardSuit } from './cards'

export type PracticeCardModel = Readonly<{
  rank: string | null
  suit: CardSuit | null
  hidden: boolean
}>

export function PracticeCard({ card, index }: { card: PracticeCardModel; index: number }) {
  if (card.hidden || card.rank === null || card.suit === null) {
    return <PlayingCard card={{ id: `hidden-${index}`, rank: 1, suit: 'spades' }} faceDown />
  }
  return (
    <PlayingCard card={{
      id: `${card.suit}-${card.rank}-${index}`,
      rank: rankValue(card.rank),
      suit: card.suit,
    }} />
  )
}

function rankValue(rank: string): CardRank {
  if (rank === 'A') return 1
  if (rank === 'J') return 11
  if (rank === 'Q') return 12
  if (rank === 'K') return 13
  const numeric = Number(rank)
  return Number.isInteger(numeric) && numeric >= 2 && numeric <= 10
    ? numeric as CardRank
    : 1
}
