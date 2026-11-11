import { SolitaireBoard as Board, type SolitaireBoardProps } from '@fortuneforge/games-solitaire'
import { playCardAudio } from '../shared/cardAudio'

export function SolitaireBoard(props: Omit<SolitaireBoardProps, 'playCardAudio'>) {
  return <Board {...props} playCardAudio={playCardAudio} />
}
