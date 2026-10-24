import type { DragonTigerCard } from './contracts'
import './standardCardFaces.css'

const symbols = { clubs:'♣', diamonds:'♦', hearts:'♥', spades:'♠' } as const
const ranks = { ace:'A', two:'2', three:'3', four:'4', five:'5', six:'6', seven:'7', eight:'8', nine:'9', ten:'10', jack:'J', queen:'Q', king:'K' } as const

/** Isolated package fallback; the connected site and local preview inject the shared PlayingCard. */
export function DefaultCardFace({ card }: { card: DragonTigerCard }) {
  const symbol = symbols[card.suit]
  const rank = ranks[card.rank]
  const court = card.rank === 'jack' || card.rank === 'queen' || card.rank === 'king'
  const color = card.suit === 'hearts' || card.suit === 'diamonds' ? 'red' : 'black'
  const name = card.rank.charAt(0).toUpperCase() + card.rank.slice(1)
  return <div className={`ff-playing-card ff-playing-card--standalone ff-playing-card--face ff-playing-card--${color}`} aria-label={`${name} of ${card.suit}`}>
    <span className="ff-playing-card__corner"><b>{rank}</b><i>{symbol}</i></span>
    <span className={`ff-playing-card__center${court ? ' ff-playing-card__center--court' : ''}`} aria-hidden="true">{court && <b>{rank}</b>}<i>{symbol}</i></span>
    <span className="ff-playing-card__corner ff-playing-card__corner--bottom" aria-hidden="true"><b>{rank}</b><i>{symbol}</i></span>
  </div>
}
