import { cardLabel, type PlayingCardModel } from './cards'

const suitSymbols = {
  clubs: '♣',
  diamonds: '♦',
  hearts: '♥',
  spades: '♠',
} as const

const rankSymbols: Record<PlayingCardModel['rank'], string> = {
  1: 'A',
  2: '2',
  3: '3',
  4: '4',
  5: '5',
  6: '6',
  7: '7',
  8: '8',
  9: '9',
  10: '10',
  11: 'J',
  12: 'Q',
  13: 'K',
}

export function PlayingCard({
  card,
  faceDown = false,
}: {
  card: PlayingCardModel
  faceDown?: boolean
}) {
  if (faceDown) {
    return (
      <div className="ff-playing-card ff-playing-card--back" aria-label="Face-down card">
        <span className="ff-playing-card__back-frame">
          <i />
          <b>FF</b>
        </span>
      </div>
    )
  }

  const symbol = suitSymbols[card.suit]
  const rank = rankSymbols[card.rank]
  const color = card.suit === 'hearts' || card.suit === 'diamonds' ? 'red' : 'black'

  return (
    <div className={`ff-playing-card ff-playing-card--face ff-playing-card--${color}`} aria-label={cardLabel(card)}>
      <span className="ff-playing-card__corner">
        <b>{rank}</b>
        <i>{symbol}</i>
      </span>
      <span className="ff-playing-card__center" aria-hidden="true">
        <i>{symbol}</i>
        {card.rank >= 11 && <b>{rank}</b>}
      </span>
      <span className="ff-playing-card__corner ff-playing-card__corner--bottom" aria-hidden="true">
        <b>{rank}</b>
        <i>{symbol}</i>
      </span>
    </div>
  )
}
