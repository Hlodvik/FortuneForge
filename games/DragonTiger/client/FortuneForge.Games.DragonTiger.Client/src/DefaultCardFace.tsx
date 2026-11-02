import { StandardCardFace } from '@fortuneforge/client-presentation'
import type { DragonTigerCard } from './contracts'
import './standardCardFaces.css'

/** Isolated package fallback; the connected site and local preview inject the shared PlayingCard. */
export function DefaultCardFace({ card }: { card: DragonTigerCard }) {
  return <StandardCardFace card={card} />
}
