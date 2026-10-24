import { CourtCardSymbol } from './CourtCardSymbol'
import { PlayingCard } from './PlayingCard'

/** Optional unsuited joker artwork; never inserted into a standard game deck. */
export function JokerCard({ color = 'black', faceDown = false }: { color?: 'black' | 'red'; faceDown?: boolean }) {
  if (faceDown) return <PlayingCard card={{ id: 'hidden-joker', rank: 1, suit: 'spades' }} faceDown />
  return <div className={`ff-playing-card ff-playing-card--face ff-playing-card--${color}`} aria-label={`${color === 'red' ? 'Red' : 'Black'} joker`}>
    <span className="ff-playing-card__corner ff-playing-card__corner--joker"><b>JOKER</b></span>
    <span className="ff-playing-card__center ff-playing-card__center--court" aria-hidden="true"><CourtCardSymbol rank="joker" /></span>
    <span className="ff-playing-card__corner ff-playing-card__corner--bottom ff-playing-card__corner--joker" aria-hidden="true"><b>JOKER</b></span>
  </div>
}
